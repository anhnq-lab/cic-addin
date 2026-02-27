using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Trích xuất thông tin từ mô hình Revit → chuỗi context dạng text
/// để gửi kèm câu hỏi cho AI chatbot.
/// </summary>
public class ModelExtractorService
{
    private const double SqFeetToSqM = 0.092903;
    private const double CuFeetToCuM = 0.0283168;
    private const double FeetToMm = 304.8;

    /// <summary>
    /// Trích xuất toàn bộ thông tin mô hình thành chuỗi context.
    /// Context này sẽ được gửi kèm câu hỏi cho LLM.
    /// </summary>
    public static string ExtractModelContext(Document doc)
    {
        var sb = new StringBuilder();

        // ═══ Project Info ═══
        sb.AppendLine("=== THÔNG TIN DỰ ÁN ===");
        sb.AppendLine($"Tên dự án: {doc.Title}");
        var pInfo = doc.ProjectInformation;
        if (pInfo != null)
        {
            if (!string.IsNullOrEmpty(pInfo.BuildingName))
                sb.AppendLine($"Tên công trình: {pInfo.BuildingName}");
            if (!string.IsNullOrEmpty(pInfo.Address))
                sb.AppendLine($"Địa chỉ: {pInfo.Address}");
            if (!string.IsNullOrEmpty(pInfo.Number))
                sb.AppendLine($"Số dự án: {pInfo.Number}");
            if (!string.IsNullOrEmpty(pInfo.Status))
                sb.AppendLine($"Trạng thái: {pInfo.Status}");
        }
        sb.AppendLine();

        // ═══ Levels ═══
        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .ToList();

        sb.AppendLine("=== DANH SÁCH TẦNG ===");
        foreach (var lvl in levels)
        {
            var elevMm = Math.Round(lvl.Elevation * FeetToMm, 0);
            sb.AppendLine($"- {lvl.Name}: cao độ {elevMm} mm");
        }
        sb.AppendLine($"Tổng: {levels.Count} tầng");
        sb.AppendLine();

        // ═══ Element Count by Category ═══
        sb.AppendLine("=== THỐNG KÊ CẤU KIỆN ===");
        var categoryStats = new Dictionary<string, CategoryStat>();

        foreach (var catInfo in ParamManagerService.AllCategories)
        {
            var collected = new FilteredElementCollector(doc)
                .OfCategory(catInfo.Category)
                .WhereElementIsNotElementType()
                .ToList();

            // Lọc structural cho Tường/Sàn
            if (catInfo.StructuralOnly)
                collected = collected.Where(e => IsStructural(e)).ToList();
            else if (catInfo.Discipline == ParamManagerService.Discipline.KienTruc &&
                     (catInfo.Category == BuiltInCategory.OST_Walls ||
                      catInfo.Category == BuiltInCategory.OST_Floors))
                collected = collected.Where(e => !IsStructural(e)).ToList();

            if (collected.Count == 0) continue;

            var key = catInfo.DisplayName;
            if (categoryStats.ContainsKey(key))
            {
                categoryStats[key].Count += collected.Count;
                categoryStats[key].Elements.AddRange(collected);
            }
            else
            {
                categoryStats[key] = new CategoryStat
                {
                    DisplayName = key,
                    Discipline = ParamManagerService.GetDisciplineName(catInfo.Discipline),
                    Count = collected.Count,
                    Elements = collected
                };
            }
        }

        int totalElements = 0;
        foreach (var stat in categoryStats.Values.OrderByDescending(s => s.Count))
        {
            sb.AppendLine($"- {stat.DisplayName} ({stat.Discipline}): {stat.Count} cấu kiện");
            totalElements += stat.Count;
        }
        sb.AppendLine($"Tổng cộng: {totalElements} cấu kiện");
        sb.AppendLine();

        // ═══ Element Details by Level ═══
        sb.AppendLine("=== CHI TIẾT THEO TẦNG ===");
        foreach (var lvl in levels)
        {
            var elementsOnLevel = new Dictionary<string, int>();
            double totalAreaOnLevel = 0;
            double totalVolumeOnLevel = 0;

            foreach (var stat in categoryStats.Values)
            {
                var onLevel = stat.Elements.Where(e => GetLevelId(e) == lvl.Id).ToList();
                if (onLevel.Count == 0) continue;
                elementsOnLevel[stat.DisplayName] = onLevel.Count;

                foreach (var elem in onLevel)
                {
                    totalAreaOnLevel += GetArea(elem);
                    totalVolumeOnLevel += GetVolume(elem);
                }
            }

            if (elementsOnLevel.Count == 0) continue;

            sb.AppendLine($"\n[{lvl.Name}]");
            foreach (var kvp in elementsOnLevel.OrderByDescending(x => x.Value))
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
            if (totalAreaOnLevel > 0)
                sb.AppendLine($"  Tổng diện tích: {totalAreaOnLevel:N2} m²");
            if (totalVolumeOnLevel > 0)
                sb.AppendLine($"  Tổng thể tích: {totalVolumeOnLevel:N2} m³");
        }
        sb.AppendLine();

        // ═══ Rooms ═══
        var rooms = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .Where(r => r.Area > 0)
            .OrderBy(r => r.Level?.Name ?? "")
            .ThenBy(r => r.Name)
            .ToList();

        if (rooms.Count > 0)
        {
            sb.AppendLine("=== DANH SÁCH PHÒNG ===");
            foreach (var room in rooms)
            {
                var areaSqM = room.Area * SqFeetToSqM;
                var levelName = room.Level?.Name ?? "N/A";
                sb.AppendLine($"- {room.Number} {room.Name} | Tầng: {levelName} | DT: {areaSqM:N2} m²");
            }
            sb.AppendLine($"Tổng: {rooms.Count} phòng");
            sb.AppendLine();
        }

        // ═══ Type Statistics ═══
        sb.AppendLine("=== LOẠI CẤU KIỆN (TYPES) ===");
        foreach (var stat in categoryStats.Values.Where(s => s.Count > 0).OrderByDescending(s => s.Count))
        {
            var typeGroups = stat.Elements
                .GroupBy(e => GetTypeName(e))
                .OrderByDescending(g => g.Count())
                .Take(10);

            sb.AppendLine($"\n[{stat.DisplayName}]");
            foreach (var group in typeGroups)
            {
                sb.AppendLine($"  {group.Key}: {group.Count()} cấu kiện");
            }
        }
        sb.AppendLine();

        // ═══ Shared Parameters Summary ═══
        var sharedParams = DiscoverAllSharedParams(categoryStats);
        if (sharedParams.Count > 0)
        {
            sb.AppendLine("=== SHARED PARAMETERS ===");
            foreach (var param in sharedParams.Take(30))
            {
                sb.AppendLine($"- {param}");
            }
            sb.AppendLine();
        }

        // ═══ Materials ═══
        var materials = new HashSet<string>();
        foreach (var stat in categoryStats.Values)
        {
            foreach (var elem in stat.Elements.Take(50))
            {
                var matParam = elem.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
                if (matParam != null)
                {
                    var matName = matParam.AsValueString();
                    if (!string.IsNullOrEmpty(matName))
                        materials.Add(matName);
                }
            }
        }

        if (materials.Count > 0)
        {
            sb.AppendLine("=== VẬT LIỆU ===");
            foreach (var mat in materials.OrderBy(m => m))
            {
                sb.AppendLine($"- {mat}");
            }
            sb.AppendLine();
        }

        // ═══ Formwork data (if available) ═══
        var formworkData = ExtractFormworkData(categoryStats);
        if (!string.IsNullOrEmpty(formworkData))
        {
            sb.AppendLine("=== DỮ LIỆU VÁN KHUÔN ===");
            sb.AppendLine(formworkData);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Trích xuất nhanh (lightweight) — chỉ summary, không chi tiết.
    /// Dùng cho context refreshing.
    /// </summary>
    public static string ExtractQuickSummary(Document doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Dự án: {doc.Title}");

        var catCounts = new Dictionary<string, int>();
        foreach (var catInfo in ParamManagerService.AllCategories)
        {
            var count = new FilteredElementCollector(doc)
                .OfCategory(catInfo.Category)
                .WhereElementIsNotElementType()
                .GetElementCount();

            if (count > 0)
            {
                var key = catInfo.DisplayName;
                if (catCounts.ContainsKey(key))
                    catCounts[key] += count;
                else
                    catCounts[key] = count;
            }
        }

        foreach (var kvp in catCounts.OrderByDescending(x => x.Value))
            sb.AppendLine($"- {kvp.Key}: {kvp.Value}");

        return sb.ToString();
    }

    #region Private Helpers

    private static bool IsStructural(Element e)
    {
        var p = e.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)
             ?? e.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
        return p?.AsInteger() == 1;
    }

    private static ElementId GetLevelId(Element elem)
    {
        var bips = new[]
        {
            BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
            BuiltInParameter.FAMILY_LEVEL_PARAM,
            BuiltInParameter.SCHEDULE_LEVEL_PARAM,
            BuiltInParameter.WALL_BASE_CONSTRAINT
        };

        foreach (var bip in bips)
        {
            var p = elem.get_Parameter(bip);
            if (p != null)
            {
                var lvlId = p.AsElementId();
                if (lvlId != null && lvlId != ElementId.InvalidElementId)
                    return lvlId;
            }
        }

        if (elem is Wall wall && wall.LevelId != ElementId.InvalidElementId)
            return wall.LevelId;

        return ElementId.InvalidElementId;
    }

    private static double GetArea(Element elem)
    {
        var p = elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
        if (p != null && p.HasValue)
            return p.AsDouble() * SqFeetToSqM;
        return 0;
    }

    private static double GetVolume(Element elem)
    {
        var p = elem.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
        if (p != null && p.HasValue)
            return p.AsDouble() * CuFeetToCuM;
        return 0;
    }

    private static string GetTypeName(Element elem)
    {
        if (elem is FamilyInstance fi)
            return fi.Symbol?.FamilyName + " : " + fi.Name;
        return elem.Name;
    }

    private static List<string> DiscoverAllSharedParams(Dictionary<string, CategoryStat> stats)
    {
        var paramNames = new HashSet<string>();
        foreach (var stat in stats.Values)
        {
            foreach (var elem in stat.Elements.Take(5))
            {
                foreach (Parameter p in elem.Parameters)
                {
                    if (p.IsShared)
                        paramNames.Add(p.Definition.Name);
                }
            }
        }

        return paramNames
            .OrderByDescending(n => n.StartsWith("CIC_"))
            .ThenBy(n => n)
            .ToList();
    }

    private static string ExtractFormworkData(Dictionary<string, CategoryStat> stats)
    {
        var sb = new StringBuilder();
        var structuralKeys = new[] { "Dầm", "Cột", "Tường KC", "Sàn KC", "Móng" };

        foreach (var key in structuralKeys)
        {
            if (!stats.ContainsKey(key)) continue;
            var elements = stats[key].Elements;

            double totalFormwork = 0;
            int countWithFormwork = 0;

            foreach (var elem in elements)
            {
                var fwParam = elem.LookupParameter("CIC_FormworkArea");
                if (fwParam != null && fwParam.HasValue)
                {
                    var val = fwParam.StorageType == StorageType.Double
                        ? fwParam.AsDouble()
                        : double.TryParse(fwParam.AsString(), out var d) ? d : 0;
                    if (val > 0)
                    {
                        totalFormwork += val;
                        countWithFormwork++;
                    }
                }
            }

            if (countWithFormwork > 0)
            {
                sb.AppendLine($"- {key}: {countWithFormwork} cấu kiện có VK, tổng DT ván khuôn: {totalFormwork:N2} m²");
            }
        }

        return sb.ToString();
    }

    private class CategoryStat
    {
        public string DisplayName { get; set; } = "";
        public string Discipline { get; set; } = "";
        public int Count { get; set; }
        public List<Element> Elements { get; set; } = new();
    }

    #endregion
}
