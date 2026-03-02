using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Tạo tường hoàn thiện theo boundary của Room.
/// Nguyên tắc: Room đã chuẩn rồi → lấy boundary → offset (nếu có) → tạo wall.
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
        public string RoomNameParam { get; set; } = "Comments"; // Tên parameter để ghi tên Room

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

    private void ProcessRoom(Document doc, Room room, WallFinishOptions options, WallFinishResult result)
    {
        // Lấy boundary theo Finish location (mặt hoàn thiện room)
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
        double offsetFeet = options.OffsetMm * MmToFeet;

        using var tx = new Transaction(doc, $"Tường HT - {room.Name}");
        tx.Start();

        // Process mỗi boundary loop (outer + inner holes)
        foreach (var loop in boundaries)
        {
            // Gom segments thành CurveLoop
            var curveLoop = BuildCurveLoop(loop);
            if (curveLoop == null || curveLoop.NumberOfCurves() == 0)
                continue;

            // Offset biên dạng (nếu có)
            var workingLoop = ApplyOffset(curveLoop, offsetFeet, room.Name, result);

            // Tạo wall cho từng curve trong loop
            foreach (var curve in workingLoop)
            {
                if (curve.ApproximateLength < 10 * MmToFeet) // Skip quá ngắn (<10mm)
                    continue;

                CreateFinishWall(doc, curve, options.WallTypeId, levelId,
                    heightFeet, baseOffset, options, result, room.Name);
            }
        }

        // Optional: Tạo sàn hoàn thiện
        if (options.CreateFloorFinish && options.FloorTypeId != ElementId.InvalidElementId)
        {
            try
            {
                CreateFloorFinish(doc, room, boundaries, offsetFeet, options, result);
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
    /// Gom BoundarySegment thành 1 CurveLoop khép kín.
    /// </summary>
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
    /// Offset CurveLoop theo biên dạng room.
    /// Dương = co vào trong (inward), Âm = nới ra ngoài.
    /// </summary>
    private CurveLoop ApplyOffset(CurveLoop original, double offsetFeet, string roomName, WallFinishResult result)
    {
        if (Math.Abs(offsetFeet) < 1e-9)
            return original;

        try
        {
            // CurveLoop.CreateViaOffset: offset theo normal (Z axis cho mặt phẳng ngang)
            // Room boundary thường ngược chiều kim đồng hồ nhìn từ trên
            // → offset dương = co vào trong
            var offsetLoop = CurveLoop.CreateViaOffset(original, offsetFeet, XYZ.BasisZ);
            return offsetLoop;
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Room '{roomName}': Offset thất bại ({ex.Message}), dùng biên gốc.");
            return original;
        }
    }

    /// <summary>
    /// Tạo 1 tường hoàn thiện.
    /// </summary>
    private void CreateFinishWall(Document doc, Curve curve, ElementId typeId,
        ElementId levelId, double heightFeet, double baseOffset,
        WallFinishOptions options, WallFinishResult result, string roomName)
    {
        if (typeId == ElementId.InvalidElementId) return;

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
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Wall in '{roomName}': {ex.Message}");
        }
    }

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

            // Áp dụng cùng offset cho sàn
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

            // Gắn tên Room vào parameter
            if (options.AssignRoomName && !string.IsNullOrEmpty(options.RoomNameParam))
            {
                SetElementParam(floor, options.RoomNameParam, room.Name);
            }

            result.CreatedFloorIds.Add(floor.Id);
            result.FloorsCreated++;
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

        // Collect original bounding element IDs
        var originalIds = new HashSet<ElementId>();
        foreach (var loop in boundaries)
        {
            foreach (var seg in loop)
            {
                if (seg.ElementId != ElementId.InvalidElementId)
                    originalIds.Add(seg.ElementId);
            }
        }

        // Join finish walls with originals
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
                catch { /* Skip silently */ }
            }
        }
    }

    /// <summary>
    /// Ghi giá trị string vào parameter theo tên.
    /// </summary>
    private void SetElementParam(Element element, string paramName, string value)
    {
        var param = element.LookupParameter(paramName);
        if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
        {
            param.Set(value);
        }
    }
}
