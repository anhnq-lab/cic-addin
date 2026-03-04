using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Tạo Room tự động trên Level chỉ định.
/// Bật Room Bounding cho link instances & cột → Place Rooms.
/// </summary>
public class AutoRoomService
{
    public class AutoRoomOptions
    {
        public ElementId LevelId { get; set; } = ElementId.InvalidElementId;
        public bool AutoRoomBounding { get; set; } = true;
        public bool UseUpperLevel { get; set; } = true;
        public double OffsetMm { get; set; } = 0;
    }

    public class AutoRoomResult
    {
        public int RoomsCreated { get; set; }
        public int LinksSet { get; set; }
        public int ColumnsSet { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<ElementId> CreatedRoomIds { get; set; } = new();
    }

    private const double MmToFeet = 1.0 / 304.8;

    public AutoRoomResult Execute(Document doc, AutoRoomOptions options)
    {
        var result = new AutoRoomResult();

        var level = doc.GetElement(options.LevelId) as Level;
        if (level == null)
        {
            result.Warnings.Add("Không tìm thấy Level đã chọn.");
            return result;
        }

        var phase = GetLatestPhase(doc);
        if (phase == null)
        {
            result.Warnings.Add("Không tìm thấy Phase trong dự án.");
            return result;
        }

        // ═══════════════════════════════════════════════════════════
        // BƯỚC 1: Bật Room Bounding — TransactionGroup RIÊNG
        // Phải assimilate TRƯỚC khi lấy PlanTopology
        // ═══════════════════════════════════════════════════════════
        if (options.AutoRoomBounding)
        {
            using var tgBounding = new TransactionGroup(doc, "Bật Room Bounding");
            tgBounding.Start();
            EnableRoomBounding(doc, result);
            tgBounding.Assimilate();
            // ▸ Sau khi Assimilate, Revit đã cập nhật boundaries từ Links
        }

        // ═══════════════════════════════════════════════════════════
        // BƯỚC 2: Tạo Room — TransactionGroup RIÊNG
        // PlanTopology giờ đã tính boundary từ Links
        // ═══════════════════════════════════════════════════════════
        Level? upperLevel = null;
        if (options.UseUpperLevel)
        {
            var allLevels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            var currentIdx = allLevels.FindIndex(l => l.Id == level.Id);
            if (currentIdx >= 0 && currentIdx < allLevels.Count - 1)
                upperLevel = allLevels[currentIdx + 1];
        }

        using var tgRooms = new TransactionGroup(doc, "Tạo Room tự động");
        tgRooms.Start();

        // Multi-pass: mỗi pass tạo Room → commit → regenerate → tìm thêm circuits
        int maxPasses = 3;
        for (int pass = 0; pass < maxPasses; pass++)
        {
            int roomsThisPass = 0;

            using var tx = new Transaction(doc, $"Tạo Room - Pass {pass + 1}");
            tx.Start();

            try
            {
                var planTopology = doc.get_PlanTopology(level, phase);
                if (planTopology == null)
                {
                    if (pass == 0)
                        result.Warnings.Add($"Level '{level.Name}': không có PlanTopology.");
                    tx.RollBack();
                    break;
                }

                foreach (PlanCircuit circuit in planTopology.Circuits)
                {
                    if (circuit.IsRoomLocated) continue;

                    try
                    {
                        var room = doc.Create.NewRoom(null, circuit);
                        if (room != null)
                        {
                            if (upperLevel != null)
                            {
                                var upperParam = room.get_Parameter(BuiltInParameter.ROOM_UPPER_LEVEL);
                                if (upperParam != null && !upperParam.IsReadOnly)
                                    upperParam.Set(upperLevel.Id);
                            }

                            if (options.OffsetMm != 0)
                            {
                                var offsetParam = room.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET);
                                if (offsetParam != null && !offsetParam.IsReadOnly)
                                    offsetParam.Set(options.OffsetMm * MmToFeet);
                            }

                            result.CreatedRoomIds.Add(room.Id);
                            result.RoomsCreated++;
                            roomsThisPass++;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        result.Warnings.Add($"Circuit: {ex.Message}");
                    }
                }

                tx.Commit();
            }
            catch (System.Exception ex)
            {
                result.Warnings.Add($"Pass {pass + 1}: {ex.Message}");
                if (tx.HasStarted())
                    tx.RollBack();
            }

            // Nếu pass này không tạo được Room mới nào → dừng
            if (roomsThisPass == 0)
                break;
        }

        // Xóa Room có diện tích = 0 (redundant/not enclosed)
        CleanupZeroAreaRooms(doc, result);

        tgRooms.Assimilate();
        return result;
    }

    /// <summary>Xóa Room có Area=0 (vùng không khép kín)</summary>
    private void CleanupZeroAreaRooms(Document doc, AutoRoomResult result)
    {
        using var tx = new Transaction(doc, "Xóa Room lỗi");
        tx.Start();

        int deleted = 0;
        foreach (var roomId in result.CreatedRoomIds.ToList())
        {
            var room = doc.GetElement(roomId) as Room;
            if (room != null && room.Area <= 0)
            {
                try
                {
                    doc.Delete(roomId);
                    result.CreatedRoomIds.Remove(roomId);
                    result.RoomsCreated--;
                    deleted++;
                }
                catch { }
            }
        }

        if (deleted > 0)
            result.Warnings.Add($"Đã xóa {deleted} Room không hợp lệ (diện tích = 0).");

        tx.Commit();
    }

    private void EnableRoomBounding(Document doc, AutoRoomResult result)
    {
        using var tx = new Transaction(doc, "Bật Room Bounding");
        tx.Start();

        int totalSet = 0;

        // Links — quan trọng nhất: link kết cấu chứa tường/cột/dầm
        var links = new FilteredElementCollector(doc)
            .OfClass(typeof(RevitLinkInstance))
            .ToList();

        foreach (var link in links)
        {
            var rbParam = link.LookupParameter("Room Bounding");
            if (rbParam != null && !rbParam.IsReadOnly && rbParam.AsInteger() == 0)
            {
                rbParam.Set(1);
                result.LinksSet++;
            }
        }

        // Columns (Structural + Architectural)
        var columns = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .WherePasses(new LogicalOrFilter(
                new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns),
                new ElementCategoryFilter(BuiltInCategory.OST_Columns)))
            .ToList();

        foreach (var col in columns)
        {
            if (SetRoomBounding(col))
                result.ColumnsSet++;
        }

        // Structural Walls — tường kết cấu có thể chưa bật Room Bounding
        var structWalls = new FilteredElementCollector(doc)
            .OfClass(typeof(Wall))
            .WhereElementIsNotElementType()
            .Cast<Wall>()
            .Where(w => w.StructuralUsage != Autodesk.Revit.DB.Structure.StructuralWallUsage.NonBearing
                      || w.WallType.Kind == WallKind.Basic)
            .ToList();

        foreach (var wall in structWalls)
        {
            if (SetRoomBounding(wall))
                totalSet++;
        }

        if (totalSet > 0)
            result.Warnings.Add($"Đã bật Room Bounding cho thêm {totalSet} tường kết cấu.");

        tx.Commit();
    }

    private static bool SetRoomBounding(Element elem)
    {
        var rbParam = elem.LookupParameter("Room Bounding");
        if (rbParam != null && !rbParam.IsReadOnly && rbParam.AsInteger() == 0)
        {
            rbParam.Set(1);
            return true;
        }
        return false;
    }

    private Phase? GetLatestPhase(Document doc)
    {
        var phases = doc.Phases;
        if (phases == null || phases.Size == 0) return null;
        return phases.get_Item(phases.Size - 1) as Phase;
    }
}
