using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutoCAD.Gateway.Contracts;
using AutoCAD.Gateway.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Native ObjectARX annotation, hatch, multileader, and table drafting service inside atomic <c>Acad.Write</c> transactions.
/// </summary>
public static class DraftingService
{
    /// <summary>Creates a native aligned, rotated, radial, diametric, or angular dimension entity.</summary>
    /// <param name="body">JSON payload describing dimension type, definition points, style, override text, and entity metadata.</param>
    /// <returns>Serialized JSON confirmation with entity handle and revision.</returns>
    /// <exception cref="AcadGatewayException">Thrown when required points, dimension type, or style are invalid.</exception>
    public static Task<string> CreateDimensionAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string dimType = DatabaseHelpers.GetRequiredString(root, "type").ToLowerInvariant();
        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        long? expectedRevision = DatabaseHelpers.GetLongOpt(root, "expected_revision");
        string textOverride = DatabaseHelpers.GetStringOpt(root, "text_override") ?? string.Empty;
        string? dimStyleName = DatabaseHelpers.GetStringOpt(root, "dim_style");
        double? textHeight = DatabaseHelpers.GetFiniteDoubleOpt(root, "text_height");
        double? arrowSize = DatabaseHelpers.GetFiniteDoubleOpt(root, "arrow_size");
        double? dimScale = DatabaseHelpers.GetFiniteDoubleOpt(root, "dim_scale");

        if (!string.IsNullOrWhiteSpace(dimStyleName) && !SecurityPolicy.IsValidSymbolName(dimStyleName))
        {
            throw new AcadGatewayException("bad_argument", $"Invalid dimension style name '{dimStyleName}'.");
        }

        if ((textHeight.HasValue && textHeight.Value <= 0) ||
            (arrowSize.HasValue && arrowSize.Value <= 0) ||
            (dimScale.HasValue && dimScale.Value <= 0))
        {
            throw new AcadGatewayException("bad_argument", "'text_height', 'arrow_size', and 'dim_scale' must be greater than zero when provided.");
        }

        bool hasExplicitStyle = !string.IsNullOrWhiteSpace(dimStyleName);
        if (!SecurityPolicy.TryNormalizeSpace(
                DatabaseHelpers.GetStringOpt(root, "space"),
                null,
                out string canonicalSpace,
                out string? spaceErr))
        {
            throw new AcadGatewayException("bad_argument", spaceErr ?? "'space' must be 'model' or 'paper'.");
        }

        return Acad.Write(ctx =>
        {
            ObjectId dimStyleId = ctx.Db.Dimstyle;
            if (hasExplicitStyle)
            {
                var dst = (DimStyleTable)ctx.Tr.GetObject(ctx.Db.DimStyleTableId, OpenMode.ForRead);
                string trimmedStyle = dimStyleName!.Trim();
                if (!dst.Has(trimmedStyle))
                {
                    throw new AcadGatewayException("not_found", $"Dimension style '{trimmedStyle}' does not exist.");
                }

                dimStyleId = dst[trimmedStyle];
            }

            var styleRecord = (DimStyleTableRecord)ctx.Tr.GetObject(dimStyleId, OpenMode.ForRead);
            BlockTableRecord space = DatabaseHelpers.OpenSpace(ctx.Db, ctx.Tr, canonicalSpace, OpenMode.ForWrite);
            bool calibrated = false;
            Dimension dim = DatabaseHelpers.CreateAndAppend(
                space,
                ctx.Tr,
                ctx.Db,
                () => BuildDimension(root, dimType, textOverride, dimStyleId),
                d =>
                {
                    DatabaseHelpers.ValidateAndApplyEntityProperties(d, ctx.Db, ctx.Tr, root);
                    ApplyDimensionSizing(
                        d,
                        ctx.Db,
                        styleRecord,
                        canonicalSpace,
                        hasExplicitStyle,
                        dimStyleId,
                        textHeight,
                        arrowSize,
                        dimScale,
                        out calibrated);
                });

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                handle = dim.ObjectId.Handle.ToString(),
                type = dimType,
                space = canonicalSpace,
                dim_style = styleRecord.Name,
                text_height = Math.Round(dim.Dimtxt, 4),
                arrow_size = Math.Round(dim.Dimasz, 4),
                dim_scale = Math.Round(dim.Dimscale, 4),
                calibrated,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName, expectedRevision: expectedRevision);
    }

    private static void ApplyDimensionSizing(
        Dimension dim,
        Database db,
        DimStyleTableRecord styleRecord,
        string spaceName,
        bool hasExplicitStyle,
        ObjectId explicitStyleId,
        double? textHeight,
        double? arrowSize,
        double? dimScale,
        out bool calibrated)
    {
        calibrated = false;

        if (hasExplicitStyle)
        {
            dim.DimensionStyle = explicitStyleId;
        }
        else
        {
            dim.SetDimstyleData(db.GetDimstyleData());
        }

        bool isAnnotative = styleRecord.Annotative == AnnotativeStates.True ||
            dim.Annotative == AnnotativeStates.True;
        bool isMetricUnits = db.Insunits is UnitsValue.Meters or UnitsValue.Millimeters;

        // Auto-calibrate fallback metric dimensions only when caller did not supply explicit sizing.
        if (spaceName == "model" &&
            !hasExplicitStyle &&
            !isAnnotative &&
            isMetricUnits &&
            !dimScale.HasValue &&
            !textHeight.HasValue &&
            !arrowSize.HasValue)
        {
            double baseScale = dim.Dimscale > 0 ? dim.Dimscale : 1.0;
            double effectiveText = dim.Dimtxt * baseScale;
            if (effectiveText >= 1.0)
            {
                dim.Dimscale = 1.0;
                dim.Dimtxt = 0.25;
                dim.Dimasz = 0.18;
                dim.Dimexe = 0.12;
                dim.Dimexo = 0.06;
                dim.Dimgap = 0.06;
                dim.Dimdec = 2;
                calibrated = true;
            }
        }

        if (dimScale.HasValue)
        {
            dim.Dimscale = dimScale.Value;
        }

        if (textHeight.HasValue)
        {
            dim.Dimtxt = textHeight.Value;
            if (!arrowSize.HasValue)
            {
                dim.Dimasz = Math.Round(textHeight.Value * 0.72, 4);
                dim.Dimexe = Math.Round(textHeight.Value * 0.48, 4);
                dim.Dimexo = Math.Round(textHeight.Value * 0.24, 4);
                dim.Dimgap = Math.Round(textHeight.Value * 0.24, 4);
            }
        }

        if (arrowSize.HasValue)
        {
            dim.Dimasz = arrowSize.Value;
        }
    }

    private static Dimension BuildDimension(JsonElement root, string dimType, string textOverride, ObjectId dimStyleId)
    {
        switch (dimType)
        {
            case "aligned":
            {
                Point3d p1 = DatabaseHelpers.ReadRequiredPoint3d(root, "p1", "x1", "y1", "z1");
                Point3d p2 = DatabaseHelpers.ReadRequiredPoint3d(root, "p2", "x2", "y2", "z2");
                Point3d dimLine = DatabaseHelpers.ReadPoint3dOpt(root, "dim_line_point")
                    ?? ComputeMidpoint(p1, p2, offsetY: 1.0);
                return new AlignedDimension(p1, p2, dimLine, textOverride, dimStyleId);
            }

            case "rotated":
            {
                Point3d p1 = DatabaseHelpers.ReadRequiredPoint3d(root, "p1", "x1", "y1", "z1");
                Point3d p2 = DatabaseHelpers.ReadRequiredPoint3d(root, "p2", "x2", "y2", "z2");
                double angleDeg = DatabaseHelpers.GetFiniteDoubleOpt(root, "angle") ?? 0.0;
                Point3d dimLine = DatabaseHelpers.ReadPoint3dOpt(root, "dim_line_point")
                    ?? ComputeMidpoint(p1, p2, offsetY: 1.0);
                return new RotatedDimension(DatabaseHelpers.ToRadians(angleDeg), p1, p2, dimLine, textOverride, dimStyleId);
            }

            case "radial":
            {
                Point3d center = DatabaseHelpers.ReadRequiredPoint3d(root, "center", "center_x", "center_y", "center_z");
                Point3d chord = DatabaseHelpers.ReadRequiredPoint3d(root, "chord", "x2", "y2", "z2");
                double leaderLen = DatabaseHelpers.GetFiniteDoubleOpt(root, "leader_length") ?? 0.5;
                if (leaderLen < 0) throw new AcadGatewayException("bad_argument", "'leader_length' cannot be negative.");
                return new RadialDimension(center, chord, leaderLen, textOverride, dimStyleId);
            }

            case "diametric":
            {
                Point3d chord = DatabaseHelpers.ReadRequiredPoint3d(root, "p1", "x1", "y1", "z1");
                Point3d farChord = DatabaseHelpers.ReadRequiredPoint3d(root, "p2", "x2", "y2", "z2");
                double leaderLen = DatabaseHelpers.GetFiniteDoubleOpt(root, "leader_length") ?? 0.5;
                if (leaderLen < 0) throw new AcadGatewayException("bad_argument", "'leader_length' cannot be negative.");
                return new DiametricDimension(chord, farChord, leaderLen, textOverride, dimStyleId);
            }

            case "angular":
            {
                Point3d vertex = DatabaseHelpers.ReadRequiredPoint3d(root, "vertex", "cx", "cy", "cz");
                Point3d p1 = DatabaseHelpers.ReadRequiredPoint3d(root, "p1", "x1", "y1", "z1");
                Point3d p2 = DatabaseHelpers.ReadRequiredPoint3d(root, "p2", "x2", "y2", "z2");
                Point3d rawArc = DatabaseHelpers.ReadPoint3dOpt(root, "arc_point")
                    ?? DatabaseHelpers.ReadPoint3dOpt(root, "dim_line_point")
                    ?? ComputeMidpoint(p1, p2, offsetY: 0.0);
                Point3d arcPt = new(rawArc.X, rawArc.Y, vertex.Z);
                return new Point3AngularDimension(vertex, p1, p2, arcPt, textOverride, dimStyleId);
            }

            default:
                throw new AcadGatewayException("bad_argument", $"Unsupported dimension type '{dimType}'. Use aligned, rotated, radial, diametric, or angular.");
        }
    }

    /// <summary>Creates a native multileader annotation pointing from <c>arrow_point</c> to <c>landing_point</c>.</summary>
    /// <param name="body">JSON payload containing arrow/landing coordinates, text, text height, arrow size, and entity metadata.</param>
    /// <returns>Serialized JSON confirmation with entity handle and revision.</returns>
    /// <exception cref="AcadGatewayException">Thrown when coordinates, text, or sizes are invalid.</exception>
    public static Task<string> CreateMLeaderAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        Point3d arrowPt = DatabaseHelpers.ReadRequiredPoint3d(root, "arrow_point", "arrow_x", "arrow_y", "arrow_z");
        Point3d landingPt = DatabaseHelpers.ReadRequiredPoint3d(root, "landing_point", "landing_x", "landing_y", "landing_z");
        string text = DatabaseHelpers.GetRequiredString(root, "text");
        double textHeight = DatabaseHelpers.GetFiniteDoubleOpt(root, "text_height") ?? 0.25;
        double? arrowSize = DatabaseHelpers.GetFiniteDoubleOpt(root, "arrow_size");

        if (textHeight <= 0 || (arrowSize.HasValue && arrowSize.Value <= 0))
        {
            throw new AcadGatewayException("bad_argument", "'text_height' and optional 'arrow_size' must be greater than zero.");
        }

        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        long? expectedRevision = DatabaseHelpers.GetLongOpt(root, "expected_revision");

        return Acad.Write(ctx =>
        {
            BlockTableRecord space = DatabaseHelpers.OpenSpace(ctx.Db, ctx.Tr, DatabaseHelpers.GetStringOpt(root, "space"), OpenMode.ForWrite);
            var ml = new MLeader();
            using var mtext = new MText();
            try
            {
                ml.SetDatabaseDefaults(ctx.Db);
                ml.MLeaderStyle = ctx.Db.MLeaderstyle;
                mtext.SetDatabaseDefaults(ctx.Db);
                mtext.Contents = text;
                mtext.TextHeight = textHeight;
                mtext.Location = landingPt;

                ml.ContentType = ContentType.MTextContent;
                ml.MText = mtext;
                ml.TextLocation = landingPt;
                if (arrowSize.HasValue)
                {
                    ml.ArrowSize = arrowSize.Value;
                }

                int leaderIdx = ml.AddLeader();
                int lineIdx = ml.AddLeaderLine(leaderIdx);
                ml.AddFirstVertex(lineIdx, arrowPt);
                ml.AddLastVertex(lineIdx, landingPt);

                DatabaseHelpers.ValidateAndApplyEntityProperties(ml, ctx.Db, ctx.Tr, root);
            }
            catch
            {
                ml.Dispose();
                throw;
            }

            ObjectId id = DatabaseHelpers.AppendEntity(space, ctx.Tr, ml);
            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                handle = id.Handle.ToString(),
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName, expectedRevision: expectedRevision);
    }

    /// <summary>Creates a native solid or patterned <see cref="Hatch"/> from closed boundary handles or 2D polygon vertices.</summary>
    /// <param name="body">JSON payload containing exclusive boundary <c>handles</c> or <c>points</c>, pattern, scale, angle, and entity metadata.</param>
    /// <returns>Serialized JSON confirmation with entity handle, pattern, and revision.</returns>
    /// <exception cref="AcadGatewayException">Thrown when both or neither boundary sources are supplied, pattern/scale is invalid, or handles fail validation.</exception>
    public static Task<string> CreateHatchAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        string pattern = (DatabaseHelpers.GetStringOpt(root, "pattern") ?? "ANSI31").Trim();
        if (!SecurityPolicy.IsValidSymbolName(pattern))
        {
            throw new AcadGatewayException("bad_argument", $"Invalid hatch pattern name '{pattern}'.");
        }

        double scale = DatabaseHelpers.GetFiniteDoubleOpt(root, "scale") ?? 1.0;
        double angleDeg = DatabaseHelpers.GetFiniteDoubleOpt(root, "angle") ?? 0.0;
        if (scale <= 0)
        {
            throw new AcadGatewayException("bad_argument", "Hatch scale must be greater than zero.");
        }

        bool hasHandles = root.TryGetProperty("handles", out JsonElement handlesEl) && handlesEl.ValueKind != JsonValueKind.Null;
        if (hasHandles)
        {
            if (handlesEl.ValueKind != JsonValueKind.Array)
            {
                throw new AcadGatewayException("bad_argument", "'handles' must be an array of hexadecimal entity handles.");
            }

            hasHandles = handlesEl.GetArrayLength() > 0;
        }

        bool hasPoints = root.TryGetProperty("points", out JsonElement ptsEl) && ptsEl.ValueKind != JsonValueKind.Null;
        if (hasHandles && hasPoints)
        {
            throw new AcadGatewayException("bad_argument", "Provide either boundary 'handles' or closed polygon 'points', not both.");
        }

        if (!hasHandles && !hasPoints)
        {
            throw new AcadGatewayException("bad_argument", "Supply either boundary 'handles' or closed polygon 'points'.");
        }

        bool associative = DatabaseHelpers.GetBoolOpt(root, "associative") ?? hasHandles;
        if (!hasHandles && associative)
        {
            associative = false;
        }

        List<Point2d>? polyPts = hasPoints ? ReadPoint2dLoop(ptsEl) : null;
        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        long? expectedRevision = DatabaseHelpers.GetLongOpt(root, "expected_revision");

        return Acad.Write(ctx =>
        {
            var loopIds = hasHandles ? DatabaseHelpers.ResolveHandles(ctx.Db, root) : null;
            BlockTableRecord space = DatabaseHelpers.OpenSpace(ctx.Db, ctx.Tr, DatabaseHelpers.GetStringOpt(root, "space"), OpenMode.ForWrite);

            Hatch hatch = DatabaseHelpers.CreateAndAppend(
                space,
                ctx.Tr,
                ctx.Db,
                static () => new Hatch(),
                h =>
                {
                    DatabaseHelpers.ValidateAndApplyEntityProperties(h, ctx.Db, ctx.Tr, root);
                    h.HatchObjectType = HatchObjectType.HatchObject;
                    h.SetHatchPattern(HatchPatternType.PreDefined, pattern);
                    if (!string.Equals(pattern, "SOLID", StringComparison.OrdinalIgnoreCase))
                    {
                        h.PatternScale = scale;
                        h.PatternAngle = DatabaseHelpers.ToRadians(angleDeg);
                    }
                });

            ObjectId hatchId = hatch.ObjectId;
            hatch.Associative = associative;

            if (loopIds is not null)
            {
                using var objIds = new ObjectIdCollection();
                foreach (ObjectId lid in loopIds)
                {
                    objIds.Add(lid);
                }

                hatch.AppendLoop(HatchLoopTypes.Default, objIds);
            }
            else if (polyPts is not null)
            {
                var p2dCol = new Point2dCollection(polyPts.Count);
                var bulges = new DoubleCollection(polyPts.Count);
                foreach (Point2d pt in polyPts)
                {
                    p2dCol.Add(pt);
                    bulges.Add(0.0);
                }

                hatch.AppendLoop(HatchLoopTypes.External, p2dCol, bulges);
            }

            hatch.EvaluateHatch(true);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                handle = hatchId.Handle.ToString(),
                pattern,
                associative,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName, expectedRevision: expectedRevision);
    }

    /// <summary>Creates a structured AutoCAD <see cref="Table"/> entity inside one <c>Acad.Write</c> transaction.</summary>
    /// <param name="body">JSON payload containing origin position, grid size, cell matrix, optional headers/title, dimensions, and layer.</param>
    /// <returns>Serialized JSON confirmation with entity handle, total row/column counts, and revision.</returns>
    /// <exception cref="AcadGatewayException">Thrown when dimensions, row/column caps, or cell payload shapes are invalid.</exception>
    public static Task<string> CreateTableAsync(string body)
    {
        JsonElement root = DatabaseHelpers.ParseJsonObject(body);
        Point3d position = DatabaseHelpers.ReadRequiredPoint3d(root, "position", "x", "y", "z");
        int rows = DatabaseHelpers.GetIntOpt(root, "rows") ?? 0;
        int cols = DatabaseHelpers.GetIntOpt(root, "cols") ?? 0;
        if (rows < 1 || rows > SecurityPolicy.MaxTableRows || cols < 1 || cols > SecurityPolicy.MaxTableCols)
        {
            throw new AcadGatewayException(
                "bad_argument",
                $"'rows' (1..{SecurityPolicy.MaxTableRows}) and 'cols' (1..{SecurityPolicy.MaxTableCols}) are required.");
        }

        double rowHeight = DatabaseHelpers.GetFiniteDoubleOpt(root, "row_height") ?? 1.0;
        double colWidth = DatabaseHelpers.GetFiniteDoubleOpt(root, "col_width") ?? 5.0;
        double textHeight = DatabaseHelpers.GetFiniteDoubleOpt(root, "text_height") ?? 0.35;
        double titleTextHeight = textHeight * 1.25;
        if (rowHeight <= 0 || colWidth <= 0 || textHeight <= 0 || !double.IsFinite(titleTextHeight))
        {
            throw new AcadGatewayException("bad_argument", "'row_height', 'col_width', and 'text_height' must yield finite positive sizes.");
        }

        string? title = DatabaseHelpers.GetStringOpt(root, "title");
        bool hasTitle = !string.IsNullOrWhiteSpace(title);
        List<string>? headers = ReadStringArrayOpt(root, "headers", cols);
        List<List<string>>? cells = ReadCellMatrixOpt(root, "cells", rows, cols);

        int extraRows = (hasTitle ? 1 : 0) + (headers is not null ? 1 : 0);
        int totalRows = rows + extraRows;
        if (totalRows > SecurityPolicy.MaxTableRows)
        {
            throw new AcadGatewayException("bad_argument", $"Total rows including title/headers cannot exceed {SecurityPolicy.MaxTableRows}.");
        }

        string? documentName = DatabaseHelpers.GetStringOpt(root, "document");
        long? expectedRevision = DatabaseHelpers.GetLongOpt(root, "expected_revision");

        return Acad.Write(ctx =>
        {
            BlockTableRecord space = DatabaseHelpers.OpenSpace(ctx.Db, ctx.Tr, DatabaseHelpers.GetStringOpt(root, "space"), OpenMode.ForWrite);
            var table = new Table();
            try
            {
                table.SetDatabaseDefaults(ctx.Db);
                table.TableStyle = ctx.Db.Tablestyle;
                table.SetSize(totalRows, cols);
                if (!hasTitle && cols > 1 && table.Cells[0, 0].IsMerged == true)
                {
                    table.UnmergeCells(table.Cells[0, 0].GetMergeRange());
                }

                int styleRow = 0;
                if (hasTitle)
                {
                    table.Rows[styleRow++].Style = "Title";
                }

                if (headers is not null)
                {
                    table.Rows[styleRow++].Style = "Header";
                }

                while (styleRow < totalRows)
                {
                    table.Rows[styleRow++].Style = "Data";
                }

                table.Position = position;
                table.SetRowHeight(rowHeight);
                table.SetColumnWidth(colWidth);

                int rowIdx = 0;
                if (hasTitle)
                {
                    table.Cells[rowIdx, 0].TextString = title!.Trim();
                    table.Cells[rowIdx, 0].TextHeight = titleTextHeight;
                    rowIdx++;
                }

                if (headers is not null)
                {
                    for (int c = 0; c < Math.Min(cols, headers.Count); c++)
                    {
                        table.Cells[rowIdx, c].TextString = headers[c];
                        table.Cells[rowIdx, c].TextHeight = textHeight;
                    }

                    rowIdx++;
                }

                if (cells is not null)
                {
                    for (int r = 0; r < Math.Min(rows, cells.Count); r++)
                    {
                        List<string> rowCells = cells[r];
                        for (int c = 0; c < Math.Min(cols, rowCells.Count); c++)
                        {
                            table.Cells[rowIdx + r, c].TextString = rowCells[c];
                            table.Cells[rowIdx + r, c].TextHeight = textHeight;
                        }
                    }
                }

                DatabaseHelpers.ValidateAndApplyEntityProperties(table, ctx.Db, ctx.Tr, root);
                table.GenerateLayout();
            }
            catch
            {
                table.Dispose();
                throw;
            }

            ObjectId id = DatabaseHelpers.AppendEntity(space, ctx.Tr, table);
            return JsonSerializer.Serialize(new
            {
                status = "success",
                ok = true,
                handle = id.Handle.ToString(),
                rows = totalRows,
                cols,
                document = ctx.Doc.Name,
                revision = ctx.Session.Revision,
            }, GatewayProtocol.WireJsonOptions);
        }, documentName: documentName, expectedRevision: expectedRevision);
    }

    private static Point3d ComputeMidpoint(Point3d p1, Point3d p2, double offsetY)
    {
        double mx = p1.X * 0.5 + p2.X * 0.5;
        double my = p1.Y * 0.5 + p2.Y * 0.5 + offsetY;
        double mz = p1.Z * 0.5 + p2.Z * 0.5;
        if (!double.IsFinite(mx) || !double.IsFinite(my) || !double.IsFinite(mz))
        {
            throw new AcadGatewayException("bad_argument", "Computed dimension midpoint coordinates overflowed.");
        }

        return new Point3d(mx, my, mz);
    }

    private static List<Point2d> ReadPoint2dLoop(JsonElement ptsEl)
    {
        if (ptsEl.ValueKind != JsonValueKind.Array)
        {
            throw new AcadGatewayException("bad_argument", "'points' boundary polygon must be an array of [x, y] vertices.");
        }

        int len = ptsEl.GetArrayLength();
        if (len < 3 || len > SecurityPolicy.MaxGeometryVertices)
        {
            throw new AcadGatewayException(
                "bad_argument",
                $"'points' boundary polygon requires between 3 and {SecurityPolicy.MaxGeometryVertices} [x, y] vertices.");
        }

        var result = new List<Point2d>(len);
        foreach (JsonElement ptEl in ptsEl.EnumerateArray())
        {
            Point3d p = DatabaseHelpers.ReadPoint3d(ptEl, "points");
            result.Add(new Point2d(p.X, p.Y));
        }

        if (result.Count >= 4 && result[^1].IsEqualTo(result[0]))
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    private static List<string>? ReadStringArrayOpt(JsonElement root, string name, int maxItems)
    {
        if (!root.TryGetProperty(name, out JsonElement el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (el.ValueKind != JsonValueKind.Array)
        {
            throw new AcadGatewayException("bad_argument", $"'{name}' must be an array of strings.");
        }

        int len = el.GetArrayLength();
        if (len > maxItems)
        {
            throw new AcadGatewayException("bad_argument", $"'{name}' cannot exceed {maxItems} items.");
        }

        var list = new List<string>(len);
        foreach (JsonElement item in el.EnumerateArray())
        {
            list.Add(item.ValueKind == JsonValueKind.String ? (item.GetString() ?? string.Empty) : item.ToString());
        }

        return list;
    }

    private static List<List<string>>? ReadCellMatrixOpt(JsonElement root, string name, int maxRows, int maxCols)
    {
        if (!root.TryGetProperty(name, out JsonElement el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (el.ValueKind != JsonValueKind.Array)
        {
            throw new AcadGatewayException("bad_argument", $"'{name}' must be a 2D array of rows.");
        }

        int rowLen = el.GetArrayLength();
        if (rowLen > maxRows)
        {
            throw new AcadGatewayException("bad_argument", $"'{name}' cannot exceed {maxRows} rows.");
        }

        var rows = new List<List<string>>(rowLen);
        foreach (JsonElement rowEl in el.EnumerateArray())
        {
            if (rowEl.ValueKind != JsonValueKind.Array)
            {
                throw new AcadGatewayException("bad_argument", "Each row in 'cells' must be a JSON array.");
            }

            int colLen = rowEl.GetArrayLength();
            if (colLen > maxCols)
            {
                throw new AcadGatewayException("bad_argument", $"Each row in '{name}' cannot exceed {maxCols} columns.");
            }

            var row = new List<string>(colLen);
            foreach (JsonElement cellEl in rowEl.EnumerateArray())
            {
                row.Add(cellEl.ValueKind == JsonValueKind.String ? (cellEl.GetString() ?? string.Empty) : cellEl.ToString());
            }

            rows.Add(row);
        }

        return rows;
    }
}
