using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using CIC.BIM.Addin.FacilityMgmt.Services;

namespace CIC.BIM.Addin.FacilityMgmt.Commands;

/// <summary>
/// Command: Xuất báo cáo FM
/// Exports all MEP elements with FM parameters to an Excel file.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public class ExportFMReportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument.Document;

        try
        {
            // Collect MEP elements
            var mepElements = CollectMEPElements(doc);

            if (mepElements.Count == 0)
            {
                TaskDialog.Show("CIC Tool - Xuất báo cáo",
                    "⚠️ Không tìm thấy thiết bị MEP nào trong model.");
                return Result.Succeeded;
            }

            // Ask for save location using WinForms SaveFileDialog (more reliable in Revit)
            string? savePath = null;
            using (var saveDialog = new System.Windows.Forms.SaveFileDialog())
            {
                saveDialog.Title = "Lưu báo cáo thiết bị vận hành";
                saveDialog.Filter = "Excel Files (*.xlsx)|*.xlsx";
                saveDialog.FileName = $"FM_Report_{SanitizeFileName(doc.Title)}_{DateTime.Now:yyyyMMdd}";
                saveDialog.DefaultExt = ".xlsx";
                saveDialog.OverwritePrompt = true;

                if (saveDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                savePath = saveDialog.FileName;
            }

            if (string.IsNullOrEmpty(savePath)) return Result.Cancelled;

            // Generate Excel
            try
            {
                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add("Danh sách Thiết bị");

                // Headers
                var headers = new[]
                {
                    "STT", "Mã tài sản", "Tên thiết bị", "Family", "Type",
                    "Phân loại FM", "Vị trí", "Tầng",
                    "Nhà sản xuất", "Model",
                    "Chu kỳ bảo trì (ngày)", "Trạng thái", "Tình trạng",
                    "Revit Element ID"
                };

                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = ws.Cell(1, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E79");
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                }

                // Data rows
                int row = 2;
                int stt = 1;

                foreach (var element in mepElements)
                {
                    ws.Cell(row, 1).Value = stt++;
                    ws.Cell(row, 2).Value = ParameterService.GetStringParam(element, "CIC_FM_AssetCode") ?? "";
                    ws.Cell(row, 3).Value = element.Name ?? "";
                    ws.Cell(row, 4).Value = GetFamilyName(element) ?? "";
                    ws.Cell(row, 5).Value = GetTypeName(element) ?? "";
                    ws.Cell(row, 6).Value = ParameterService.GetStringParam(element, "CIC_FM_Category") ?? "";
                    ws.Cell(row, 7).Value = ParameterService.GetStringParam(element, "CIC_FM_Location") ?? "";
                    ws.Cell(row, 8).Value = GetLevelName(element, doc) ?? "";
                    ws.Cell(row, 9).Value = ParameterService.GetStringParam(element, "CIC_FM_Manufacturer") ?? "";
                    ws.Cell(row, 10).Value = ParameterService.GetStringParam(element, "CIC_FM_Model") ?? "";

                    var cycle = ParameterService.GetIntParam(element, "CIC_FM_MaintenanceCycle");
                    ws.Cell(row, 11).Value = cycle ?? 0;

                    ws.Cell(row, 12).Value = ParameterService.GetStringParam(element, "CIC_FM_Status") ?? "";
                    ws.Cell(row, 13).Value = ParameterService.GetStringParam(element, "CIC_FM_Condition") ?? "";
                    ws.Cell(row, 14).Value = element.Id.Value;

                    // Alternate row color
                    if (row % 2 == 0)
                    {
                        ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor =
                            XLColor.FromHtml("#F2F7FC");
                    }

                    row++;
                }

                // Auto-fit columns
                ws.Columns().AdjustToContents();

                // Summary sheet
                var summaryWs = workbook.Worksheets.Add("Tổng hợp");
                AddSummarySheet(summaryWs, mepElements, doc);

                // Save
                workbook.SaveAs(savePath);
            }
            catch (Exception exExcel)
            {
                TaskDialog.Show("CIC Tool - Lỗi",
                    $"❌ Lỗi khi tạo file Excel:\n{exExcel.GetType().Name}: {exExcel.Message}\n\n" +
                    $"Stack: {exExcel.StackTrace?.Substring(0, Math.Min(500, exExcel.StackTrace?.Length ?? 0))}");
                return Result.Failed;
            }

            TaskDialog.Show("CIC Tool - Xuất báo cáo",
                $"✅ Đã xuất thành công!\n\n" +
                $"📊 {mepElements.Count} thiết bị\n" +
                $"📁 {savePath}");

            // Open file
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = savePath,
                    UseShellExecute = true
                });
            }
            catch { /* Can't open file, not critical */ }

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("CIC Tool - Lỗi",
                $"❌ Không thể xuất báo cáo:\n{ex.GetType().Name}: {ex.Message}\n\n" +
                $"Stack: {ex.StackTrace?.Substring(0, Math.Min(500, ex.StackTrace?.Length ?? 0))}");
            return Result.Failed;
        }
    }

    private void AddSummarySheet(IXLWorksheet ws, List<Element> elements, Document doc)
    {
        ws.Cell(1, 1).Value = "TỔNG HỢP THIẾT BỊ VẬN HÀNH";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        ws.Cell(2, 1).Value = $"Dự án: {doc.Title}";
        ws.Cell(3, 1).Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}";
        ws.Cell(4, 1).Value = $"Tổng số thiết bị: {elements.Count}";

        ws.Cell(6, 1).Value = "Phân loại";
        ws.Cell(6, 2).Value = "Số lượng";
        ws.Cell(6, 1).Style.Font.Bold = true;
        ws.Cell(6, 2).Style.Font.Bold = true;

        var categoryCounts = new Dictionary<string, int>();
        foreach (var elem in elements)
        {
            var cat = ParameterService.GetStringParam(elem, "CIC_FM_Category") ?? "Chưa phân loại";
            if (!categoryCounts.ContainsKey(cat)) categoryCounts[cat] = 0;
            categoryCounts[cat]++;
        }

        int summaryRow = 7;
        foreach (var kvp in categoryCounts.OrderByDescending(x => x.Value))
        {
            ws.Cell(summaryRow, 1).Value = kvp.Key;
            ws.Cell(summaryRow, 2).Value = kvp.Value;
            summaryRow++;
        }

        ws.Columns().AdjustToContents();
    }

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
            catch { }
        }
        return result;
    }

    private string? GetFamilyName(Element element)
    {
        if (element is FamilyInstance fi)
            return fi.Symbol?.Family?.Name;
        return element.Category?.Name;
    }

    private string? GetTypeName(Element element)
    {
        if (element is FamilyInstance fi)
            return fi.Symbol?.Name;
        return null;
    }

    private string? GetLevelName(Element element, Document doc)
    {
        if (element.LevelId != ElementId.InvalidElementId)
        {
            var level = doc.GetElement(element.LevelId) as Level;
            return level?.Name;
        }

        var levelParam = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
        if (levelParam != null && levelParam.AsElementId() != ElementId.InvalidElementId)
        {
            var level = doc.GetElement(levelParam.AsElementId()) as Level;
            return level?.Name;
        }

        return null;
    }

    /// <summary>Remove invalid filename characters.</summary>
    private string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }
}
