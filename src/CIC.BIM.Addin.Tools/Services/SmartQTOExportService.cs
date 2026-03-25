using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace CIC.BIM.Addin.Tools.Services;

public class SmartQTOExportService
{
    public string ExportToExcel(List<SmartQTOResult> qtoResults, string projectName, string filePath = "")
    {
        if (qtoResults == null || !qtoResults.Any())
            return string.Empty;

        var targetPath = string.IsNullOrEmpty(filePath) 
            ? Path.Combine(Path.GetTempPath(), $"CIC_BIM_BOQ_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx")
            : filePath;

        using (var xlWorkbook = new XLWorkbook())
        {
            // ═══ Sheet 1: Chi tiết ═══
            var worksheet = xlWorkbook.Worksheets.Add("Khối lượng BOQ");
            CreateDetailSheet(worksheet, qtoResults, projectName);

            // ═══ Sheet 2: Tổng hợp ═══
            var summarySheet = xlWorkbook.Worksheets.Add("Tổng hợp");
            CreateSummarySheet(summarySheet, qtoResults, projectName);

            // Save
            xlWorkbook.SaveAs(targetPath);
        }

        return targetPath;
    }

    private void CreateDetailSheet(IXLWorksheet worksheet, List<SmartQTOResult> qtoResults, string projectName)
    {
        // --- Header ---
        worksheet.Cell("A1").Value = "BẢNG TỔNG HỢP KHỐI LƯỢNG (BOQ)";
        worksheet.Range("A1:I1").Merge().Style
            .Font.SetBold(true)
            .Font.SetFontSize(16)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        worksheet.Cell("A2").Value = $"Dự án: {projectName}";
        worksheet.Cell("A3").Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}";
        worksheet.Range("A2:I2").Merge().Style.Font.SetItalic(true);
        worksheet.Range("A3:I3").Merge().Style.Font.SetItalic(true);

        // --- Column Headers ---
        var headers = new[] { "STT", "Hạng mục (Category)", "Tên Cấu kiện (Family & Type)", "Kích thước/Dày", "Vật liệu", "Số Lượng", "Chiều dài (m)", "Diện tích (m²)", "Thể tích (m³)" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = worksheet.Cell(5, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightBlue;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        // Freeze header rows
        worksheet.SheetView.FreezeRows(5);

        // --- Data Rows ---
        int row = 6;
        int stt = 1;
        
        bool isGroupedByLevel = qtoResults.Select(x => x.LevelName).Distinct().Count() > 1 || 
                                qtoResults.Any(x => x.LevelName != "Không xác định Tầng");

        var groupedByCategory = qtoResults.GroupBy(x => x.CategoryName).ToList();

        foreach (var catGroup in groupedByCategory)
        {
            // Category Header Row
            worksheet.Cell(row, 1).Value = catGroup.Key;
            worksheet.Range(row, 1, row, 9).Merge().Style
                .Font.SetBold(true)
                .Fill.SetBackgroundColor(XLColor.PastelGray)
                .Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            row++;
            
            var groupedByLevel = catGroup.GroupBy(x => x.LevelName).ToList();
            
            foreach (var levelGroup in groupedByLevel)
            {
                // Level Header Row (if applied)
                if (isGroupedByLevel)
                {
                    worksheet.Cell(row, 1).Value = "▶ " + levelGroup.Key;
                    worksheet.Range(row, 1, row, 9).Merge().Style
                        .Font.SetBold(true)
                        .Font.SetItalic(true)
                        .Fill.SetBackgroundColor(XLColor.LightYellow)
                        .Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                    row++;
                }

                foreach (var item in levelGroup)
                {
                    worksheet.Cell(row, 1).Value = stt++;
                    worksheet.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    
                    worksheet.Cell(row, 2).Value = item.CategoryName;
                    worksheet.Cell(row, 3).Value = item.FamilyAndType;
                    worksheet.Cell(row, 4).Value = item.SizeTag;
                    worksheet.Cell(row, 5).Value = item.MaterialName;
                    
                    worksheet.Cell(row, 6).Value = item.Count;
                    
                    if (item.LengthM > 0) worksheet.Cell(row, 7).Value = Math.Round(item.LengthM, 2);
                    if (item.AreaM2 > 0) worksheet.Cell(row, 8).Value = Math.Round(item.AreaM2, 2);
                    if (item.VolumeM3 > 0) worksheet.Cell(row, 9).Value = Math.Round(item.VolumeM3, 2);

                    // Number format
                    worksheet.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(row, 8).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(row, 9).Style.NumberFormat.Format = "#,##0.000";

                    // Borders
                    for (int c = 1; c <= 9; c++)
                    {
                        worksheet.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    }

                    row++;
                }
            }
        }

        // Auto fit columns
        worksheet.Columns().AdjustToContents();
        worksheet.Column(3).Width = Math.Min(worksheet.Column(3).Width, 50); // Cap FamilyAndType width
    }

    /// <summary>
    /// Sheet tổng hợp — tổng KL theo hạng mục (Category).
    /// </summary>
    private void CreateSummarySheet(IXLWorksheet ws, List<SmartQTOResult> qtoResults, string projectName)
    {
        ws.Cell("A1").Value = "TỔNG HỢP KHỐI LƯỢNG THEO HẠNG MỤC";
        ws.Range("A1:F1").Merge().Style
            .Font.SetBold(true)
            .Font.SetFontSize(14)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        ws.Cell("A2").Value = $"Dự án: {projectName}";
        ws.Cell("A3").Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}";
        ws.Range("A2:F2").Merge().Style.Font.SetItalic(true);
        ws.Range("A3:F3").Merge().Style.Font.SetItalic(true);

        var summaryHeaders = new[] { "STT", "Hạng mục", "Số Lượng", "Tổng Chiều dài (m)", "Tổng Diện tích (m²)", "Tổng Thể tích (m³)" };
        for (int i = 0; i < summaryHeaders.Length; i++)
        {
            var cell = ws.Cell(5, i + 1);
            cell.Value = summaryHeaders[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        ws.SheetView.FreezeRows(5);

        var categorySummary = qtoResults
            .GroupBy(x => x.CategoryName)
            .Select(g => new
            {
                Category = g.Key,
                Count = g.Sum(x => x.Count),
                Length = g.Sum(x => x.LengthM),
                Area = g.Sum(x => x.AreaM2),
                Volume = g.Sum(x => x.VolumeM3)
            })
            .OrderBy(x => x.Category)
            .ToList();

        int row = 6;
        int stt = 1;
        foreach (var cat in categorySummary)
        {
            ws.Cell(row, 1).Value = stt++;
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 2).Value = cat.Category;
            ws.Cell(row, 3).Value = cat.Count;
            if (cat.Length > 0) ws.Cell(row, 4).Value = Math.Round(cat.Length, 2);
            if (cat.Area > 0) ws.Cell(row, 5).Value = Math.Round(cat.Area, 2);
            if (cat.Volume > 0) ws.Cell(row, 6).Value = Math.Round(cat.Volume, 3);

            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.000";

            for (int c = 1; c <= 6; c++)
                ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            row++;
        }

        // Grand total
        ws.Cell(row, 2).Value = "TỔNG CỘNG";
        ws.Cell(row, 2).Style.Font.Bold = true;
        ws.Cell(row, 3).Value = categorySummary.Sum(x => x.Count);
        ws.Cell(row, 3).Style.Font.Bold = true;
        if (categorySummary.Sum(x => x.Length) > 0)
        {
            ws.Cell(row, 4).Value = Math.Round(categorySummary.Sum(x => x.Length), 2);
            ws.Cell(row, 4).Style.Font.Bold = true;
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";
        }
        if (categorySummary.Sum(x => x.Area) > 0)
        {
            ws.Cell(row, 5).Value = Math.Round(categorySummary.Sum(x => x.Area), 2);
            ws.Cell(row, 5).Style.Font.Bold = true;
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";
        }
        if (categorySummary.Sum(x => x.Volume) > 0)
        {
            ws.Cell(row, 6).Value = Math.Round(categorySummary.Sum(x => x.Volume), 3);
            ws.Cell(row, 6).Style.Font.Bold = true;
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.000";
        }

        for (int c = 1; c <= 6; c++)
        {
            ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Cell(row, c).Style.Fill.BackgroundColor = XLColor.LightGoldenrodYellow;
        }

        ws.Columns().AdjustToContents();
    }
}
