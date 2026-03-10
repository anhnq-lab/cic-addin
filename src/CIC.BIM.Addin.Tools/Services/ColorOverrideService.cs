using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Service tô màu đối tượng theo Category trong Active View
/// sử dụng OverrideGraphicSettings.
/// </summary>
public class ColorOverrideService
{
    /// <summary>
    /// Bảng màu mặc định — 20 màu dễ phân biệt.
    /// </summary>
    public static readonly Color[] DefaultPalette = new[]
    {
        new Color(231,  76,  60),  // Đỏ
        new Color( 46, 134, 193),  // Xanh dương
        new Color( 39, 174,  96),  // Xanh lá
        new Color(243, 156,  18),  // Cam
        new Color(142,  68, 173),  // Tím
        new Color( 26, 188, 156),  // Ngọc lam
        new Color(241, 196,  15),  // Vàng
        new Color(230, 126,  34),  // Cam đậm
        new Color( 52, 152, 219),  // Xanh nhạt
        new Color(211,  84,   0),  // Đỏ cam
        new Color( 22, 160, 133),  // Teal
        new Color(155,  89, 182),  // Lavender
        new Color( 41, 128, 185),  // Steel blue
        new Color(192,  57,  43),  // Crimson
        new Color( 44,  62,  80),  // Navy
        new Color(127, 140, 141),  // Xám
        new Color( 46, 204, 113),  // Emerald
        new Color(236, 112,  99),  // Salmon
        new Color( 93, 109, 126),  // Slate
        new Color(174, 214,  41),  // Lime
    };

    /// <summary>
    /// Thông tin một category cùng màu override.
    /// </summary>
    public class CategoryColorInfo
    {
        public ElementId CategoryId { get; set; } = ElementId.InvalidElementId;
        public string CategoryName { get; set; } = "";
        public Color Color { get; set; } = new Color(200, 200, 200);
        public bool IsEnabled { get; set; } = true;
        public int ElementCount { get; set; }
    }

    /// <summary>
    /// Kết quả sau khi apply override.
    /// </summary>
    public class ColorOverrideResult
    {
        public int CategoriesApplied { get; set; }
        public int ElementsColored { get; set; }
        public int Skipped { get; set; }
    }

    /// <summary>
    /// Quét Active View, trả về danh sách Category có element kèm màu mặc định.
    /// </summary>
    public static List<CategoryColorInfo> GetCategoriesInView(Document doc, View view)
    {
        var result = new Dictionary<int, CategoryColorInfo>();

        var elements = new FilteredElementCollector(doc, view.Id)
            .WhereElementIsNotElementType()
            .ToElements();

        int colorIndex = 0;
        foreach (var elem in elements)
        {
            var cat = elem.Category;
            if (cat == null) continue;
            // Bỏ qua category không có tên (internal)
            if (string.IsNullOrEmpty(cat.Name)) continue;

            int catId = cat.Id.IntegerValue;
            if (!result.ContainsKey(catId))
            {
                result[catId] = new CategoryColorInfo
                {
                    CategoryId = cat.Id,
                    CategoryName = cat.Name,
                    Color = DefaultPalette[colorIndex % DefaultPalette.Length],
                    IsEnabled = true,
                    ElementCount = 0
                };
                colorIndex++;
            }
            result[catId].ElementCount++;
        }

        return result.Values
            .OrderBy(c => c.CategoryName)
            .ToList();
    }

    /// <summary>
    /// Áp dụng override màu cho từng Category trong view.
    /// </summary>
    public static ColorOverrideResult ApplyColorOverrides(
        Document doc, View view, List<CategoryColorInfo> categories)
    {
        var result = new ColorOverrideResult();

        // Cache solid fill pattern ID
        var solidFill = GetSolidFillPatternId(doc);

        using var tx = new Transaction(doc, "CIC - Tô màu đối tượng");
        tx.Start();

        foreach (var info in categories)
        {
            if (!info.IsEnabled) continue;

            try
            {
                var ogs = new OverrideGraphicSettings();

                // Surface color (mặt phẳng)
                ogs.SetSurfaceForegroundPatternColor(info.Color);
                if (solidFill != ElementId.InvalidElementId)
                    ogs.SetSurfaceForegroundPatternId(solidFill);

                // Projection line (đường nét)
                ogs.SetProjectionLineColor(info.Color);

                // Cut color
                ogs.SetCutForegroundPatternColor(info.Color);
                if (solidFill != ElementId.InvalidElementId)
                    ogs.SetCutForegroundPatternId(solidFill);
                ogs.SetCutLineColor(info.Color);

                view.SetCategoryOverrides(info.CategoryId, ogs);

                result.CategoriesApplied++;
                result.ElementsColored += info.ElementCount;
            }
            catch
            {
                // Category không hỗ trợ override (Cameras, etc.) → bỏ qua
                result.Skipped++;
            }
        }

        tx.Commit();
        return result;
    }

    /// <summary>
    /// Xóa tất cả override trên view, trả về màu gốc.
    /// </summary>
    public static int ResetOverrides(Document doc, View view, List<CategoryColorInfo> categories)
    {
        int resetCount = 0;

        using var tx = new Transaction(doc, "CIC - Reset màu đối tượng");
        tx.Start();

        foreach (var info in categories)
        {
            try
            {
                view.SetCategoryOverrides(info.CategoryId, new OverrideGraphicSettings());
                resetCount++;
            }
            catch
            {
                // Category không hỗ trợ override → bỏ qua
            }
        }

        tx.Commit();
        return resetCount;
    }

    // ═══════════════════════════════════════════════════════════════
    //  MODE 2: BY PARAMETER VALUE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Thông tin một nhóm elements cùng giá trị parameter.
    /// </summary>
    public class ParameterValueColorInfo
    {
        public string ParameterValue { get; set; } = "";
        public Color Color { get; set; } = new Color(200, 200, 200);
        public bool IsEnabled { get; set; } = true;
        public int ElementCount { get; set; }
        public List<ElementId> ElementIds { get; set; } = new();
    }

    /// <summary>
    /// Lấy danh sách parameter names phổ biến từ elements trong view.
    /// </summary>
    public static List<string> GetParameterNamesInView(Document doc, View view)
    {
        var paramCounts = new Dictionary<string, int>();

        var elements = new FilteredElementCollector(doc, view.Id)
            .WhereElementIsNotElementType()
            .ToElements();

        foreach (var elem in elements)
        {
            if (elem.Category == null || string.IsNullOrEmpty(elem.Category.Name)) continue;

            foreach (Parameter p in elem.Parameters)
            {
                if (p.Definition == null) continue;
                var name = p.Definition.Name;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (p.IsReadOnly && p.StorageType == StorageType.None) continue;

                if (!paramCounts.ContainsKey(name)) paramCounts[name] = 0;
                paramCounts[name]++;
            }
        }

        return paramCounts
            .Where(kv => kv.Value >= 3)  // Chỉ hiện param có >= 3 elements
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Key)
            .ToList();
    }

    /// <summary>
    /// Group elements theo giá trị của 1 parameter, kèm màu mặc định.
    /// </summary>
    public static List<ParameterValueColorInfo> GetElementsByParameterValue(
        Document doc, View view, string parameterName)
    {
        var groups = new Dictionary<string, ParameterValueColorInfo>();
        int colorIndex = 0;

        var elements = new FilteredElementCollector(doc, view.Id)
            .WhereElementIsNotElementType()
            .ToElements();

        foreach (var elem in elements)
        {
            if (elem.Category == null || string.IsNullOrEmpty(elem.Category.Name)) continue;

            var param = elem.LookupParameter(parameterName);
            string val = "(Trống)";

            if (param != null)
            {
                var displayVal = param.AsValueString();
                if (string.IsNullOrWhiteSpace(displayVal))
                {
                    switch (param.StorageType)
                    {
                        case StorageType.String:
                            displayVal = param.AsString(); break;
                        case StorageType.Integer:
                            displayVal = param.AsInteger().ToString(); break;
                        case StorageType.Double:
                            displayVal = param.AsDouble().ToString("F2"); break;
                        case StorageType.ElementId:
                            var eId = param.AsElementId();
                            if (eId != ElementId.InvalidElementId)
                                displayVal = doc.GetElement(eId)?.Name ?? eId.ToString();
                            break;
                    }
                }
                if (!string.IsNullOrWhiteSpace(displayVal))
                    val = displayVal;
            }

            if (!groups.ContainsKey(val))
            {
                groups[val] = new ParameterValueColorInfo
                {
                    ParameterValue = val,
                    Color = DefaultPalette[colorIndex % DefaultPalette.Length],
                    IsEnabled = true
                };
                colorIndex++;
            }
            groups[val].ElementCount++;
            groups[val].ElementIds.Add(elem.Id);
        }

        return groups.Values.OrderBy(g => g.ParameterValue).ToList();
    }

    /// <summary>
    /// Áp dụng override màu cho từng element (theo parameter value).
    /// </summary>
    public static ColorOverrideResult ApplyParameterColorOverrides(
        Document doc, View view, List<ParameterValueColorInfo> groups)
    {
        var result = new ColorOverrideResult();
        var solidFill = GetSolidFillPatternId(doc);

        using var tx = new Transaction(doc, "CIC - Tô màu theo Parameter");
        tx.Start();

        foreach (var group in groups)
        {
            if (!group.IsEnabled) continue;

            var ogs = new OverrideGraphicSettings();
            ogs.SetSurfaceForegroundPatternColor(group.Color);
            if (solidFill != ElementId.InvalidElementId)
                ogs.SetSurfaceForegroundPatternId(solidFill);
            ogs.SetProjectionLineColor(group.Color);
            ogs.SetCutForegroundPatternColor(group.Color);
            if (solidFill != ElementId.InvalidElementId)
                ogs.SetCutForegroundPatternId(solidFill);
            ogs.SetCutLineColor(group.Color);

            foreach (var elemId in group.ElementIds)
            {
                try
                {
                    view.SetElementOverrides(elemId, ogs);
                    result.ElementsColored++;
                }
                catch { result.Skipped++; }
            }
            result.CategoriesApplied++;
        }

        tx.Commit();
        return result;
    }

    /// <summary>
    /// Reset element-level overrides.
    /// </summary>
    public static int ResetElementOverrides(Document doc, View view, List<ParameterValueColorInfo> groups)
    {
        int resetCount = 0;

        using var tx = new Transaction(doc, "CIC - Reset màu element");
        tx.Start();

        foreach (var group in groups)
        {
            foreach (var elemId in group.ElementIds)
            {
                try
                {
                    view.SetElementOverrides(elemId, new OverrideGraphicSettings());
                    resetCount++;
                }
                catch { }
            }
        }

        tx.Commit();
        return resetCount;
    }

    /// <summary>
    /// Lấy Solid Fill Pattern ID (dùng để tô bề mặt).
    /// </summary>
    private static ElementId GetSolidFillPatternId(Document doc)
    {
        var patterns = new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .ToList();

        // Tìm pattern Solid
        var solid = patterns.FirstOrDefault(p =>
            p.GetFillPattern().IsSolidFill);

        return solid?.Id ?? ElementId.InvalidElementId;
    }
}
