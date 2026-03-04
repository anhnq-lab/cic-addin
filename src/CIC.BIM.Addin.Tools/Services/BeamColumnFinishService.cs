using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Tạo lớp hoàn thiện (tường bọc) cho Dầm và Cột kết cấu.
/// Lấy bounding box / face geometry → tạo Wall bao quanh.
/// </summary>
public class BeamColumnFinishService
{
    public class BeamColFinishOptions
    {
        public ElementId WallTypeId { get; set; } = ElementId.InvalidElementId;
        public bool JoinWithOriginal { get; set; } = true;
        public bool IncludeBeamBottom { get; set; } = true;
        public double OffsetMm { get; set; } = 0;
    }

    public class BeamColFinishResult
    {
        public int ElementsProcessed { get; set; }
        public int WallsCreated { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<ElementId> CreatedWallIds { get; set; } = new();
    }

    private const double MmToFeet = 1.0 / 304.8;

    public BeamColFinishResult Execute(Document doc, IList<Element> elements, BeamColFinishOptions options)
    {
        var result = new BeamColFinishResult();

        using var tg = new TransactionGroup(doc, "Tạo hoàn thiện Dầm/Cột");
        tg.Start();

        foreach (var elem in elements)
        {
            try
            {
                ProcessElement(doc, elem, options, result);
                result.ElementsProcessed++;
            }
            catch (System.Exception ex)
            {
                result.Warnings.Add($"'{elem.Name}' (ID:{elem.Id.IntegerValue}): {ex.Message}");
            }
        }

        tg.Assimilate();
        return result;
    }

    private void ProcessElement(Document doc, Element elem, BeamColFinishOptions options, BeamColFinishResult result)
    {
        // Lấy bounding box
        var bbox = elem.get_BoundingBox(null);
        if (bbox == null)
        {
            result.Warnings.Add($"'{elem.Name}': không có bounding box.");
            return;
        }

        // Xác định level
        var levelId = elem.LevelId;
        if (levelId == ElementId.InvalidElementId)
        {
            // Thử lấy từ parameter
            var lvlParam = elem.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
            if (lvlParam != null)
                levelId = lvlParam.AsElementId();
        }

        if (levelId == ElementId.InvalidElementId)
        {
            // Fallback: tìm level gần nhất theo elevation
            levelId = FindNearestLevel(doc, bbox.Min.Z);
        }

        if (levelId == ElementId.InvalidElementId)
        {
            result.Warnings.Add($"'{elem.Name}': không xác định được Level.");
            return;
        }

        using var tx = new Transaction(doc, $"HT - {elem.Name}");
        tx.Start();

        try
        {
            double offsetFeet = options.OffsetMm * MmToFeet;
            bool isColumn = elem.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns
                         || elem.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Columns;

            if (isColumn)
            {
                CreateColumnFinish(doc, elem, bbox, levelId, offsetFeet, options, result);
            }
            else
            {
                CreateBeamFinish(doc, elem, bbox, levelId, offsetFeet, options, result);
            }

            // Join geometry
            if (options.JoinWithOriginal)
            {
                foreach (var wallId in result.CreatedWallIds.ToList())
                {
                    var wall = doc.GetElement(wallId);
                    if (wall == null) continue;
                    try
                    {
                        if (!JoinGeometryUtils.AreElementsJoined(doc, wall, elem))
                            JoinGeometryUtils.JoinGeometry(doc, wall, elem);
                    }
                    catch { }
                }
            }

            tx.Commit();
        }
        catch (System.Exception ex)
        {
            result.Warnings.Add($"'{elem.Name}': {ex.Message}");
            if (tx.HasStarted())
                tx.RollBack();
        }
    }

    /// <summary>
    /// Tạo tường bọc cho Cột — 4 mặt bên theo bounding box.
    /// </summary>
    private void CreateColumnFinish(Document doc, Element elem, BoundingBoxXYZ bbox, ElementId levelId,
        double offsetFeet, BeamColFinishOptions options, BeamColFinishResult result)
    {
        var min = bbox.Min;
        var max = bbox.Max;
        double height = max.Z - min.Z;
        if (height <= 0) return;

        // 4 cạnh bounding box (mặt bằng)
        var corners = new[]
        {
            new XYZ(min.X - offsetFeet, min.Y - offsetFeet, min.Z),
            new XYZ(max.X + offsetFeet, min.Y - offsetFeet, min.Z),
            new XYZ(max.X + offsetFeet, max.Y + offsetFeet, min.Z),
            new XYZ(min.X - offsetFeet, max.Y + offsetFeet, min.Z)
        };

        for (int i = 0; i < 4; i++)
        {
            var startPt = corners[i];
            var endPt = corners[(i + 1) % 4];
            var line = Line.CreateBound(startPt, endPt);

            if (line.ApproximateLength < 10 * MmToFeet) continue;

            try
            {
                var level = doc.GetElement(levelId) as Level;
                double baseOffset = min.Z - (level?.Elevation ?? 0);

                var wall = Wall.Create(doc, line, options.WallTypeId, levelId,
                    height, baseOffset, false, false);

                if (wall != null)
                {
                    var structParam = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
                    if (structParam != null && !structParam.IsReadOnly)
                        structParam.Set(0);

                    result.CreatedWallIds.Add(wall.Id);
                    result.WallsCreated++;
                }
            }
            catch (System.Exception ex)
            {
                result.Warnings.Add($"Cột wall: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Tạo tường bọc cho Dầm — 2 mặt bên + mặt đáy (tùy chọn).
    /// </summary>
    private void CreateBeamFinish(Document doc, Element elem, BoundingBoxXYZ bbox, ElementId levelId,
        double offsetFeet, BeamColFinishOptions options, BeamColFinishResult result)
    {
        var min = bbox.Min;
        var max = bbox.Max;

        // Xác định phương dầm (dọc theo trục dài hơn)
        double dx = max.X - min.X;
        double dy = max.Y - min.Y;
        double height = max.Z - min.Z;
        if (height <= 0) return;

        var level = doc.GetElement(levelId) as Level;
        double baseOffset = min.Z - (level?.Elevation ?? 0);

        // 2 mặt bên (song song với trục dầm)
        List<Line> sideLines;
        if (dx >= dy)
        {
            // Dầm chạy theo X
            sideLines = new List<Line>
            {
                Line.CreateBound(
                    new XYZ(min.X, min.Y - offsetFeet, min.Z),
                    new XYZ(max.X, min.Y - offsetFeet, min.Z)),
                Line.CreateBound(
                    new XYZ(min.X, max.Y + offsetFeet, min.Z),
                    new XYZ(max.X, max.Y + offsetFeet, min.Z))
            };
        }
        else
        {
            // Dầm chạy theo Y
            sideLines = new List<Line>
            {
                Line.CreateBound(
                    new XYZ(min.X - offsetFeet, min.Y, min.Z),
                    new XYZ(min.X - offsetFeet, max.Y, min.Z)),
                Line.CreateBound(
                    new XYZ(max.X + offsetFeet, min.Y, min.Z),
                    new XYZ(max.X + offsetFeet, max.Y, min.Z))
            };
        }

        foreach (var line in sideLines)
        {
            if (line.ApproximateLength < 10 * MmToFeet) continue;
            try
            {
                var wall = Wall.Create(doc, line, options.WallTypeId, levelId,
                    height, baseOffset, false, false);

                if (wall != null)
                {
                    var structParam = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
                    if (structParam != null && !structParam.IsReadOnly)
                        structParam.Set(0);

                    result.CreatedWallIds.Add(wall.Id);
                    result.WallsCreated++;
                }
            }
            catch (System.Exception ex)
            {
                result.Warnings.Add($"Dầm side wall: {ex.Message}");
            }
        }

        // Mặt đáy (nếu chọn)
        if (options.IncludeBeamBottom)
        {
            Line? bottomLine;
            if (dx >= dy)
            {
                bottomLine = Line.CreateBound(
                    new XYZ(min.X, (min.Y + max.Y) / 2, min.Z),
                    new XYZ(max.X, (min.Y + max.Y) / 2, min.Z));
            }
            else
            {
                bottomLine = Line.CreateBound(
                    new XYZ((min.X + max.X) / 2, min.Y, min.Z),
                    new XYZ((min.X + max.X) / 2, max.Y, min.Z));
            }

            if (bottomLine != null && bottomLine.ApproximateLength >= 10 * MmToFeet)
            {
                try
                {
                    double bottomWallWidth = dy >= dx ? (max.X - min.X) : (max.Y - min.Y);
                    double bottomHeight = bottomWallWidth + 2 * offsetFeet;
                    if (bottomHeight <= 0) bottomHeight = 100 * MmToFeet;

                    var wall = Wall.Create(doc, bottomLine, options.WallTypeId, levelId,
                        bottomHeight, baseOffset, false, false);

                    if (wall != null)
                    {
                        var structParam = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
                        if (structParam != null && !structParam.IsReadOnly)
                            structParam.Set(0);

                        result.CreatedWallIds.Add(wall.Id);
                        result.WallsCreated++;
                    }
                }
                catch (System.Exception ex)
                {
                    result.Warnings.Add($"Dầm bottom wall: {ex.Message}");
                }
            }
        }
    }

    private ElementId FindNearestLevel(Document doc, double elevation)
    {
        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => System.Math.Abs(l.Elevation - elevation))
            .FirstOrDefault();

        return levels?.Id ?? ElementId.InvalidElementId;
    }
}
