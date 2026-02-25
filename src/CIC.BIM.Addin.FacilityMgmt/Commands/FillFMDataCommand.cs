using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.FacilityMgmt.Services;

namespace CIC.BIM.Addin.FacilityMgmt.Commands;

/// <summary>
/// Command: Điền dữ liệu FM
/// Automatically fills Location (from Room/Space), Category, AssetCode, Status, Condition
/// for all MEP equipment in the model.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class FillFMDataCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument.Document;

        try
        {
            // Collect all MEP elements from target categories
            var mepElements = CollectMEPElements(doc);

            if (mepElements.Count == 0)
            {
                TaskDialog.Show("CIC - Điền dữ liệu FM",
                    "⚠️ Không tìm thấy thiết bị MEP nào trong model.\n" +
                    "Hãy đảm bảo model có các thiết bị thuộc categories: " +
                    "Mechanical Equipment, Electrical Equipment, v.v.");
                return Result.Succeeded;
            }

            // Confirm with user
            var confirmDialog = new TaskDialog("CIC - Điền dữ liệu FM")
            {
                MainInstruction = $"Tìm thấy {mepElements.Count} thiết bị MEP",
                MainContent = "Sẽ tự động điền:\n" +
                    "• Location (từ Room/Space)\n" +
                    "• Category (phân loại FM)\n" +
                    "• AssetCode (mã tài sản)\n" +
                    "• Status = Active\n" +
                    "• Condition = Good\n" +
                    "• MaintenanceCycle = 180 ngày\n\n" +
                    "Chỉ điền vào các ô trống, không ghi đè dữ liệu đã có.",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
            };

            if (confirmDialog.Show() != TaskDialogResult.Ok)
                return Result.Cancelled;

            using var tx = new Transaction(doc, "CIC - Điền dữ liệu Vận hành");
            tx.Start();

            int filledCount = 0;
            int skippedCount = 0;
            var categoryCounters = new Dictionary<string, int>();

            foreach (var element in mepElements)
            {
                var filled = FillElementFMData(element, doc, categoryCounters);
                if (filled)
                    filledCount++;
                else
                    skippedCount++;
            }

            tx.Commit();

            // Report
            var report = $"✅ Hoàn tất điền dữ liệu FM!\n\n";
            report += $"📊 Kết quả:\n";
            report += $"  • Đã điền: {filledCount} thiết bị\n";
            report += $"  • Bỏ qua (đã có dữ liệu): {skippedCount} thiết bị\n\n";
            report += $"📋 Phân loại:\n";

            foreach (var kvp in categoryCounters.OrderByDescending(x => x.Value))
            {
                report += $"  • {kvp.Key}: {kvp.Value}\n";
            }

            report += "\n💡 Bước tiếp: Chạy 'Xuất báo cáo Vận hành' để export Excel.";

            TaskDialog.Show("CIC - Điền dữ liệu FM", report);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Lỗi", $"❌ Không thể điền dữ liệu:\n{ex.Message}");
            return Result.Failed;
        }
    }

    /// <summary>
    /// Fill FM data for a single element.
    /// Only fills empty parameters (does not overwrite existing values).
    /// Returns true if any data was filled.
    /// </summary>
    private bool FillElementFMData(Element element, Document doc, Dictionary<string, int> counters)
    {
        bool anyFilled = false;

        // Category
        var existingCategory = ParameterService.GetStringParam(element, "CIC_FM_Category");
        var fmCategory = CategoryMappingService.GetFMCategory(element);

        if (string.IsNullOrEmpty(existingCategory))
        {
            ParameterService.SetStringParam(element, "CIC_FM_Category", fmCategory);
            anyFilled = true;
        }
        else
        {
            fmCategory = existingCategory; // Use existing for counter
        }

        // Track category counts
        if (!counters.ContainsKey(fmCategory)) counters[fmCategory] = 0;
        counters[fmCategory]++;

        // Location (from Room/Space)
        if (string.IsNullOrEmpty(ParameterService.GetStringParam(element, "CIC_FM_Location")))
        {
            var location = LocationService.GetElementLocation(element, doc);
            if (!string.IsNullOrEmpty(location))
            {
                ParameterService.SetStringParam(element, "CIC_FM_Location", location);
                anyFilled = true;
            }
        }

        // AssetCode - auto-generate if empty
        if (string.IsNullOrEmpty(ParameterService.GetStringParam(element, "CIC_FM_AssetCode")))
        {
            var assetCode = GenerateAssetCode(element, doc, fmCategory, counters[fmCategory]);
            ParameterService.SetStringParam(element, "CIC_FM_AssetCode", assetCode);
            anyFilled = true;
        }

        // Status - default "Active"
        if (string.IsNullOrEmpty(ParameterService.GetStringParam(element, "CIC_FM_Status")))
        {
            ParameterService.SetStringParam(element, "CIC_FM_Status", "Active");
            anyFilled = true;
        }

        // Condition - default "Good"
        if (string.IsNullOrEmpty(ParameterService.GetStringParam(element, "CIC_FM_Condition")))
        {
            ParameterService.SetStringParam(element, "CIC_FM_Condition", "Good");
            anyFilled = true;
        }

        // MaintenanceCycle - default 180 days
        var existingCycle = ParameterService.GetIntParam(element, "CIC_FM_MaintenanceCycle");
        if (existingCycle == null || existingCycle == 0)
        {
            ParameterService.SetIntParam(element, "CIC_FM_MaintenanceCycle", 180);
            anyFilled = true;
        }

        return anyFilled;
    }

    /// <summary>
    /// Generate asset code in format: {CategoryPrefix}-{LevelCode}-{Number}
    /// Example: HVAC-T2-001
    /// </summary>
    private string GenerateAssetCode(Element element, Document doc, string fmCategory, int counter)
    {
        // Category prefix
        var prefix = fmCategory switch
        {
            "HVAC" => "HVAC",
            "Cơ điện" => "CD",
            "Cấp thoát nước" => "CTN",
            "PCCC" => "PCCC",
            "Điện chiếu sáng" => "DCS",
            "Thang máy" => "TM",
            "Hệ thống IT/Mạng" => "IT",
            "Camera/An ninh" => "AN",
            "Máy phát điện" => "MPD",
            _ => "TB" // Thiết bị chung
        };

        // Level code
        var levelCode = "XX";
        Level? level = null;

        if (element.LevelId != ElementId.InvalidElementId)
            level = doc.GetElement(element.LevelId) as Level;

        if (level == null)
        {
            var levelParam = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
            if (levelParam != null && levelParam.AsElementId() != ElementId.InvalidElementId)
                level = doc.GetElement(levelParam.AsElementId()) as Level;
        }

        if (level != null)
        {
            var levelName = level.Name;
            // Extract level number/code from name
            // Common patterns: "Tầng 1", "Level 1", "B1", "T1", "Floor 1"
            var match = System.Text.RegularExpressions.Regex.Match(levelName, @"(\d+)");
            if (match.Success)
            {
                var num = match.Value;
                if (levelName.Contains("B") || levelName.Contains("basement", StringComparison.OrdinalIgnoreCase)
                    || levelName.Contains("hầm", StringComparison.OrdinalIgnoreCase))
                    levelCode = $"B{num}";
                else
                    levelCode = $"T{num}";
            }
            else
            {
                levelCode = levelName.Length <= 4 ? levelName : levelName[..4];
            }
        }

        return $"{prefix}-{levelCode}-{counter:D3}";
    }

    /// <summary>
    /// Collect all elements from target MEP categories.
    /// </summary>
    private List<Element> CollectMEPElements(Document doc)
    {
        var result = new List<Element>();

        foreach (var builtInCat in FMParameters.TargetCategories)
        {
            try
            {
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(builtInCat)
                    .WhereElementIsNotElementType()
                    .ToElements();

                result.AddRange(collector);
            }
            catch { /* Category may not exist */ }
        }

        return result;
    }
}
