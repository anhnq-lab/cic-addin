using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Tự động tạo Room Separation Lines bằng cách:
/// 1. Đọc tường từ host model + linked models
/// 2. Phát hiện gaps (đầu mút tường không nối với tường khác)
/// 3. Vẽ Room Separation Lines để đóng kín phòng
/// </summary>
public class AutoSepLineService
{
    public class SepLineResult
    {
        public int LinesCreated { get; set; }
        public int GapsFound { get; set; }
        public int WallsFromHost { get; set; }
        public int WallsFromLinks { get; set; }
        public List<string> Messages { get; set; } = new();
    }

    // Tolerance: 50mm — khoảng cách tối đa giữa 2 đầu mút để coi là nối nhau
    private const double ConnectTolerance = 50.0 / 304.8; // feet
    // Max gap distance: 2000mm — khoảng cách tối đa để vẽ sep line đóng gap
    private const double MaxGapDistance = 2000.0 / 304.8; // feet

    public SepLineResult Execute(Document doc, ElementId levelId)
    {
        var result = new SepLineResult();
        var level = doc.GetElement(levelId) as Level;
        if (level == null) { result.Messages.Add("❌ Level not found."); return result; }

        result.Messages.Add($"🔧 Auto Sep Lines: {level.Name}");

        // ═══ BƯỚC 1: Thu thập tất cả tường (host + links) ═══
        var allCurves = new List<WallSegment>();

        // Host walls
        var hostCurves = CollectHostWalls(doc, level);
        allCurves.AddRange(hostCurves);
        result.WallsFromHost = hostCurves.Count;
        result.Messages.Add($"  ℹ️ Host: {hostCurves.Count} walls");

        // Link walls
        var linkCurves = CollectLinkedWalls(doc, level);
        allCurves.AddRange(linkCurves);
        result.WallsFromLinks = linkCurves.Count;
        result.Messages.Add($"  ℹ️ Links: {linkCurves.Count} walls");

        // Existing sep lines
        var existingSepLines = CollectExistingSepLines(doc, level);
        allCurves.AddRange(existingSepLines);
        result.Messages.Add($"  ℹ️ Existing sep lines: {existingSepLines.Count}");

        if (allCurves.Count == 0)
        {
            result.Messages.Add("  ⚠️ No walls found.");
            return result;
        }

        // ═══ BƯỚC 2: Tìm dangling endpoints (gaps) ═══
        var gaps = FindGaps(allCurves);
        result.GapsFound = gaps.Count;
        result.Messages.Add($"  ℹ️ Found {gaps.Count} dangling endpoints (gaps).");

        if (gaps.Count == 0)
        {
            result.Messages.Add("  ✅ No gaps found — all boundaries are closed.");
            return result;
        }

        // ═══ BƯỚC 3: Vẽ sep lines để đóng gaps ═══
        CreateSepLinesToCloseGaps(doc, level, gaps, allCurves, result);

        result.Messages.Add($"✅ Done: Created {result.LinesCreated} sep lines to close {result.GapsFound} gaps.");
        return result;
    }

    /// <summary>
    /// Thu thập walls từ host model trên level chỉ định.
    /// </summary>
    private List<WallSegment> CollectHostWalls(Document doc, Level level)
    {
        var segments = new List<WallSegment>();
        var walls = new FilteredElementCollector(doc)
            .OfClass(typeof(Wall)).WhereElementIsNotElementType()
            .Cast<Wall>().ToList();

        foreach (var wall in walls)
        {
            try
            {
                if (!wall.IsValidObject) continue;
                if (wall.WallType.Kind == WallKind.Curtain) continue;

                // Check if wall is on or spans this level
                if (!IsWallOnLevel(doc, wall, level)) continue;

                // Check Room Bounding
                var rbParam = wall.LookupParameter("Room Bounding");
                if (rbParam != null && rbParam.AsInteger() == 0) continue;

                var loc = wall.Location as LocationCurve;
                if (loc?.Curve == null) continue;

                AddCurveSegment(segments, loc.Curve, level.Elevation);
            }
            catch { }
        }

        return segments;
    }

    /// <summary>
    /// Thu thập walls từ tất cả linked models.
    /// Transform coordinates về host model coordinates.
    /// </summary>
    private List<WallSegment> CollectLinkedWalls(Document doc, Level level)
    {
        var segments = new List<WallSegment>();

        var linkInstances = new FilteredElementCollector(doc)
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>()
            .ToList();

        foreach (var linkInstance in linkInstances)
        {
            try
            {
                // Check Room Bounding on the link instance
                var rbParam = linkInstance.LookupParameter("Room Bounding");
                if (rbParam != null && rbParam.AsInteger() == 0) continue;

                var linkDoc = linkInstance.GetLinkDocument();
                if (linkDoc == null) continue;

                var transform = linkInstance.GetTotalTransform();

                // Get walls from linked document
                var linkWalls = new FilteredElementCollector(linkDoc)
                    .OfClass(typeof(Wall)).WhereElementIsNotElementType()
                    .Cast<Wall>().ToList();

                foreach (var wall in linkWalls)
                {
                    try
                    {
                        if (!wall.IsValidObject) continue;
                        if (wall.WallType.Kind == WallKind.Curtain) continue;

                        // Check Room Bounding
                        var wallRb = wall.LookupParameter("Room Bounding");
                        if (wallRb != null && wallRb.AsInteger() == 0) continue;

                        // Check level correspondence
                        if (!IsWallOnLevel(linkDoc, wall, level, transform)) continue;

                        var loc = wall.Location as LocationCurve;
                        if (loc?.Curve == null) continue;

                        // Transform curve from link coordinates to host coordinates
                        var transformedCurve = loc.Curve.CreateTransformed(transform);
                        AddCurveSegment(segments, transformedCurve, level.Elevation);
                    }
                    catch { }
                }
            }
            catch { }
        }

        return segments;
    }

    /// <summary>
    /// Thu thập Room Separation Lines hiện có.
    /// </summary>
    private List<WallSegment> CollectExistingSepLines(Document doc, Level level)
    {
        var segments = new List<WallSegment>();
        var sepLines = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_RoomSeparationLines)
            .WhereElementIsNotElementType().ToElements();

        foreach (var el in sepLines)
        {
            try
            {
                if (!(el is ModelCurve)) continue;
                if (el.LevelId != ElementId.InvalidElementId)
                {
                    var eLvl = doc.GetElement(el.LevelId) as Level;
                    if (eLvl?.Id != level.Id) continue;
                }
                else continue;

                var loc = el.Location as LocationCurve;
                if (loc?.Curve == null) continue;
                AddCurveSegment(segments, loc.Curve, level.Elevation);
            }
            catch { }
        }

        return segments;
    }

    /// <summary>
    /// Tìm dangling endpoints — đầu mút tường chỉ kết nối với 1 tường duy nhất.
    /// Đây là các vị trí có "gap" cần đóng bằng sep line.
    /// </summary>
    private List<XYZ> FindGaps(List<WallSegment> allCurves)
    {
        // Collect all endpoints
        var endpoints = new List<EndpointInfo>();
        for (int i = 0; i < allCurves.Count; i++)
        {
            endpoints.Add(new EndpointInfo { Point = allCurves[i].Start2D, SegmentIndex = i, IsStart = true });
            endpoints.Add(new EndpointInfo { Point = allCurves[i].End2D, SegmentIndex = i, IsStart = false });
        }

        // For each endpoint, count how many OTHER segments connect to it
        var danglingPoints = new List<XYZ>();

        foreach (var ep in endpoints)
        {
            int connectionCount = 0;
            foreach (var other in endpoints)
            {
                if (other.SegmentIndex == ep.SegmentIndex) continue;
                double dist = Distance2D(ep.Point, other.Point);
                if (dist < ConnectTolerance)
                {
                    connectionCount++;
                    break; // At least one connection found
                }
            }

            if (connectionCount == 0)
            {
                // This endpoint is dangling — not connected to any other segment
                // Check it's not a duplicate
                bool isDuplicate = danglingPoints.Any(p => Distance2D(p, ep.Point) < ConnectTolerance);
                if (!isDuplicate)
                    danglingPoints.Add(ep.Point);
            }
        }

        return danglingPoints;
    }

    /// <summary>
    /// Vẽ Room Separation Lines để đóng gaps.
    /// Nối các cặp dangling endpoints gần nhau.
    /// </summary>
    private void CreateSepLinesToCloseGaps(Document doc, Level level,
        List<XYZ> gaps, List<WallSegment> allCurves, SepLineResult result)
    {
        using var tg = new TransactionGroup(doc, "Auto Sep Lines");
        tg.Start();

        using var tx = new Transaction(doc, "Create Sep Lines");
        tx.Start();

        try
        {
            var plane = Autodesk.Revit.DB.Plane.CreateByNormalAndOrigin(
                XYZ.BasisZ, new XYZ(0, 0, level.Elevation));
            var sketchPlane = SketchPlane.Create(doc, plane);

            // Nối các cặp dangling endpoints gần nhau
            var used = new HashSet<int>();
            for (int i = 0; i < gaps.Count; i++)
            {
                if (used.Contains(i)) continue;

                // Tìm endpoint gần nhất chưa dùng
                double bestDist = double.MaxValue;
                int bestIdx = -1;

                for (int j = i + 1; j < gaps.Count; j++)
                {
                    if (used.Contains(j)) continue;
                    double dist = Distance2D(gaps[i], gaps[j]);
                    if (dist < MaxGapDistance && dist > ConnectTolerance && dist < bestDist)
                    {
                        // Verify the line doesn't cross existing walls (would create invalid boundary)
                        bestDist = dist;
                        bestIdx = j;
                    }
                }

                if (bestIdx >= 0)
                {
                    try
                    {
                        var startPt = new XYZ(gaps[i].X, gaps[i].Y, level.Elevation);
                        var endPt = new XYZ(gaps[bestIdx].X, gaps[bestIdx].Y, level.Elevation);

                        if (startPt.DistanceTo(endPt) < 0.003) continue; // Skip < 1mm

                        var line = Line.CreateBound(startPt, endPt);
                        var curveArray = new CurveArray();
                        curveArray.Append(line);

                        doc.Create.NewRoomBoundaryLines(sketchPlane, curveArray, doc.ActiveView);
                        result.LinesCreated++;
                        used.Add(i);
                        used.Add(bestIdx);
                    }
                    catch (Exception ex)
                    {
                        result.Messages.Add($"  ⚠️ Skip gap {i}-{bestIdx}: {ex.Message}");
                    }
                }
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            result.Messages.Add($"  ❌ Error: {ex.Message}");
            if (tx.HasStarted()) tx.RollBack();
        }

        tg.Assimilate();
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private void AddCurveSegment(List<WallSegment> segments, Curve curve, double levelZ)
    {
        var start = curve.GetEndPoint(0);
        var end = curve.GetEndPoint(1);
        segments.Add(new WallSegment
        {
            Start2D = new XYZ(start.X, start.Y, 0),
            End2D = new XYZ(end.X, end.Y, 0),
            Length = curve.Length
        });
    }

    private bool IsWallOnLevel(Document doc, Wall wall, Level targetLevel, Transform? linkTransform = null)
    {
        try
        {
            // Get wall's base level
            var baseLevelId = wall.LevelId;
            if (baseLevelId == ElementId.InvalidElementId) return false;

            var wallDoc = linkTransform != null ? wall.Document : doc;
            var baseLevel = wallDoc.GetElement(baseLevelId) as Level;
            if (baseLevel == null) return false;

            double wallBaseZ = baseLevel.Elevation;
            if (linkTransform != null)
                wallBaseZ += linkTransform.Origin.Z;

            // Get wall height
            var heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            double wallHeight = heightParam?.AsDouble() ?? 10; // Default 10 feet

            double wallTopZ = wallBaseZ + wallHeight;
            double targetZ = targetLevel.Elevation;

            // Wall spans this level if its base is at/below and its top is at/above
            return wallBaseZ <= targetZ + 0.1 && wallTopZ >= targetZ + 0.1;
        }
        catch { return false; }
    }

    private static double Distance2D(XYZ a, XYZ b)
    {
        return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    }

    private class WallSegment
    {
        public XYZ Start2D { get; set; } = XYZ.Zero;
        public XYZ End2D { get; set; } = XYZ.Zero;
        public double Length { get; set; }
    }

    private class EndpointInfo
    {
        public XYZ Point { get; set; } = XYZ.Zero;
        public int SegmentIndex { get; set; }
        public bool IsStart { get; set; }
    }
}
