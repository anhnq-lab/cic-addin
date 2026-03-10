using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Tạo tường hoàn thiện theo boundary của Room.
/// Nguyên tắc: Room đã chuẩn rồi → lấy boundary Finish → offset vào trong ½ wall width → tạo wall.
/// </summary>
public class WallFinishService
{
    public class WallFinishOptions
    {
        public ElementId WallTypeId { get; set; } = ElementId.InvalidElementId;
        public double HeightMm { get; set; } = 0; // 0 = auto from Room
        public double BaseOffsetMm { get; set; } = 0; // Offset đáy (phương đứng Z)
        public double OffsetMm { get; set; } = 0; // Offset biên dạng: dương = co vào trong, âm = nới ra
        public bool JoinWithOriginal { get; set; } = true;

        // Gắn tham biến tên Room vào element đã tạo
        public bool AssignRoomName { get; set; } = true;
        public string RoomNameParam { get; set; } = "Comments";

        // Optional floor finish
        public bool CreateFloorFinish { get; set; } = false;
        public ElementId FloorTypeId { get; set; } = ElementId.InvalidElementId;
        public double FloorOffsetMm { get; set; } = 0;

        // Nhận diện trần
        public bool DetectCeiling { get; set; } = false;
        public double CeilingOverlapMm { get; set; } = 50; // Trát quá trần thêm 50mm
    }

    public class WallFinishResult
    {
        public int WallsCreated { get; set; }
        public int FloorsCreated { get; set; }
        public int RoomsProcessed { get; set; }
        public int OpeningsCut { get; set; }
        public int StairOpeningsCut { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<ElementId> CreatedWallIds { get; set; } = new();
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
    /// Chỉ tạo sàn hoàn thiện (không tạo tường) — dùng cho tab Sàn riêng.
    /// </summary>
    public WallFinishResult ExecuteFloorOnly(Document doc, IList<Room> rooms, WallFinishOptions options)
    {
        var result = new WallFinishResult();

        using var tg = new TransactionGroup(doc, "Tạo sàn hoàn thiện");
        tg.Start();

        foreach (var room in rooms)
        {
            try
            {
                var boundaryOpts = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                };
                var boundaries = room.GetBoundarySegments(boundaryOpts);
                if (boundaries == null || boundaries.Count == 0)
                {
                    result.Warnings.Add($"Room '{room.Name}': không có boundary.");
                    continue;
                }

                double offsetFeet = options.OffsetMm * MmToFeet;

                using var tx = new Transaction(doc, $"Sàn HT - {room.Name}");
                tx.Start();

                CreateFloorFinish(doc, room, boundaries, offsetFeet, options, result);

                tx.Commit();
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

    // ═══════════════════════════════════════════════════════════════
    //  CORE: ProcessRoom
    // ═══════════════════════════════════════════════════════════════

    private void ProcessRoom(Document doc, Room room, WallFinishOptions options, WallFinishResult result)
    {
        // Lấy boundary theo Finish location (mặt hoàn thiện = mặt trong tường gốc)
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

        // Height: auto from room, detect ceiling, or user-specified
        double heightFeet = GetWallHeight(doc, room, options);
        double baseOffset = options.BaseOffsetMm * MmToFeet;
        double userOffset = options.OffsetMm * MmToFeet; // User-specified boundary offset

        // Lấy bề dày tường HT
        double halfWidth = 0;
        var wallType = doc.GetElement(options.WallTypeId) as WallType;
        if (wallType != null)
            halfWidth = wallType.Width / 2.0;

        double totalOffset = halfWidth + userOffset;

        using var tx = new Transaction(doc, $"Tường HT - {room.Name}");
        tx.Start();

        // Track: boundary segment → finish wall (để cắt cửa sau)
        var segmentWallMap = new List<(BoundarySegment Segment, Wall FinishWall)>();

        // Process mỗi boundary loop
        foreach (var loop in boundaries)
        {
            // ═══ THỬ OFFSET THEO CURVELOOP TRƯỚC (chính xác cho room lõm) ═══
            var offsetCurves = TryOffsetLoop(loop, totalOffset);

            if (offsetCurves != null)
            {
                // Dùng kết quả CurveLoop offset
                int segIdx = 0;
                foreach (var offsetCurve in offsetCurves)
                {
                    if (offsetCurve.ApproximateLength < 10 * MmToFeet) { segIdx++; continue; }

                    var matchingSeg = segIdx < loop.Count ? loop[segIdx] : loop[loop.Count - 1];
                    var finishWall = CreateFinishWall(doc, offsetCurve, options.WallTypeId, levelId,
                        heightFeet, baseOffset, options, result, room.Name);
                    if (finishWall != null)
                        segmentWallMap.Add((matchingSeg, finishWall));
                    segIdx++;
                }
            }
            else
            {
                // ═══ FALLBACK: offset từng segment riêng lẻ ═══
                XYZ roomCenter = GetRoomCenter(room);
                foreach (var seg in loop)
                {
                    var curve = seg.GetCurve();
                    if (curve == null) continue;
                    if (curve.ApproximateLength < 10 * MmToFeet) continue;

                    var offsetCurve = OffsetCurveInward(curve, roomCenter, totalOffset);
                    if (offsetCurve == null) offsetCurve = curve;

                    var finishWall = CreateFinishWall(doc, offsetCurve, options.WallTypeId, levelId,
                        heightFeet, baseOffset, options, result, room.Name);
                    if (finishWall != null)
                        segmentWallMap.Add((seg, finishWall));
                }
            }
        }

        // ═══ CẮT Ô CỬA + CẦU THANG ═══
        CutOpeningsForInserts(doc, segmentWallMap, result, room.Name);
        CutOpeningsForNearbyStairs(doc, result, room.Name);

        // Optional: Tạo sàn hoàn thiện
        if (options.CreateFloorFinish && options.FloorTypeId != ElementId.InvalidElementId)
        {
            try
            {
                CreateFloorFinish(doc, room, boundaries, userOffset, options, result);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Sàn HT '{room.Name}': {ex.Message}");
            }
        }

        // Join geometry với tường/cột gốc
        if (options.JoinWithOriginal)
        {
            JoinFinishWithOriginalElements(doc, room, result);
        }

        tx.Commit();
    }

    /// <summary>
    /// Thử offset toàn bộ CurveLoop — chính xác cho room lõm/L-shape.
    /// Trả về null nếu thất bại.
    /// </summary>
    private List<Curve>? TryOffsetLoop(IList<BoundarySegment> segments, double offsetDistance)
    {
        if (Math.Abs(offsetDistance) < 1e-9)
        {
            // Không offset → trả về curves gốc
            return segments.Select(s => s.GetCurve()).Where(c => c != null).ToList()!;
        }

        try
        {
            var loop = new CurveLoop();
            foreach (var seg in segments)
            {
                var c = seg.GetCurve();
                if (c != null) loop.Append(c);
            }

            // CurveLoop.CreateViaOffset offset theo normal (BasisZ)
            // Giá trị dương = co vào trong (nếu loop counterclockwise), xử lý đúng room lõm
            var offsetLoop = CurveLoop.CreateViaOffset(loop, offsetDistance, XYZ.BasisZ);
            return offsetLoop.ToList();
        }
        catch
        {
            return null; // Offset fail → fallback
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  OFFSET: Dịch curve vào phía trong room
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Offset 1 curve vào phía trong room (hướng tâm room).
    /// Dùng cho từng segment riêng lẻ, đáng tin cậy hơn CurveLoop offset.
    /// </summary>
    private Curve? OffsetCurveInward(Curve curve, XYZ roomCenter, double offsetDistance)
    {
        if (Math.Abs(offsetDistance) < 1e-9)
            return curve;

        try
        {
            // Lấy midpoint và tangent tại giữa curve
            var midPoint = curve.Evaluate(0.5, true);
            var tangent = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();

            // Vector vuông góc (nằm ngang, vuông góc với curve)
            var perpendicular = XYZ.BasisZ.CrossProduct(tangent).Normalize();

            // Kiểm tra hướng: perpendicular phải chỉ VÀO room (hướng tâm)
            var toCenter = new XYZ(roomCenter.X - midPoint.X, roomCenter.Y - midPoint.Y, 0).Normalize();
            double dot = perpendicular.DotProduct(toCenter);

            // Nếu perpendicular chỉ ra ngoài → đảo chiều
            if (dot < 0)
                perpendicular = perpendicular.Negate();

            // Dịch curve theo hướng vào trong
            var translation = Transform.CreateTranslation(perpendicular * offsetDistance);
            return curve.CreateTransformed(translation);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Lấy tâm room (dùng Location hoặc trung bình boundary).
    /// </summary>
    private XYZ GetRoomCenter(Room room)
    {
        // Thử LocationPoint trước
        if (room.Location is LocationPoint lp)
            return lp.Point;

        // Fallback: trung bình các boundary points
        try
        {
            var opts = new SpatialElementBoundaryOptions();
            var boundaries = room.GetBoundarySegments(opts);
            if (boundaries != null && boundaries.Count > 0)
            {
                double sumX = 0, sumY = 0, sumZ = 0;
                int count = 0;
                foreach (var seg in boundaries[0])
                {
                    var pt = seg.GetCurve().GetEndPoint(0);
                    sumX += pt.X; sumY += pt.Y; sumZ += pt.Z;
                    count++;
                }
                if (count > 0)
                    return new XYZ(sumX / count, sumY / count, sumZ / count);
            }
        }
        catch { }

        return XYZ.Zero;
    }

    // ═══════════════════════════════════════════════════════════════
    //  TẠO TƯỜNG
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Tạo 1 tường hoàn thiện. Trả về Wall để map với boundary segment.
    /// </summary>
    private Wall? CreateFinishWall(Document doc, Curve curve, ElementId typeId,
        ElementId levelId, double heightFeet, double baseOffset,
        WallFinishOptions options, WallFinishResult result, string roomName)
    {
        if (typeId == ElementId.InvalidElementId) return null;

        try
        {
            var wall = Wall.Create(doc, curve, typeId, levelId,
                heightFeet, baseOffset, false, false);

            if (wall != null)
            {
                // Đánh dấu non-structural
                var structParam = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
                if (structParam != null && !structParam.IsReadOnly)
                    structParam.Set(0);

                // Gắn tên Room vào parameter
                if (options.AssignRoomName && !string.IsNullOrEmpty(options.RoomNameParam))
                {
                    SetElementParam(wall, options.RoomNameParam, roomName);
                }

                result.CreatedWallIds.Add(wall.Id);
                result.WallsCreated++;
                return wall;
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Wall in '{roomName}': {ex.Message}");
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════
    //  CẮT Ô CỬA
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Detect doors/windows/stairs trên tường gốc bao quanh room,
    /// tạo opening tương ứng trên tường hoàn thiện.
    /// </summary>
    private void CutOpeningsForInserts(Document doc,
        List<(BoundarySegment Segment, Wall FinishWall)> segmentWallMap,
        WallFinishResult result, string roomName)
    {
        foreach (var (seg, finishWall) in segmentWallMap)
        {
            try
            {
                // Lấy tường gốc từ boundary segment
                var origWall = doc.GetElement(seg.ElementId) as Wall;
                if (origWall == null) continue;

                // Tìm tất cả inserts trên tường gốc (doors, windows, openings)
                var inserts = origWall.FindInserts(true, true, true, true);
                if (inserts == null || inserts.Count == 0) continue;

                foreach (var insertId in inserts)
                {
                    var insertElem = doc.GetElement(insertId);
                    if (insertElem == null) continue;

                    // Xử lý doors, windows VÀ stairs
                    var catId = insertElem.Category?.Id.IntegerValue ?? 0;
                    bool isDoor = catId == (int)BuiltInCategory.OST_Doors;
                    bool isWindow = catId == (int)BuiltInCategory.OST_Windows;
                    bool isStair = catId == (int)BuiltInCategory.OST_Stairs;
                    bool isStairRun = catId == (int)BuiltInCategory.OST_StairsRuns;
                    bool isStairLanding = catId == (int)BuiltInCategory.OST_StairsLandings;
                    if (!isDoor && !isWindow && !isStair && !isStairRun && !isStairLanding) continue;

                    var bbox = insertElem.get_BoundingBox(null);
                    if (bbox == null) continue;

                    try
                    {
                        // Tạo opening hình chữ nhật trên tường HT
                        var pt1 = new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Min.Z);
                        var pt2 = new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Max.Z);
                        doc.Create.NewOpening(finishWall, pt1, pt2);

                        if (isStair || isStairRun || isStairLanding)
                            result.StairOpeningsCut++;
                        else
                            result.OpeningsCut++;
                    }
                    catch
                    {
                        // NewOpening có thể fail nếu opening ngoài phạm vi wall → skip
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Cut opening '{roomName}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Tìm cầu thang gần tường hoàn thiện (không nằm trên tường gốc)
    /// và tạo opening trên tường HT.
    /// </summary>
    private void CutOpeningsForNearbyStairs(Document doc, WallFinishResult result, string roomName)
    {
        if (result.CreatedWallIds.Count == 0) return;

        // Thu thập tất cả Stairs trong model
        var stairs = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Stairs)
            .WhereElementIsNotElementType()
            .ToList();

        if (stairs.Count == 0) return;

        // Duyệt qua mỗi finishing wall, kiểm tra giao với BoundingBox Stairs
        foreach (var finishWallId in result.CreatedWallIds)
        {
            var finishWall = doc.GetElement(finishWallId) as Wall;
            if (finishWall == null) continue;

            var wallBBox = finishWall.get_BoundingBox(null);
            if (wallBBox == null) continue;

            foreach (var stairElem in stairs)
            {
                var stairBBox = stairElem.get_BoundingBox(null);
                if (stairBBox == null) continue;

                // Kiểm tra BoundingBox giao nhau (X-Y plane)
                if (!BBoxOverlap2D(wallBBox, stairBBox)) continue;

                try
                {
                    var pt1 = new XYZ(stairBBox.Min.X, stairBBox.Min.Y, stairBBox.Min.Z);
                    var pt2 = new XYZ(stairBBox.Max.X, stairBBox.Max.Y, stairBBox.Max.Z);
                    doc.Create.NewOpening(finishWall, pt1, pt2);
                    result.StairOpeningsCut++;
                }
                catch
                {
                    // Opening ngoài phạm vi wall hoặc đã cut → skip
                }
            }
        }
    }

    /// <summary>
    /// Kiểm tra 2 BoundingBox giao nhau trên mặt phẳng XY (bỏ qua Z).
    /// </summary>
    private static bool BBoxOverlap2D(BoundingBoxXYZ a, BoundingBoxXYZ b)
    {
        // Mở rộng nhẹ tolerance (1mm = ~0.00328 feet)
        const double tol = 0.01; // ~3mm tolerance
        return a.Min.X - tol <= b.Max.X && a.Max.X + tol >= b.Min.X
            && a.Min.Y - tol <= b.Max.Y && a.Max.Y + tol >= b.Min.Y;
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private double GetWallHeight(Document doc, Room room, WallFinishOptions options)
    {
        if (options.HeightMm > 0)
            return options.HeightMm * MmToFeet;

        // Nhận diện trần
        if (options.DetectCeiling)
        {
            double ceilingHeight = FindCeilingHeightAboveRoom(doc, room);
            if (ceilingHeight > 0)
            {
                // Trát quá trần thêm 1 khoảng
                return ceilingHeight + options.CeilingOverlapMm * MmToFeet;
            }
        }

        double heightFeet = room.UnboundedHeight;
        if (heightFeet <= 0)
        {
            var upperLimit = room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET);
            heightFeet = upperLimit?.AsDouble() ?? 3000 * MmToFeet;
        }
        return heightFeet;
    }

    /// <summary>
    /// Tìm chiều cao trần phía trên Room (tính từ Level của Room).
    /// Trả về chiều cao (feet) hoặc 0 nếu không tìm thấy trần.
    /// </summary>
    private double FindCeilingHeightAboveRoom(Document doc, Room room)
    {
        var roomLevelId = room.LevelId;
        if (roomLevelId == ElementId.InvalidElementId) return 0;

        var roomLevel = doc.GetElement(roomLevelId) as Level;
        if (roomLevel == null) return 0;

        // Lấy BoundingBox Room để so sánh overlap
        var roomBBox = room.get_BoundingBox(null);
        if (roomBBox == null) return 0;

        // Tìm ceilings trên cùng Level hoặc gần room
        var ceilings = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Ceilings)
            .WhereElementIsNotElementType()
            .ToList();

        double bestHeight = 0;

        foreach (var ceiling in ceilings)
        {
            var ceilingBBox = ceiling.get_BoundingBox(null);
            if (ceilingBBox == null) continue;

            // Kiểm tra overlap XY với room
            if (!BBoxOverlap2D(roomBBox, ceilingBBox)) continue;

            // Lấy chiều cao thấp nhất của trần (mặt dưới)
            double ceilingBottomZ = ceilingBBox.Min.Z;

            // Tính chiều cao từ Level lên trần
            double heightFromLevel = ceilingBottomZ - roomLevel.Elevation;
            if (heightFromLevel > 0 && (bestHeight == 0 || heightFromLevel < bestHeight))
            {
                bestHeight = heightFromLevel;
            }
        }

        return bestHeight;
    }

    private void CreateFloorFinish(Document doc, Room room, IList<IList<BoundarySegment>> boundaries,
        double offsetFeet, WallFinishOptions options, WallFinishResult result)
    {
        var curveLoops = new List<CurveLoop>();

        for (int i = 0; i < boundaries.Count; i++)
        {
            var loop = BuildCurveLoop(boundaries[i]);
            if (loop == null) continue;

            // Áp dụng offset cho sàn (dùng CurveLoop offset OK cho sàn vì chỉ co/giãn)
            if (Math.Abs(offsetFeet) > 1e-9)
            {
                try
                {
                    loop = CurveLoop.CreateViaOffset(loop, offsetFeet, XYZ.BasisZ);
                }
                catch { /* Dùng loop gốc */ }
            }

            curveLoops.Add(loop);
        }

        if (curveLoops.Count == 0) return;

        var levelId = room.LevelId;
        var floor = Floor.Create(doc, curveLoops, options.FloorTypeId, levelId);

        if (floor != null)
        {
            var offsetParam = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
            if (offsetParam != null && !offsetParam.IsReadOnly)
                offsetParam.Set(options.FloorOffsetMm * MmToFeet);

            if (options.AssignRoomName && !string.IsNullOrEmpty(options.RoomNameParam))
            {
                SetElementParam(floor, options.RoomNameParam, room.Name);
            }

            result.CreatedFloorIds.Add(floor.Id);
            result.FloorsCreated++;
        }
    }

    private CurveLoop? BuildCurveLoop(IList<BoundarySegment> segments)
    {
        try
        {
            var curveLoop = new CurveLoop();
            foreach (var seg in segments)
            {
                var c = seg.GetCurve();
                if (c != null)
                    curveLoop.Append(c);
            }
            return curveLoop;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Join lớp hoàn thiện với tường/cột gốc bao quanh room.
    /// Chỉ join với Wall và FamilyInstance (cột), bỏ qua Room Separation Lines, Model Lines, v.v.
    /// </summary>
    private void JoinFinishWithOriginalElements(Document doc, Room room, WallFinishResult result)
    {
        var boundaryOpts = new SpatialElementBoundaryOptions();
        var boundaries = room.GetBoundarySegments(boundaryOpts);
        if (boundaries == null) return;

        var originalIds = new HashSet<ElementId>();
        foreach (var loop in boundaries)
        {
            foreach (var seg in loop)
            {
                if (seg.ElementId != ElementId.InvalidElementId)
                    originalIds.Add(seg.ElementId);
            }
        }

        // Chỉ join với các loại element hỗ trợ: Wall, FamilyInstance (cột)
        var joinableCategories = new HashSet<int>
        {
            (int)BuiltInCategory.OST_Walls,
            (int)BuiltInCategory.OST_StructuralColumns,
            (int)BuiltInCategory.OST_Columns
        };

        foreach (var finishWallId in result.CreatedWallIds)
        {
            var finishWall = doc.GetElement(finishWallId);
            if (finishWall == null) continue;

            foreach (var origId in originalIds)
            {
                var origElem = doc.GetElement(origId);
                if (origElem == null) continue;

                // Bỏ qua Room Separation Lines, Model Lines, và các element không hỗ trợ join
                var catId = origElem.Category?.Id.IntegerValue ?? 0;
                if (!joinableCategories.Contains(catId)) continue;

                try
                {
                    if (!JoinGeometryUtils.AreElementsJoined(doc, finishWall, origElem))
                        JoinGeometryUtils.JoinGeometry(doc, finishWall, origElem);
                }
                catch { }
            }
        }
    }

    private void SetElementParam(Element element, string paramName, string value)
    {
        var param = element.LookupParameter(paramName);
        if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
        {
            param.Set(value);
        }
    }
}
