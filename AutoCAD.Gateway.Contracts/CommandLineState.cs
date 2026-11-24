namespace AutoCAD.Gateway.Contracts;

/// <summary>
/// Pure state model describing AutoCAD's command line after evaluating AutoLISP or commands.
/// Kept AutoCAD-free so all classification rules are unit-tested offline.
/// </summary>
public sealed record CommandLineState(
    string State,
    string? PendingCommand,
    int? CmdActive,
    string Hint)
{
    /// <summary>A command or dialog remained active after execution.</summary>
    public const string Pending = "pending";

    /// <summary>Command-line state could not be read within the bounded wait.</summary>
    public const string Unknown = "unknown";

    /// <summary>CMDACTIVE bit indicating an open modal dialog that Ctrl+C cannot dismiss.</summary>
    public const int DialogBit = 8;

    /// <summary>
    /// Classifies CMDACTIVE and CMDNAMES values; returns null when the command line is idle.
    /// </summary>
    public static CommandLineState? FromSystemVariables(int cmdActive, string? cmdNames)
    {
        if (cmdActive == 0)
        {
            return null;
        }

        string? names = string.IsNullOrWhiteSpace(cmdNames) ? null : cmdNames.Trim();
        string hint = (cmdActive & DialogBit) != 0
            ? $"A dialog is open (CMDACTIVE {cmdActive}{(names is null ? string.Empty : $", CMDNAMES '{names}'")}). " +
              "Ctrl+C on the command line cannot dismiss dialogs; close it in AutoCAD."
            : $"{(names is null ? "A command" : names)} is still waiting at its prompt (CMDACTIVE {cmdActive}), " +
              "so subsequent writes will report 'busy' until cleared. Call cancel_command or supply remaining inputs.";

        return new CommandLineState(Pending, names, cmdActive, hint);
    }

    /// <summary>
    /// Builds an unknown state result when CMDACTIVE could not be queried within <paramref name="waitedMs"/>.
    /// </summary>
    public static CommandLineState NotRead(int waitedMs) => new(
        Unknown,
        null,
        null,
        $"CMDACTIVE could not be read within {waitedMs} ms; an interactive drag prompt may be delaying idle ticks. " +
        "If the next call returns 'busy', call cancel_command.");
}
