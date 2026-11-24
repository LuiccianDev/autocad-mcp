using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AutoCAD.Gateway.Contracts;

/// <summary>
/// Manages per-process Gateway instance files in <see cref="GatewayProtocol.InstanceDirectory"/>
/// for automatic port and session-token discovery without manual configuration.
/// </summary>
public static class InstanceDiscovery
{
    /// <summary>Maximum allowed size in bytes for a discovery descriptor JSON file.</summary>
    public const long MaxDescriptorBytes = 16 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static bool IsReparsePoint(FileSystemInfo info) =>
        (info.Attributes & FileAttributes.ReparsePoint) != 0;

    /// <summary>Returns the instance descriptor file path for a given process id.</summary>
    public static string GetInstanceFilePath(int pid, string? directory = null)
    {
        string dir = ResolveDir(directory);
        return Path.Combine(dir, $"{pid}.json");
    }

    /// <summary>
    /// Atomically publishes the instance descriptor and prunes descriptors belonging to exited processes.
    /// </summary>
    public static string Publish(
        InstanceDescriptor descriptor,
        string? directory = null,
        Func<int, bool>? isProcessAlive = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Pid <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "PID must be positive.");
        }

        if (descriptor.Port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "Port must be between 1 and 65535.");
        }

        string dir = ResolveDir(directory);
        var dirInfo = Directory.CreateDirectory(dir);
        if (IsReparsePoint(dirInfo))
        {
            throw new IOException($"Instance discovery directory '{dir}' cannot be a symbolic link or junction.");
        }

        PruneDeadInstances(dir, descriptor.Pid, isProcessAlive);

        string target = Path.Combine(dir, $"{descriptor.Pid}.json");
        string temp = Path.Combine(dir, $".{descriptor.Pid}.{Guid.NewGuid():N}.tmp");
        string json = JsonSerializer.Serialize(descriptor, JsonOptions);

        try
        {
            File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch
            {
                // Best-effort cleanup of temp file
            }
        }

        return target;
    }

    /// <summary>Deletes the descriptor file for the given PID if present.</summary>
    public static void Remove(int pid, string? directory = null)
    {
        try
        {
            string path = GetInstanceFilePath(pid, directory);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup errors on shutdown
        }
    }

    /// <summary>
    /// Removes stale descriptor files whose PID no longer corresponds to a live process.
    /// </summary>
    public static int PruneDeadInstances(
        string? directory = null,
        int? keepPid = null,
        Func<int, bool>? isProcessAlive = null)
    {
        string dir = ResolveDir(directory);
        var dirInfo = new DirectoryInfo(dir);
        if (!dirInfo.Exists || IsReparsePoint(dirInfo))
        {
            return 0;
        }

        Func<int, bool> probe = isProcessAlive ?? DefaultIsProcessAlive;
        int removed = 0;

        foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            if (!int.TryParse(stem, out int pid))
            {
                continue;
            }

            if (keepPid.HasValue && pid == keepPid.Value)
            {
                continue;
            }

            if (!probe(pid))
            {
                try
                {
                    var fi = new FileInfo(file);
                    if (fi.Exists && !IsReparsePoint(fi))
                    {
                        File.Delete(file);
                        removed++;
                    }
                }
                catch (IOException)
                {
                    // Ignore locked or already deleted files
                }
                catch (UnauthorizedAccessException)
                {
                    // Ignore transient permissions race during cleanup
                }
            }
        }

        return removed;
    }

    /// <summary>
    /// Returns the newest live Gateway instance descriptor, or null if none exist.
    /// </summary>
    public static InstanceDescriptor? FindNewestLive(
        string? directory = null,
        Func<int, bool>? isProcessAlive = null)
    {
        var live = EnumerateLiveDescriptors(directory, isProcessAlive);
        return live.Count == 0 ? null : live[0];
    }

    /// <summary>
    /// Returns the newest live Gateway instance bound to <paramref name="port"/>, or null if none match.
    /// </summary>
    public static InstanceDescriptor? FindLiveByPort(
        int port,
        string? directory = null,
        Func<int, bool>? isProcessAlive = null)
    {
        var live = EnumerateLiveDescriptors(directory, isProcessAlive);
        return live.FirstOrDefault(d => d.Port == port);
    }

    private static List<InstanceDescriptor> EnumerateLiveDescriptors(
        string? directory,
        Func<int, bool>? isProcessAlive)
    {
        string dir = ResolveDir(directory);
        var dirInfo = new DirectoryInfo(dir);
        if (!dirInfo.Exists || IsReparsePoint(dirInfo))
        {
            return [];
        }

        Func<int, bool> probe = isProcessAlive ?? DefaultIsProcessAlive;
        var candidates = new List<(DateTime Timestamp, InstanceDescriptor Descriptor)>();

        foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            if (!int.TryParse(stem, out int pid) || !probe(pid))
            {
                continue;
            }

            try
            {
                var fi = new FileInfo(file);
                if (!fi.Exists || IsReparsePoint(fi) || fi.Length > MaxDescriptorBytes)
                {
                    continue;
                }

                string raw = File.ReadAllText(file, Encoding.UTF8);
                var desc = JsonSerializer.Deserialize<InstanceDescriptor>(raw);
                if (desc == null || desc.Pid != pid || desc.Port is <= 0 or > 65535 || string.IsNullOrEmpty(desc.Token))
                {
                    continue;
                }

                bool hasStartedUtc = DateTime.TryParse(
                    desc.StartedUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out DateTime parsedStarted);

                if (isProcessAlive is null && hasStartedUtc && IsStaleReusedPid(pid, parsedStarted))
                {
                    continue;
                }

                DateTime stamp = hasStartedUtc ? parsedStarted : File.GetLastWriteTimeUtc(file);
                candidates.Add((stamp, desc));
            }
            catch
            {
                // Skip unreadable or malformed descriptor files
            }
        }

        return candidates.OrderByDescending(c => c.Timestamp).Select(c => c.Descriptor).ToList();
    }

    private static string ResolveDir(string? directory) =>
        string.IsNullOrWhiteSpace(directory) ? GatewayProtocol.InstanceDirectory : directory;

    private static bool IsStaleReusedPid(int pid, DateTime descriptorStartedUtc)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            DateTime procStartUtc = proc.StartTime.ToUniversalTime();
            // A process started more than 5 seconds AFTER the descriptor was written cannot own that descriptor.
            return procStartUtc > descriptorStartedUtc.AddSeconds(5);
        }
        catch
        {
            return false;
        }
    }

    private static bool DefaultIsProcessAlive(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            using var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return true;
        }
    }
}
