using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Core engine: tạo tường hoàn thiện (wall finish) theo boundary của Room.
/// Workflow: Room.GetBoundarySegments() → offset curve inward → Wall.Create()
/// </summary>
public class WallFinishService
{
    public class WallFinishOptions
    {
        public ElementId WallTypeId { get; set; } = ElementId.InvalidElementId;
        public double HeightMm { get; set; } = 0; // 0 = auto from Room
        public double BaseOffsetMm { get; set; } = 0;
        public bool JoinWithOriginal { get; set; } = true;
        public bool FlipWallFacing { get; set; } = false;
        
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
        // Get room boundary
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

        // Get level
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
                // Fallback: try room upper limit
                var upperLimit = room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET);
                heightFeet = upperLimit?.AsDouble() ?? 3000 * MmToFeet;
            }
        }

        double baseOffset = options.BaseOffsetMm * MmToFeet;

        // Create walls for each boundary loop
        using var tx = new Transaction(doc, $"Tường HT - {room.Name}");
        tx.Start();

        // Process outer + inner loops
        foreach (var loop in boundaries)
        {
            foreach (var segment in loop)
            {
                var curve = segment.GetCurve();
                if (curve == null || curve.ApproximateLength < 0.01) continue;

                try
                {
                    var wall = Wall.Create(
                        doc,
                        curve,
                        options.WallTypeId,
                        levelId,
                        heightFeet,
                        baseOffset,
                        false,   // flip
                        false    // structural
                    );

                    if (wall != null)
                    {
                        // Set wall as non-structural
                        var structParam = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
                        if (structParam != null && !structParam.IsReadOnly)
                            structParam.Set(0);

                        result.CreatedWallIds.Add(wall.Id);
                        result.WallsCreated++;
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Segment in '{room.Name}': {ex.Message}");
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

        // Join geometry with original walls
        if (options.JoinWithOriginal && result.CreatedWallIds.Count > 0)
        {
            JoinFinishWithOriginalWalls(doc, room, result);
        }

        tx.Commit();
    }

    private void CreateFloorFinish(Document doc, Room room, IList<IList<BoundarySegment>> boundaries,
        WallFinishOptions options, WallFinishResult result)
    {
        // Build CurveLoop from outer boundary (first loop)
        var outerLoop = boundaries[0];
        var curveLoop = new CurveLoop();
        foreach (var seg in outerLoop)
        {
            var c = seg.GetCurve();
            if (c != null) curveLoop.Append(c);
        }

        var curveLoops = new List<CurveLoop> { curveLoop };

        // Add inner loops (holes) if any
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

#if REVIT2024
        // Revit 2024 API
        var floor = Floor.Create(doc, curveLoops, options.FloorTypeId, levelId);
#else
        // Revit 2025+ API
        var floor = Floor.Create(doc, curveLoops, options.FloorTypeId, levelId);
#endif

        if (floor != null)
        {
            // Set offset
            var offsetParam = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
            if (offsetParam != null && !offsetParam.IsReadOnly)
                offsetParam.Set(options.FloorOffsetMm * MmToFeet);

            result.CreatedFloorIds.Add(floor.Id);
            result.FloorsCreated++;
        }
    }

    private void JoinFinishWithOriginalWalls(Document doc, Room room, WallFinishResult result)
    {
        // Collect original walls that bound this room
        var boundaryOpts = new SpatialElementBoundaryOptions();
        var boundaries = room.GetBoundarySegments(boundaryOpts);
        if (boundaries == null) return;

        var originalWallIds = new HashSet<ElementId>();
        foreach (var loop in boundaries)
        {
            foreach (var seg in loop)
            {
                var elem = doc.GetElement(seg.ElementId);
                if (elem is Wall) originalWallIds.Add(elem.Id);
            }
        }

        // Try to join each finish wall with nearby original walls
        foreach (var finishWallId in result.CreatedWallIds)
        {
            var finishWall = doc.GetElement(finishWallId);
            if (finishWall == null) continue;

            foreach (var origWallId in originalWallIds)
            {
                var origWall = doc.GetElement(origWallId);
                if (origWall == null) continue;

                try
                {
                    if (!JoinGeometryUtils.AreElementsJoined(doc, finishWall, origWall))
                    {
                        JoinGeometryUtils.JoinGeometry(doc, finishWall, origWall);
                    }
                }
                catch
                {
                    // Some joins may fail — that's OK, skip silently
                }
            }
        }
    }
}
