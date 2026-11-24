using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.DatabaseServices;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;
using AutoCAD.Gateway.Core.Models;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Thread-safe AutoCAD database and UI context inspection service running on AutoCAD's main thread.
/// </summary>
public sealed class AutoCADContextReader : IAutoCADContextReader
{
    /// <inheritdoc/>
    public Task<string> ReadSelectionCountAsync() =>
        Acad.RunOnDocumentAsync((doc, _) =>
        {
            var selection = doc.Editor.SelectImplied();
            int count = selection.Status == Autodesk.AutoCAD.EditorInput.PromptStatus.OK
                ? selection.Value.GetObjectIds().Length
                : 0;

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                selection_count = count,
            }, GatewayProtocol.WireJsonOptions);
        });

    /// <inheritdoc/>
    public Task<string> ReadEntityCountsAsync() =>
        Acad.Read(ctx =>
        {
            var entityCounts = new Dictionary<string, int>();
            var bt = (BlockTable)ctx.Tr.GetObject(ctx.Db.BlockTableId, OpenMode.ForRead);
            var btr = (BlockTableRecord)ctx.Tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId objId in btr)
            {
                if (objId.IsNull || objId.IsErased)
                {
                    continue;
                }

                if (ctx.Tr.GetObject(objId, OpenMode.ForRead, false) is Entity ent)
                {
                    string type = ent.GetType().Name;
                    entityCounts[type] = entityCounts.TryGetValue(type, out int c) ? c + 1 : 1;
                }
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                data = entityCounts,
            }, GatewayProtocol.WireJsonOptions);
        });

    /// <inheritdoc/>
    public Task<string> ReadDrawingInfoAsync() =>
        Acad.RunOnDocumentAsync((doc, _) =>
        {
            var info = new DrawingInfo
            {
                Name = doc.Name,
                IsReadOnly = doc.IsReadOnly,
                Units = doc.Database.Insunits.ToString(),
            };

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                data = info,
            }, GatewayProtocol.WireJsonOptions);
        });

    /// <inheritdoc/>
    public Task<string> ReadLayersAsync() =>
        Acad.Read(ctx =>
        {
            var list = new List<LayerInfo>();
            var lt = (LayerTable)ctx.Tr.GetObject(ctx.Db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in lt)
            {
                if (id.IsNull || id.IsErased)
                {
                    continue;
                }

                if (ctx.Tr.GetObject(id, OpenMode.ForRead, false) is LayerTableRecord ltr)
                {
                    list.Add(new LayerInfo
                    {
                        Name = ltr.Name,
                        IsLocked = ltr.IsLocked,
                        IsFrozen = ltr.IsFrozen,
                        IsOff = ltr.IsOff,
                        Color = ltr.Color.ColorIndex,
                    });
                }
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                data = list,
            }, GatewayProtocol.WireJsonOptions);
        });

    /// <inheritdoc/>
    public Task<string> ReadExtentsAsync() =>
        Acad.RunOnDocumentAsync((doc, _) =>
        {
            var extmin = doc.Database.Extmin;
            var extmax = doc.Database.Extmax;
            var extents = new DrawingExtentsInfo
            {
                MinX = Math.Round(extmin.X, 4),
                MinY = Math.Round(extmin.Y, 4),
                MinZ = Math.Round(extmin.Z, 4),
                MaxX = Math.Round(extmax.X, 4),
                MaxY = Math.Round(extmax.Y, 4),
                MaxZ = Math.Round(extmax.Z, 4),
            };

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                data = extents,
            }, GatewayProtocol.WireJsonOptions);
        });

    /// <inheritdoc/>
    public Task<string> ReadTextsAsync() =>
        Acad.Read(ctx =>
        {
            var list = new List<TextEntityInfo>();
            var bt = (BlockTable)ctx.Tr.GetObject(ctx.Db.BlockTableId, OpenMode.ForRead);
            var btr = (BlockTableRecord)ctx.Tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId objId in btr)
            {
                if (objId.IsNull || objId.IsErased)
                {
                    continue;
                }

                DBObject ent = ctx.Tr.GetObject(objId, OpenMode.ForRead, false);
                if (ent is DBText dbText)
                {
                    list.Add(new TextEntityInfo
                    {
                        Type = "DBText",
                        Text = dbText.TextString,
                        X = Math.Round(dbText.Position.X, 4),
                        Y = Math.Round(dbText.Position.Y, 4),
                        Layer = dbText.Layer,
                    });
                }
                else if (ent is MText mText)
                {
                    list.Add(new TextEntityInfo
                    {
                        Type = "MText",
                        Text = mText.Contents,
                        X = Math.Round(mText.Location.X, 4),
                        Y = Math.Round(mText.Location.Y, 4),
                        Layer = mText.Layer,
                    });
                }
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                data = list,
            }, GatewayProtocol.WireJsonOptions);
        });

    /// <inheritdoc/>
    public Task<string> ReadBlocksAsync() =>
        Acad.Read(ctx =>
        {
            var list = new List<string>();
            var bt = (BlockTable)ctx.Tr.GetObject(ctx.Db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId objId in bt)
            {
                if (objId.IsNull || objId.IsErased)
                {
                    continue;
                }

                if (ctx.Tr.GetObject(objId, OpenMode.ForRead, false) is BlockTableRecord btr &&
                    !btr.IsAnonymous &&
                    !btr.IsLayout)
                {
                    list.Add(btr.Name);
                }
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                data = list,
            }, GatewayProtocol.WireJsonOptions);
        });

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <inheritdoc/>
    public Task<string> CaptureScreenshotAsync(bool inline = false) =>
        Acad.RunOnDocumentAsync((doc, _) =>
        {
            IntPtr hwnd = Autodesk.AutoCAD.ApplicationServices.Application.MainWindow?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero && doc.Window != null)
            {
                hwnd = doc.Window.Handle;
            }

            int width;
            int height;
            int left;
            int top;

            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out RECT rect))
            {
                width = rect.Right - rect.Left;
                height = rect.Bottom - rect.Top;
                left = rect.Left;
                top = rect.Top;
            }
            else
            {
                var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
                width = bounds.Width;
                height = bounds.Height;
                left = bounds.Left;
                top = bounds.Top;
            }

            if (width <= 0 || height <= 0)
            {
                throw new AcadGatewayException("autocad_error", "Could not determine AutoCAD window dimensions.");
            }

            using var bitmap = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(left, top, 0, 0, new Size(width, height));
            }

            string outputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoCAD.Gateway",
                "screenshots");
            DatabaseHelpers.EnsureNoReparsePoints(outputDir);
            Directory.CreateDirectory(outputDir);
            DatabaseHelpers.EnsureNoReparsePoints(outputDir);
            string suffix = Guid.NewGuid().ToString("N")[..4];
            string fileName = $"autocad_capture_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{suffix}.png";
            string fullPath = Path.Combine(outputDir, fileName);
            using (var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                bitmap.Save(fs, ImageFormat.Png);
            }

            string? imageBase64 = null;
            string? mimeType = null;
            string? inlineOmittedReason = null;
            if (inline)
            {
                long byteLength = new FileInfo(fullPath).Length;
                if (byteLength <= SecurityPolicy.MaxScreenshotInlineBytes)
                {
                    byte[] pngBytes = File.ReadAllBytes(fullPath);
                    imageBase64 = Convert.ToBase64String(pngBytes);
                    mimeType = "image/png";
                }
                else
                {
                    inlineOmittedReason = $"Screenshot size ({byteLength} bytes) exceeds inline cap ({SecurityPolicy.MaxScreenshotInlineBytes} bytes).";
                }
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                file_path = fullPath.Replace('\\', '/'),
                width,
                height,
                mime_type = mimeType,
                image_base64 = imageBase64,
                inline_omitted_reason = inlineOmittedReason,
                timestamp = DateTime.UtcNow.ToString("o"),
            }, GatewayProtocol.WireJsonOptions);
        });
}
