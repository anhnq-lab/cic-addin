using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using ClosedXML.Excel;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Xuất kết quả ván khuôn ra Excel và/hoặc Revit Schedule.
/// Ghi Shared Parameter CIC_FormworkArea lên cấu kiện.
/// </summary>
public class FormworkExportService
{
    private const string SharedParamName = "CIC_FormworkArea";
    private const string SharedParamGroup = "CIC Parameters";
    private const double SqMToSqFt = 10.7639;

    #region Shared Parameter

    /// <summary>
    /// Tạo Shared Parameter CIC_FormworkArea (nếu chưa có) và ghi giá trị lên elements.
    /// </summary>
    public static int WriteSharedParameter(Document doc, FormworkResult result)
    {
        int count = 0;

        using var tx = new Transaction(doc, "Ghi CIC_FormworkArea");
        tx.Start();

        // Đảm bảo Shared Parameter tồn tại
        EnsureSharedParameter(doc);

        foreach (var item in result.Items)
        {
            var elem = doc.GetElement(item.Id);
            if (elem == null) continue;

            var param = elem.LookupParameter(SharedParamName);
            if (param != null && !param.IsReadOnly)
            {
                // Revit Area params lưu bằng ft², convert từ m²
                param.Set(item.NetArea * SqMToSqFt);
                count++;
            }
        }

        tx.Commit();
        return count;
    }

    private static void EnsureSharedParameter(Document doc)
    {
        // Kiểm tra đã có parameter chưa
        var testElem = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_StructuralFraming)
            .WhereElementIsNotElementType()
            .FirstElement();

        if (testElem?.LookupParameter(SharedParamName) != null)
            return; // Đã có

        // Tạo shared parameter file tạm nếu chưa có
        var app = doc.Application;
        var originalFile = app.SharedParametersFilename;

        string sharedParamFile;
        if (string.IsNullOrEmpty(originalFile) || !File.Exists(originalFile))
        {
            sharedParamFile = Path.Combine(
                Path.GetTempPath(), "CIC_SharedParams.txt");
            if (!File.Exists(sharedParamFile))
                File.WriteAllText(sharedParamFile, "");
            app.SharedParametersFilename = sharedParamFile;
        }
        else
        {
            sharedParamFile = originalFile;
        }

        var defFile = app.OpenSharedParameterFile();
        if (defFile == null) return;

        // Tạo/lấy group
        var group = defFile.Groups.get_Item(SharedParamGroup)
                    ?? defFile.Groups.Create(SharedParamGroup);

        // Tạo definition nếu chưa có
        var def = group.Definitions.get_Item(SharedParamName);
        if (def == null)
        {
            var extDef = new ExternalDefinitionCreationOptions(SharedParamName, SpecTypeId.Area)
            {
                Description = "Diện tích ván khuôn (m²) — tự tính bởi CIC Tool B3.2"
            };
            def = group.Definitions.Create(extDef);
        }

        // Bind vào các categories kết cấu
        var catSet = new CategorySet();
        var cats = new[]
        {
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_StructuralFoundation
        };
        foreach (var cat in cats)
        {
            var c = doc.Settings.Categories.get_Item(cat);
            if (c != null) catSet.Insert(c);
        }

        var binding = app.Create.NewInstanceBinding(catSet);
        doc.ParameterBindings.Insert(def, binding, GroupTypeId.Data);

        // Khôi phục file gốc
        if (!string.IsNullOrEmpty(originalFile))
            app.SharedParametersFilename = originalFile;
    }

    #endregion

    #region Excel Export

    /// <summary>
    /// Xuất kết quả ra file Excel theo mẫu B3.2.
    /// Trả về đường dẫn file đã lưu.
    /// </summary>
    public static string ExportToExcel(FormworkResult result, string projectName, string? savePath = null)
    {
        // Nếu không truyền path → dùng SaveFileDialog
        if (string.IsNullOrEmpty(savePath))
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var defaultName = $"VanKhuon_B3.2_{projectName}_{timestamp}.xlsx";

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Lưu file Thống kê Ván khuôn",
                FileName = defaultName,
                DefaultExt = ".xlsx",
                Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dlg.ShowDialog() != true)
                return string.Empty; // User hủy

            savePath = dlg.FileName;
        }

        using var workbook = new XLWorkbook();

        // ═══ Sheet 1: Tổng hợp ═══
        var wsSummary = workbook.AddWorksheet("Tổng hợp");
        CreateSummarySheet(wsSummary, result, projectName);

        // ═══ Sheet 2: Chi tiết ═══
        var wsDetail = workbook.AddWorksheet("Chi tiết");
        CreateDetailSheet(wsDetail, result);

        workbook.SaveAs(savePath);
        return savePath;
    }

    // ═══ COLOR CONSTANTS ═══
    private static readonly XLColor HeaderBg = XLColor.FromHtml("#1F4E79");
    private static readonly XLColor HeaderFg = XLColor.White;
    private static readonly XLColor AltRowBg = XLColor.FromHtml("#D6E4F0");
    private static readonly XLColor TotalBg = XLColor.FromHtml("#2E75B6");
    private static readonly XLColor TotalFg = XLColor.White;
    private static readonly XLColor BorderColor = XLColor.FromHtml("#8DB4E2");

    private static void CreateSummarySheet(IXLWorksheet ws, FormworkResult result, string projectName)
    {
        // ════ TITLE ════
        ws.Cell(1, 1).Value = "BẢNG THỐNG KÊ VÁN KHUÔN — B3.2";
        ws.Range("A1:G1").Merge().Style
            .Font.SetBold(true)
            .Font.SetFontSize(16)
            .Font.SetFontColor(HeaderBg)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        ws.Cell(2, 1).Value = $"Dự án: {projectName}";
        ws.Cell(2, 1).Style.Font.SetBold(true).Font.SetFontSize(11);

        ws.Cell(3, 1).Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}";
        ws.Cell(3, 1).Style.Font.SetItalic(true).Font.SetFontColor(XLColor.Gray);

        // ════ TABLE HEADER ════
        int row = 5;
        var headers = new[] { "STT", "Tầng", "Loại CK", "Số lượng",
                             "DT thô (m²)", "Trừ GN (m²)", "DT ròng (m²)" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = headers[c];
            cell.Style
                .Font.SetBold(true)
                .Font.SetFontSize(11)
                .Font.SetFontColor(HeaderFg)
                .Fill.SetBackgroundColor(HeaderBg)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                .Border.SetOutsideBorderColor(BorderColor);
        }
        ws.Row(row).Height = 24;

        // ════ DATA ROWS ════
        var groups = result.Items
            .GroupBy(i => i.LevelName)
            .OrderBy(g => g.Key);

        int stt = 1;
        row++;

        foreach (var levelGroup in groups)
        {
            var catGroups = levelGroup
                .GroupBy(i => i.Category)
                .OrderBy(g => g.Key);

            foreach (var catGroup in catGroups)
            {
                ws.Cell(row, 1).Value = stt++;
                ws.Cell(row, 2).Value = levelGroup.Key;
                ws.Cell(row, 3).Value = catGroup.Key;
                ws.Cell(row, 4).Value = catGroup.Count();
                ws.Cell(row, 5).Value = Math.Round(catGroup.Sum(i => i.GrossArea), 2);
                ws.Cell(row, 6).Value = Math.Round(catGroup.Sum(i => i.DeductionArea), 2);
                ws.Cell(row, 7).Value = Math.Round(catGroup.Sum(i => i.NetArea), 2);

                // Style data row
                var dataRange = ws.Range(row, 1, row, headers.Length);
                dataRange.Style
                    .Font.SetFontSize(11)
                    .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                    .Border.SetInsideBorder(XLBorderStyleValues.Thin)
                    .Border.SetOutsideBorderColor(BorderColor)
                    .Border.SetInsideBorderColor(BorderColor);

                // Center align STT, số lượng
                ws.Cell(row, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                ws.Cell(row, 4).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                // Right align số liệu
                for (int c = 5; c <= 7; c++)
                {
                    ws.Cell(row, c).Style
                        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right)
                        .NumberFormat.SetFormat("#,##0.00");
                }

                // Alternating color
                if (stt % 2 == 0)
                    dataRange.Style.Fill.SetBackgroundColor(AltRowBg);

                row++;
            }
        }

        // ════ TOTAL ROW ════
        ws.Cell(row, 3).Value = "TỔNG CỘNG";
        ws.Cell(row, 4).Value = result.ElementCount;
        ws.Cell(row, 5).Value = Math.Round(result.TotalGrossArea, 2);
        ws.Cell(row, 6).Value = Math.Round(result.TotalDeduction, 2);
        ws.Cell(row, 7).Value = Math.Round(result.TotalNetArea, 2);

        var totalRange = ws.Range(row, 1, row, headers.Length);
        totalRange.Style
            .Font.SetBold(true)
            .Font.SetFontSize(12)
            .Font.SetFontColor(TotalFg)
            .Fill.SetBackgroundColor(TotalBg)
            .Border.SetOutsideBorder(XLBorderStyleValues.Medium)
            .Border.SetOutsideBorderColor(HeaderBg);

        ws.Cell(row, 4).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        for (int c = 5; c <= 7; c++)
        {
            ws.Cell(row, c).Style
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right)
                .NumberFormat.SetFormat("#,##0.00");
        }

        // ════ FORMAT ════
        ws.Columns(1, 7).AdjustToContents();
        ws.Column(1).Width = 6;   // STT
        ws.Column(4).Width = 10;  // SL
    }

    private static void CreateDetailSheet(IXLWorksheet ws, FormworkResult result)
    {
        // ════ HEADER ════
        var headers = new[] { "STT", "ID", "Loại CK", "Type", "Tầng",
                             "Rộng (mm)", "Cao (mm)", "Dài (mm)",
                             "DT thô (m²)", "Trừ GN (m²)", "DT ròng (m²)" };

        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style
                .Font.SetBold(true)
                .Font.SetFontSize(11)
                .Font.SetFontColor(HeaderFg)
                .Fill.SetBackgroundColor(HeaderBg)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                .Border.SetOutsideBorderColor(BorderColor);
        }
        ws.Row(1).Height = 24;

        // ════ DATA ════
        int row = 2;
        int stt = 1;
        foreach (var item in result.Items.OrderBy(i => i.LevelName).ThenBy(i => i.Category))
        {
            ws.Cell(row, 1).Value = stt++;
            ws.Cell(row, 2).Value = item.Id.IntegerValue;
            ws.Cell(row, 3).Value = item.Category;
            ws.Cell(row, 4).Value = item.TypeName;
            ws.Cell(row, 5).Value = item.LevelName;
            ws.Cell(row, 6).Value = item.WidthMm;
            ws.Cell(row, 7).Value = item.HeightMm;
            ws.Cell(row, 8).Value = item.LengthMm;
            ws.Cell(row, 9).Value = item.GrossArea;
            ws.Cell(row, 10).Value = item.DeductionArea;
            ws.Cell(row, 11).Value = item.NetArea;

            var dataRange = ws.Range(row, 1, row, headers.Length);
            dataRange.Style
                .Font.SetFontSize(10.5)
                .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                .Border.SetInsideBorder(XLBorderStyleValues.Thin)
                .Border.SetOutsideBorderColor(BorderColor)
                .Border.SetInsideBorderColor(BorderColor);

            // Center align STT, ID
            ws.Cell(row, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(row, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            // Right align kích thước + diện tích
            for (int c = 6; c <= 11; c++)
            {
                ws.Cell(row, c).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            }
            // Format số liệu
            for (int c = 6; c <= 8; c++)
                ws.Cell(row, c).Style.NumberFormat.SetFormat("#,##0");
            for (int c = 9; c <= 11; c++)
                ws.Cell(row, c).Style.NumberFormat.SetFormat("#,##0.00");

            // Highlight DT ròng
            ws.Cell(row, 11).Style
                .Font.SetBold(true)
                .Font.SetFontColor(XLColor.FromHtml("#1F4E79"));

            // Alternating color
            if (row % 2 == 0)
                dataRange.Style.Fill.SetBackgroundColor(AltRowBg);

            row++;
        }

        // ════ TOTAL ════
        ws.Cell(row, 3).Value = "TỔNG CỘNG";
        ws.Cell(row, 9).Value = Math.Round(result.TotalGrossArea, 2);
        ws.Cell(row, 10).Value = Math.Round(result.TotalDeduction, 2);
        ws.Cell(row, 11).Value = Math.Round(result.TotalNetArea, 2);

        var totalRange = ws.Range(row, 1, row, headers.Length);
        totalRange.Style
            .Font.SetBold(true)
            .Font.SetFontSize(11)
            .Font.SetFontColor(TotalFg)
            .Fill.SetBackgroundColor(TotalBg)
            .Border.SetOutsideBorder(XLBorderStyleValues.Medium);
        for (int c = 9; c <= 11; c++)
            ws.Cell(row, c).Style.NumberFormat.SetFormat("#,##0.00");

        // ════ FORMAT ════
        ws.Columns().AdjustToContents();
        ws.Column(1).Width = 6;   // STT
        ws.Column(2).Width = 10;  // ID
        ws.SheetView.FreezeRows(1);
    }

    #endregion

    #region Revit Schedule

    /// <summary>
    /// Tạo ViewSchedule hiển thị CIC_FormworkArea trong Revit.
    /// </summary>
    public static ViewSchedule? CreateRevitSchedule(Document doc, string scheduleName)
    {
        using var tx = new Transaction(doc, "Tạo Schedule Ván khuôn");
        tx.Start();

        try
        {
            // Tạo Multi-Category Schedule
            var schedule = ViewSchedule.CreateSchedule(doc,
                new ElementId(BuiltInCategory.OST_StructuralFraming));

            schedule.Name = scheduleName;

            // Thêm fields
            var schedulableDefs = schedule.Definition.GetSchedulableFields();

            AddFieldIfFound(schedule, schedulableDefs, "Family and Type");
            AddFieldIfFound(schedule, schedulableDefs, "Level");
            AddFieldIfFound(schedule, schedulableDefs, "Length");
            AddFieldIfFound(schedule, schedulableDefs, SharedParamName);

            // Sort by Level
            var levelField = FindField(schedule, "Level");
            if (levelField != null)
            {
                schedule.Definition.AddSortGroupField(
                    new ScheduleSortGroupField(levelField.FieldId, ScheduleSortOrder.Ascending));
            }

            tx.Commit();
            return schedule;
        }
        catch
        {
            tx.RollBack();
            return null;
        }
    }

    private static void AddFieldIfFound(ViewSchedule schedule,
        IList<SchedulableField> fields, string fieldName)
    {
        var field = fields.FirstOrDefault(f =>
            f.GetName(schedule.Document).IndexOf(fieldName, StringComparison.OrdinalIgnoreCase) >= 0);

        if (field != null)
        {
            schedule.Definition.AddField(field);
        }
    }

    private static ScheduleField? FindField(ViewSchedule schedule, string name)
    {
        var count = schedule.Definition.GetFieldCount();
        for (int i = 0; i < count; i++)
        {
            var f = schedule.Definition.GetField(i);
            if (f.GetName().IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                return f;
        }
        return null;
    }

    #endregion
}
