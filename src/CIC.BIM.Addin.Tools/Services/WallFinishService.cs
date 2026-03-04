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
    }

    public class WallFinishResult
    {
        public int WallsCreated { get; set; }
        public int FloorsCreated { get; set; }
        public int RoomsProcessed { get; set; }
        public int OpeningsCut { get; set; }
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

        // Height: auto from room or user-specified
        double heightFeet = GetWallHeight(room, options);
        double baseOffset = options.BaseOffsetMm * MmToFeet;
        double userOffset = options.OffsetMm * MmToFeet; // User-specified boundary offset

        // Lấy bề dày tường HT
        double halfWidth = 0;
        var wallType = doc.GetElement(options.WallTypeId) as WallType;
        if (wallType != null)
            halfWidth = wallType.Width / 2.0;

        // Tâm room dùng để tính hướng "vào trong"
        XYZ roomCenter = GetRoomCenter(room);

        using var tx = new Transaction(doc, $"Tường HT - {room.Name}");
        tx.Start();

        // Track: boundary segment → finish wall (để cắt cửa sau)
        var segmentWallMap = new List<(BoundarySegment Segment, Wall FinishWall)>();

        // Process mỗi boundary loop
        foreach (var loop in boundaries)
        {
            foreach (var seg in loop)
            {
                var curve = seg.GetCurve();
                if (curve == null) continue;
                if (curve.ApproximateLength < 10 * MmToFeet) continue; // Skip < 10mm

                // ═══ OFFSET TỪNG CURVE VÀO TRONG ROOM ═══
                // Tính vector vuông góc hướng VÀO room
                var offsetCurve = OffsetCurveInward(curve, roomCenter, halfWidth + userOffset);
                if (offsetCurve == null)
                {
                    result.Warnings.Add($"Room '{room.Name}': Offset curve thất bại, dùng curve gốc.");
                    offsetCurve = curve;
                }

                // Tạo tường HT
                var finishWall = CreateFinishWall(doc, offsetCurve, options.WallTypeId, levelId,
                    heightFeet, baseOffset, options, result, room.Name);

                if (finishWall != null)
                {
                    segmentWallMap.Add((seg, finishWall));
                }
            }
        }

        // ═══ CẮT Ô CỬA: detect doors/windows trên tường gốc → opening trên tường HT ═══
        CutOpeningsForDoorWindows(doc, segmentWallMap, result, room.Name);

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
    /// Detect doors/windows trên tường gốc bao quanh room,
    /// tạo opening tương ứng trên tường hoàn thiện.
    /// </summary>
    private void CutOpeningsForDoorWindows(Document doc,
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

                    // Chỉ xử lý doors và windows
                    var catId = insertElem.Category?.Id.IntegerValue ?? 0;
                    bool isDoor = catId == (int)BuiltInCategory.OST_Doors;
                    bool isWindow = catId == (int)BuiltInCategory.OST_Windows;
                    if (!isDoor && !isWindow) continue;

                    var bbox = insertElem.get_BoundingBox(null);
                    if (bbox == null) continue;

                    try
                    {
                        // Tạo opening hình chữ nhật trên tường HT
                        var pt1 = new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Min.Z);
                        var pt2 = new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Max.Z);
                        doc.Create.NewOpening(finishWall, pt1, pt2);
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

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private double GetWallHeight(Room room, WallFinishOptions options)
    {
        if (options.HeightMm > 0)
            return options.HeightMm * MmToFeet;

        double heightFeet = room.UnboundedHeight;
        if (heightFeet <= 0)
        {
            var upperLimit = room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET);
            heightFeet = upperLimit?.AsDouble() ?? 3000 * MmToFeet;
        }
        return heightFeet;
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

        foreach (var finishWallId in result.CreatedWallIds)
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
