using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Core engine: tạo tường hoàn thiện (wall finish) theo boundary của Room.
/// Tự động bật Room Bounding cho cột → room boundary bao quanh cột nhô vào.
/// Dùng BoundarySegment.ElementId để phân biệt trát tường vs trát cột.
/// </summary>
public class WallFinishService
{
    public class WallFinishOptions
    {
        public ElementId WallTypeId { get; set; } = ElementId.InvalidElementId;
        public double HeightMm { get; set; } = 0; // 0 = auto from Room
        public double BaseOffsetMm { get; set; } = 0;
        public bool JoinWithOriginal { get; set; } = true;

        // Tách riêng trát tường / trát cột
        public bool CreateWallPlaster { get; set; } = true;
        public bool CreateColumnPlaster { get; set; } = true;
        public ElementId ColumnPlasterTypeId { get; set; } = ElementId.InvalidElementId;
        public bool AutoRoomBounding { get; set; } = true;

        // Optional floor finish
        public bool CreateFloorFinish { get; set; } = false;
        public ElementId FloorTypeId { get; set; } = ElementId.InvalidElementId;
        public double FloorOffsetMm { get; set; } = 0;
    }

    public class WallFinishResult
    {
        public int WallsCreated { get; set; }
        public int ColumnWallsCreated { get; set; }
        public int FloorsCreated { get; set; }
        public int RoomsProcessed { get; set; }
        public int ColumnsSetRoomBounding { get; set; }
        public int LinksSetRoomBounding { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<ElementId> CreatedWallIds { get; set; } = new();
        public List<ElementId> CreatedColumnWallIds { get; set; } = new();
        public List<ElementId> CreatedFloorIds { get; set; } = new();
    }

    private const double MmToFeet = 1.0 / 304.8;

    /// <summary>
    /// Execute wall finish creation for a list of Rooms.
    /// </summary>
    public WallFinishResult Execute(Document doc, IList<Room> rooms, WallFinishOptions options)
    {
        var result = new WallFinishResult();

        using var tg = new TransactionGroup(doc, "Tạo tường hoàn thiện");
        tg.Start();

        // Step 0: Bật Room Bounding cho cột + link instances
        if (options.AutoRoomBounding)
        {
            EnsureRoomBounding(doc, result);
        }

        // Step 1: Process each room
        foreach (var room in rooms)
        {
            try
            {
                ProcessRoom(doc, room, options, result);
                result.RoomsProcessed++;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Room '{room.Name}' (ID:{room.Id.IntegerValue}): {ex.Message}");
            }
        }

        tg.Assimilate();
        return result;
    }

    /// <summary>
    /// Bật Room Bounding cho:
    /// 1. Tất cả cột trong dự án (host)
    /// 2. Tất cả RevitLinkInstance → room nhận biết tường/cột từ file link
    /// </summary>
    private void EnsureRoomBounding(Document doc, WallFinishResult result)
    {
        using var tx = new Transaction(doc, "Bật Room Bounding");
        tx.Start();

        // 1. Bật Room Bounding cho cột host
        var columns = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .WherePasses(new LogicalOrFilter(
                new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns),
                new ElementCategoryFilter(BuiltInCategory.OST_Columns)))
            .ToList();

        foreach (var col in columns)
        {
            var rbParam = col.LookupParameter("Room Bounding");
            if (rbParam != null && !rbParam.IsReadOnly && rbParam.AsInteger() == 0)
            {
                rbParam.Set(1);
                result.ColumnsSetRoomBounding++;
            }
        }

        // 2. Bật Room Bounding cho tất cả RevitLinkInstance
        var linkInstances = new FilteredElementCollector(doc)
            .OfClass(typeof(RevitLinkInstance))
            .ToList();

        foreach (var linkInst in linkInstances)
        {
            var rbParam = linkInst.LookupParameter("Room Bounding");
            if (rbParam != null && !rbParam.IsReadOnly && rbParam.AsInteger() == 0)
            {
                rbParam.Set(1);
                result.LinksSetRoomBounding++;
            }
        }

        tx.Commit();
    }

    private void ProcessRoom(Document doc, Room room, WallFinishOptions options, WallFinishResult result)
    {
        var boundaryOpts = new SpatialElementBoundaryOptions
        {
            SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
        };
        var boundaries = room.GetBoundarySegments(boundaryOpts);
        if (boundaries == null || boundaries.Count == 0)
        {
            result.Warnings.Add($"Room '{room.Name}': không có boundary.");
            return;
        }

        var levelId = room.LevelId;
        if (levelId == ElementId.InvalidElementId)
        {
            result.Warnings.Add($"Room '{room.Name}': không có Level.");
            return;
        }

        // Height: auto from room or user-specified
        double heightFeet;
        if (options.HeightMm > 0)
        {
            heightFeet = options.HeightMm * MmToFeet;
        }
        else
        {
            heightFeet = room.UnboundedHeight;
            if (heightFeet <= 0)
            {
                var upperLimit = room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET);
                heightFeet = upperLimit?.AsDouble() ?? 3000 * MmToFeet;
            }
        }

        double baseOffset = options.BaseOffsetMm * MmToFeet;

        // Determine column plaster type
        var colTypeId = options.ColumnPlasterTypeId != ElementId.InvalidElementId
            ? options.ColumnPlasterTypeId
            : options.WallTypeId;

        using var tx = new Transaction(doc, $"Tường HT - {room.Name}");
        tx.Start();

        // Process boundary segments with deduplication
        // Pass 1: host elements (priority), Pass 2: linked elements
        var createdMidpoints = new List<XYZ>(); // Track created wall midpoints for dedup

        foreach (var loop in boundaries)
        {
            // Pass 1: host elements only
            foreach (var segment in loop)
            {
                var boundingElem = doc.GetElement(segment.ElementId);
                if (boundingElem is RevitLinkInstance) continue; // Skip linked — process in pass 2

                var curve = segment.GetCurve();
                if (curve == null || curve.ApproximateLength < 0.01) continue;

                bool isColumn = boundingElem != null && (
                    boundingElem.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns ||
                    boundingElem.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Columns);

                if (isColumn && !options.CreateColumnPlaster) continue;
                if (!isColumn && !options.CreateWallPlaster) continue;

                var typeId = isColumn ? colTypeId : options.WallTypeId;
                if (typeId == ElementId.InvalidElementId) continue;

                CreatePlasterWall(doc, curve, typeId, levelId, heightFeet, baseOffset,
                    isColumn, result, room.Name, createdMidpoints);
            }

            // Pass 2: linked elements (skip if already covered by host)
            foreach (var segment in loop)
            {
                var boundingElem = doc.GetElement(segment.ElementId);
                if (boundingElem is not RevitLinkInstance linkInst) continue;

                var curve = segment.GetCurve();
                if (curve == null || curve.ApproximateLength < 0.01) continue;

                // Dedup: skip if a plaster wall already exists within 200mm of this curve midpoint
                var midPt = curve.Evaluate(0.5, true);
                bool isDuplicate = false;
                foreach (var existing in createdMidpoints)
                {
                    if (midPt.DistanceTo(existing) < 200 * MmToFeet)
                    { isDuplicate = true; break; }
                }
                if (isDuplicate) continue;

                bool isColumn = IsLinkedSegmentFromColumn(linkInst, curve);

                if (isColumn && !options.CreateColumnPlaster) continue;
                if (!isColumn && !options.CreateWallPlaster) continue;

                var typeId = isColumn ? colTypeId : options.WallTypeId;
                if (typeId == ElementId.InvalidElementId) continue;

                // For linked WALLS: split curve at door/window openings
                if (!isColumn)
                {
                    var splitCurves = SplitCurveAtLinkedOpenings(linkInst, curve);
                    foreach (var sc in splitCurves)
                    {
                        CreatePlasterWall(doc, sc, typeId, levelId, heightFeet, baseOffset,
                            false, result, room.Name, createdMidpoints);
                    }
                }
                else
                {
                    CreatePlasterWall(doc, curve, typeId, levelId, heightFeet, baseOffset,
                        true, result, room.Name, createdMidpoints);
                }
            }
        }

        // Optional: Create floor finish
        if (options.CreateFloorFinish && options.FloorTypeId != ElementId.InvalidElementId)
        {
            try
            {
                CreateFloorFinish(doc, room, boundaries, options, result);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Sàn HT '{room.Name}': {ex.Message}");
            }
        }

        // Join geometry with original walls and columns
        if (options.JoinWithOriginal)
        {
            JoinFinishWithOriginalElements(doc, room, result);
        }

        tx.Commit();
    }

    /// <summary>
    /// Tạo 1 tường trát và track midpoint cho dedup.
    /// </summary>
    private void CreatePlasterWall(Document doc, Curve curve, ElementId typeId,
        ElementId levelId, double heightFeet, double baseOffset,
        bool isColumn, WallFinishResult result, string roomName, List<XYZ> createdMidpoints)
    {
        try
        {
            var wall = Wall.Create(
                doc, curve, typeId, levelId,
                heightFeet, baseOffset, false, false);

            if (wall != null)
            {
                var structParam = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
                if (structParam != null && !structParam.IsReadOnly)
                    structParam.Set(0);

                if (isColumn)
                {
                    result.CreatedColumnWallIds.Add(wall.Id);
                    result.ColumnWallsCreated++;
                }
                else
                {
                    result.CreatedWallIds.Add(wall.Id);
                    result.WallsCreated++;
                }

                createdMidpoints.Add(curve.Evaluate(0.5, true));
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Segment in '{roomName}': {ex.Message}");
        }
    }

    /// <summary>
    /// Split boundary curve at door/window openings in linked wall.
    /// Tìm wall gần nhất trong linked doc → lấy doors/windows → split curve tại opening.
    /// </summary>
    private List<Curve> SplitCurveAtLinkedOpenings(RevitLinkInstance linkInst, Curve segCurve)
    {
        var fallback = new List<Curve> { segCurve };

        var linkDoc = linkInst.GetLinkDocument();
        if (linkDoc == null) return fallback;
        if (segCurve is not Line segLine) return fallback;

        var transform = linkInst.GetTotalTransform();

        // Transform segment endpoints to link coordinate system
        var p0 = transform.Inverse.OfPoint(segLine.GetEndPoint(0));
        var p1 = transform.Inverse.OfPoint(segLine.GetEndPoint(1));
        var linkMidPt = (p0 + p1) / 2;

        // Find nearest wall in linked doc
        double searchRadius = 500 * MmToFeet;
        var searchOutline = new Outline(
            new XYZ(linkMidPt.X - searchRadius, linkMidPt.Y - searchRadius, linkMidPt.Z - 5000 * MmToFeet),
            new XYZ(linkMidPt.X + searchRadius, linkMidPt.Y + searchRadius, linkMidPt.Z + 5000 * MmToFeet));

        Wall? closestWall = null;
        double minDist = double.MaxValue;

        var nearbyWalls = new FilteredElementCollector(linkDoc)
            .OfClass(typeof(Wall))
            .WherePasses(new BoundingBoxIntersectsFilter(searchOutline))
            .Cast<Wall>()
            .Where(w => w.Width > 50 * MmToFeet)
            .ToList();

        foreach (var wall in nearbyWalls)
        {
            var wallLoc = wall.Location as LocationCurve;
            if (wallLoc == null) continue;
            var wallMid = wallLoc.Curve.Evaluate(0.5, true);
            double dist = linkMidPt.DistanceTo(wallMid);
            if (dist < minDist) { minDist = dist; closestWall = wall; }
        }

        if (closestWall == null) return fallback;

        // Find doors and windows hosted on this wall
        var openings = new FilteredElementCollector(linkDoc)
            .WhereElementIsNotElementType()
            .WherePasses(new LogicalOrFilter(
                new ElementCategoryFilter(BuiltInCategory.OST_Doors),
                new ElementCategoryFilter(BuiltInCategory.OST_Windows)))
            .Cast<FamilyInstance>()
            .Where(fi => fi.Host?.Id == closestWall.Id)
            .ToList();

        if (openings.Count == 0) return fallback;

        // Project each opening range onto the segment curve (as normalized parameter 0..1)
        var segDir = (segLine.GetEndPoint(1) - segLine.GetEndPoint(0));
        double segLen = segDir.GetLength();
        if (segLen < 0.01) return fallback;
        var segDirNorm = segDir.Normalize();
        var segStart = segLine.GetEndPoint(0);

        // Collect opening ranges as (tStart, tEnd) on the HOST segment curve
        var openingRanges = new List<(double tStart, double tEnd)>();

        foreach (var opening in openings)
        {
            // Get opening location in link coordinates
            var openLoc = opening.Location as LocationPoint;
            if (openLoc == null) continue;
            var openPtLink = openLoc.Point;

            // Transform to host coordinates
            var openPtHost = transform.OfPoint(openPtLink);

            // Get opening width
            double openWidth = 0;
            var widthParam = opening.Symbol?.get_Parameter(BuiltInParameter.DOOR_WIDTH)
                ?? opening.Symbol?.get_Parameter(BuiltInParameter.WINDOW_WIDTH)
                ?? opening.Symbol?.LookupParameter("Width")
                ?? opening.Symbol?.LookupParameter("Rough Width");
            if (widthParam != null)
                openWidth = widthParam.AsDouble();
            if (openWidth <= 0)
                openWidth = 900 * MmToFeet; // Default 900mm

            // Project opening center onto segment line
            var toOpen = openPtHost - segStart;
            double t = toOpen.DotProduct(segDirNorm) / segLen; // Normalized 0..1

            double halfWidthT = (openWidth / 2) / segLen;
            double tStart = t - halfWidthT - (50 * MmToFeet / segLen); // 50mm margin
            double tEnd = t + halfWidthT + (50 * MmToFeet / segLen);

            openingRanges.Add((Math.Max(0, tStart), Math.Min(1, tEnd)));
        }

        if (openingRanges.Count == 0) return fallback;

        // Sort and merge overlapping ranges
        openingRanges.Sort((a, b) => a.tStart.CompareTo(b.tStart));
        var merged = new List<(double tStart, double tEnd)> { openingRanges[0] };
        for (int i = 1; i < openingRanges.Count; i++)
        {
            var last = merged[merged.Count - 1];
            if (openingRanges[i].tStart <= last.tEnd)
                merged[merged.Count - 1] = (last.tStart, Math.Max(last.tEnd, openingRanges[i].tEnd));
            else
                merged.Add(openingRanges[i]);
        }

        // Create sub-curves for non-opening segments
        var resultCurves = new List<Curve>();
        double prevEnd = 0;

        foreach (var (tStart, tEnd) in merged)
        {
            if (tStart > prevEnd + 0.01)
            {
                var subStart = segLine.Evaluate(prevEnd, true);
                var subEnd = segLine.Evaluate(tStart, true);
                if (subStart.DistanceTo(subEnd) > 30 * MmToFeet)
                    resultCurves.Add(Line.CreateBound(subStart, subEnd));
            }
            prevEnd = tEnd;
        }

        // Last segment after final opening
        if (prevEnd < 0.99)
        {
            var subStart = segLine.Evaluate(prevEnd, true);
            var subEnd = segLine.GetEndPoint(1);
            if (subStart.DistanceTo(subEnd) > 30 * MmToFeet)
                resultCurves.Add(Line.CreateBound(subStart, subEnd));
        }

        return resultCurves.Count > 0 ? resultCurves : fallback;
    }

    /// <summary>
    /// Kiểm tra xem boundary segment từ linked file có phải từ cột không.
    /// Tìm cột trong linked doc gần curve midpoint (transform về hệ tọa độ link).
    /// </summary>
    private bool IsLinkedSegmentFromColumn(RevitLinkInstance linkInst, Curve segCurve)
    {
        var linkDoc = linkInst.GetLinkDocument();
        if (linkDoc == null) return false;

        // Transform midpoint từ host → link coordinate system
        var transform = linkInst.GetTotalTransform();
        var midPt = segCurve.Evaluate(0.5, true);
        var linkPt = transform.Inverse.OfPoint(midPt);

        // Search for columns near this point in the linked doc
        double searchRadius = 300 * MmToFeet; // 300mm search
        var searchOutline = new Outline(
            new XYZ(linkPt.X - searchRadius, linkPt.Y - searchRadius, linkPt.Z - 3000 * MmToFeet),
            new XYZ(linkPt.X + searchRadius, linkPt.Y + searchRadius, linkPt.Z + 3000 * MmToFeet));

        var nearbyColumns = new FilteredElementCollector(linkDoc)
            .WherePasses(new LogicalOrFilter(
                new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns),
                new ElementCategoryFilter(BuiltInCategory.OST_Columns)))
            .WherePasses(new BoundingBoxIntersectsFilter(searchOutline))
            .GetElementCount();

        return nearbyColumns > 0;
    }

    private void CreateFloorFinish(Document doc, Room room, IList<IList<BoundarySegment>> boundaries,
        WallFinishOptions options, WallFinishResult result)
    {
        var outerLoop = boundaries[0];
        var curveLoop = new CurveLoop();
        foreach (var seg in outerLoop)
        {
            var c = seg.GetCurve();
            if (c != null) curveLoop.Append(c);
        }

        var curveLoops = new List<CurveLoop> { curveLoop };

        for (int i = 1; i < boundaries.Count; i++)
        {
            var innerLoop = new CurveLoop();
            foreach (var seg in boundaries[i])
            {
                var c = seg.GetCurve();
                if (c != null) innerLoop.Append(c);
            }
            curveLoops.Add(innerLoop);
        }

        var levelId = room.LevelId;
        var floor = Floor.Create(doc, curveLoops, options.FloorTypeId, levelId);

        if (floor != null)
        {
            var offsetParam = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
            if (offsetParam != null && !offsetParam.IsReadOnly)
                offsetParam.Set(options.FloorOffsetMm * MmToFeet);

            result.CreatedFloorIds.Add(floor.Id);
            result.FloorsCreated++;
        }
    }

    /// <summary>
    /// Join lớp trát với tường gốc và cột gốc.
    /// </summary>
    private void JoinFinishWithOriginalElements(Document doc, Room room, WallFinishResult result)
    {
        var boundaryOpts = new SpatialElementBoundaryOptions();
        var boundaries = room.GetBoundarySegments(boundaryOpts);
        if (boundaries == null) return;

        // Collect original bounding elements (walls + columns)
        var originalIds = new HashSet<ElementId>();
        foreach (var loop in boundaries)
        {
            foreach (var seg in loop)
            {
                if (seg.ElementId != ElementId.InvalidElementId)
                    originalIds.Add(seg.ElementId);
            }
        }

        // Join wall plaster with original elements
        var allFinishIds = new List<ElementId>();
        allFinishIds.AddRange(result.CreatedWallIds);
        allFinishIds.AddRange(result.CreatedColumnWallIds);

        foreach (var finishWallId in allFinishIds)
        {
            var finishWall = doc.GetElement(finishWallId);
            if (finishWall == null) continue;

            foreach (var origId in originalIds)
            {
                var origElem = doc.GetElement(origId);
                if (origElem == null) continue;

                try
                {
                    if (!JoinGeometryUtils.AreElementsJoined(doc, finishWall, origElem))
                        JoinGeometryUtils.JoinGeometry(doc, finishWall, origElem);
                }
                catch { /* Some joins may fail — skip silently */ }
            }
        }
    }
}
