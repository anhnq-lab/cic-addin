using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CIC.BIM.Addin.Tools.Services;
using CIC.BIM.Addin.Tools.Views;

namespace CIC.BIM.Addin.Tools.Commands;

[Transaction(TransactionMode.Manual)]
public class FormworkCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        // Step 1: Mở window — tất cả xử lý trong window (tính, tạo VK, ghi param)
        var window = new FormworkWindow(doc);
        window.ShowDialog();

        // ⚠ CRITICAL: Nếu đã có thay đổi (tạo/xóa VK) → PHẢI trả Succeeded
        // Nếu trả Cancelled → Revit sẽ undo tất cả transaction!
        if (window.HasChanges)
        {
            // Có thay đổi → export nếu cần, luôn trả Succeeded
            if (window.ExportRequested && window.Result != null && window.Result.Items.Count > 0)
            {
                ExportResults(doc, window);
            }
            return Result.Succeeded;
        }

        // Không có thay đổi → xử lý theo DialogResult
        if (window.DialogResult != true)
            return Result.Cancelled;

        // User chọn "Chọn cấu kiện" → pick elements → tính lại
        if (window.SelectElements)
        {
            var pickedElements = PickStructuralElements(uiDoc, doc, window.BuildOptions());
            if (pickedElements.Count == 0)
            {
                TaskDialog.Show("Thống kê Ván khuôn", "Chưa chọn cấu kiện nào.");
                return Result.Cancelled;
            }

            // Mở lại window với elements đã chọn
            var window2 = new FormworkWindow(doc, pickedElements);
            window2.ShowDialog();

            // Kiểm tra lại HasChanges cho window2
            if (window2.HasChanges)
            {
                if (window2.ExportRequested && window2.Result != null && window2.Result.Items.Count > 0)
                    ExportResults(doc, window2);
                return Result.Succeeded;
            }

            if (window2.DialogResult != true || !window2.ExportRequested)
                return Result.Cancelled;

            window = window2;
        }

        // Export kết quả
        if (window.Result == null || window.Result.Items.Count == 0)
        {
            TaskDialog.Show("Thống kê Ván khuôn", "Không có kết quả để xuất.");
            return Result.Succeeded; // Không Cancelled để tránh undo
        }

        ExportResults(doc, window);
        return Result.Succeeded;
    }

    /// <summary>
    /// Xuất kết quả ra Excel + Schedule.
    /// </summary>
    private static void ExportResults(Document doc, FormworkWindow window)
    {
        var result = window.Result!;
        var messages = new List<string>();

        // Ghi Shared Parameter
        if (window.SaveSharedParam)
        {
            try
            {
                var count = FormworkExportService.WriteSharedParameter(doc, result);
                messages.Add($"💾 Ghi CIC_FormworkArea: {count} cấu kiện");
            }
            catch (System.Exception ex)
            {
                messages.Add($"⚠️ Lỗi ghi Shared Param: {ex.Message}");
            }
        }

        // Xuất Excel
        if (window.OutputExcel)
        {
            try
            {
                var projectName = doc.Title ?? "Project";
                var filePath = FormworkExportService.ExportToExcel(result, projectName);

                if (string.IsNullOrEmpty(filePath))
                {
                    messages.Add("📊 Excel: Đã hủy chọn vị trí lưu");
                }
                else
                {
                    messages.Add($"📊 Excel: {filePath}");

                    // Tự mở file Excel
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = filePath,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }
            catch (System.Exception ex)
            {
                messages.Add($"⚠️ Lỗi xuất Excel: {ex.Message}");
            }
        }

        // Tạo Revit Schedule
        if (window.OutputSchedule)
        {
            try
            {
                var scheduleName = $"B3.2 Ván khuôn - {System.DateTime.Now:dd.MM.yyyy HH.mm}";
                var schedule = FormworkExportService.CreateRevitSchedule(doc, scheduleName);
                if (schedule != null)
                    messages.Add($"📄 Schedule: {schedule.Name}");
                else
                    messages.Add("⚠️ Không tạo được Schedule");
            }
            catch (System.Exception ex)
            {
                messages.Add($"⚠️ Lỗi tạo Schedule: {ex.Message}");
            }
        }

        // Summary
        var summary = $"✅ Hoàn tất thống kê ván khuôn B3.2\n\n" +
                      $"📐 Cấu kiện: {result.ElementCount}\n" +
                      $"📏 Diện tích thô: {result.TotalGrossArea:N2} m²\n" +
                      $"✂️ Trừ giao nhau: {result.TotalDeduction:N2} m²\n" +
                      $"📊 Diện tích ròng: {result.TotalNetArea:N2} m²\n\n" +
                      string.Join("\n", messages);

        TaskDialog.Show("Thống kê Ván khuôn — B3.2", summary);
    }

    /// <summary>
    /// Cho user chọn cấu kiện kết cấu.
    /// </summary>
    private static List<Element> PickStructuralElements(UIDocument uiDoc, Document doc, FormworkOptions options)
    {
        var elements = new List<Element>();

        try
        {
            TaskDialog.Show("Chọn cấu kiện",
                "Quét chọn các cấu kiện kết cấu cần tính ván khuôn.\n" +
                "Nhấn Finish (hoặc Enter) để xác nhận.");

            var refs = uiDoc.Selection.PickObjects(
                ObjectType.Element,
                "Chọn cấu kiện kết cấu (Dầm, Cột, Tường, Sàn, Móng)");

            var allowedCats = new HashSet<int>();
            if (options.IncludeBeam) allowedCats.Add((int)BuiltInCategory.OST_StructuralFraming);
            if (options.IncludeColumn) allowedCats.Add((int)BuiltInCategory.OST_StructuralColumns);
            if (options.IncludeWall) allowedCats.Add((int)BuiltInCategory.OST_Walls);
            if (options.IncludeFloor) allowedCats.Add((int)BuiltInCategory.OST_Floors);
            if (options.IncludeFoundation) allowedCats.Add((int)BuiltInCategory.OST_StructuralFoundation);

            foreach (var r in refs)
            {
                var elem = doc.GetElement(r.ElementId);
                if (elem?.Category != null && allowedCats.Contains(elem.Category.Id.IntegerValue))
                {
                    elements.Add(elem);
                }
            }
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            // User cancelled picking
        }

        return elements;
    }
}
