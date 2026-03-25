using System.Data;
using Autodesk.Revit.DB;

namespace CIC.BIM.Addin.FacilityMgmt.Services;

/// <summary>
/// Centralized service for collecting MEP elements and building preview DataTable.
/// Used by FMPreviewWindow to display the smart device list.
/// </summary>
public static class FMDataService
{
    /// <summary>
    /// Collect all elements from FM target categories.
    /// </summary>
    public static List<Element> CollectMEPElements(Document doc)
    {
        var result = new List<Element>();
        var addedIds = new HashSet<long>();

        foreach (var builtInCat in FMParameters.TargetCategories)
        {
            try
            {
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(builtInCat)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (var elem in collector)
                {
                    var id = GetElementIdValue(elem);
                    if (addedIds.Add(id))
                        result.Add(elem);
                }
            }
            catch { /* Category may not exist in model */ }
        }

        return result;
    }

    /// <summary>
    /// Build a DataTable for the preview DataGrid.
    /// Columns: ElementId (hidden), Tên thiết bị, Family, Type, Phân loại FM, Tầng, Vị trí,
    ///          Mã tài sản, Trạng thái, Tình trạng, Đã gán FM
    /// </summary>
    public static DataTable BuildPreviewTable(Document doc, List<Element> elements)
    {
        var dt = new DataTable("FMPreview");

        // Hidden ID column
        dt.Columns.Add("ElementId", typeof(long));

        // Display columns
        dt.Columns.Add("Tên thiết bị", typeof(string));
        dt.Columns.Add("Family", typeof(string));
        dt.Columns.Add("Type", typeof(string));
        dt.Columns.Add("Phân loại FM", typeof(string));
        dt.Columns.Add("Tầng", typeof(string));
        dt.Columns.Add("Vị trí", typeof(string));
        dt.Columns.Add("Mã tài sản", typeof(string));
        dt.Columns.Add("Trạng thái", typeof(string));
        dt.Columns.Add("Tình trạng", typeof(string));
        dt.Columns.Add("Đã gán FM", typeof(string));

        foreach (var elem in elements)
        {
            var row = dt.NewRow();

            row["ElementId"] = GetElementIdValue(elem);
            row["Tên thiết bị"] = elem.Name ?? "";
            row["Family"] = GetFamilyName(elem) ?? "";
            row["Type"] = GetTypeName(elem) ?? "";
            row["Phân loại FM"] = CategoryMappingService.GetFMCategory(elem);
            row["Tầng"] = GetLevelName(elem, doc) ?? "";
            row["Vị trí"] = LocationService.GetElementLocation(elem, doc) ?? "";

            // Current FM data (if any)
            var assetCode = ParameterService.GetStringParam(elem, "CIC_FM_AssetCode") ?? "";
            var status = ParameterService.GetStringParam(elem, "CIC_FM_Status") ?? "";
            var condition = ParameterService.GetStringParam(elem, "CIC_FM_Condition") ?? "";

            row["Mã tài sản"] = assetCode;
            row["Trạng thái"] = status;
            row["Tình trạng"] = condition;

            // Determine if FM params are already assigned
            var hasFM = !string.IsNullOrEmpty(assetCode) || !string.IsNullOrEmpty(status);
            row["Đã gán FM"] = hasFM ? "✅ Đã gán" : "⬜ Chưa gán";

            dt.Rows.Add(row);
        }

        return dt;
    }

    /// <summary>Get distinct FM categories from a DataTable.</summary>
    public static List<string> GetDistinctCategories(DataTable dt)
    {
        return dt.AsEnumerable()
            .Select(r => r.Field<string>("Phân loại FM") ?? "")
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();
    }

    /// <summary>Get distinct levels from a DataTable.</summary>
    public static List<string> GetDistinctLevels(DataTable dt)
    {
        return dt.AsEnumerable()
            .Select(r => r.Field<string>("Tầng") ?? "")
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();
    }

    #region Private Helpers

    private static long GetElementIdValue(Element elem)
    {
        try { return elem.Id.Value; }
        catch { return elem.Id.IntegerValue; }
    }

    private static string? GetFamilyName(Element element)
    {
        if (element is FamilyInstance fi)
            return fi.Symbol?.Family?.Name;
        return element.Category?.Name;
    }

    private static string? GetTypeName(Element element)
    {
        if (element is FamilyInstance fi)
            return fi.Symbol?.Name;
        return null;
    }

    private static string? GetLevelName(Element element, Document doc)
    {
        if (element.LevelId != ElementId.InvalidElementId)
        {
            var level = doc.GetElement(element.LevelId) as Level;
            if (level != null) return level.Name;
        }

        var levelParam = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
        if (levelParam != null && levelParam.AsElementId() != ElementId.InvalidElementId)
        {
            var level = doc.GetElement(levelParam.AsElementId()) as Level;
            if (level != null) return level.Name;
        }

        var startLevel = element.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
        if (startLevel != null && startLevel.AsElementId() != ElementId.InvalidElementId)
        {
            var level = doc.GetElement(startLevel.AsElementId()) as Level;
            if (level != null) return level.Name;
        }

        return null;
    }

    #endregion
}
