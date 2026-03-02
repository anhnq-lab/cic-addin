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
            var worksheet = xlWorkbook.Worksheets.Add("Khối lượng BOQ");

            // --- Header ---
            worksheet.Cell("A1").Value = "BẢNG TỔNG HỢP KHỐI LƯỢNG (BOQ)";
            worksheet.Range("A1:G1").Merge().Style
                .Font.SetBold(true)
                .Font.SetFontSize(16)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            worksheet.Cell("A2").Value = $"Dự án: {projectName}";
            worksheet.Cell("A3").Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}";
            worksheet.Range("A2:H2").Merge().Style.Font.SetItalic(true);
            worksheet.Range("A3:H3").Merge().Style.Font.SetItalic(true);

            // --- Column Headers ---
            var headers = new[] { "STT", "Hạng mục (Category)", "Tên Cấu kiện (Family & Type)", "Kích thước/Dày", "Số Lượng", "Chiều dài (m)", "Diện tích (m2)", "Thể tích (m3)" };
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

            // --- Data Rows ---
            int row = 6;
            int stt = 1;
            
            // Check if user grouped by level (more than 1 distinct level, or someone explicitly checked the box)
            bool isGroupedByLevel = qtoResults.Select(x => x.LevelName).Distinct().Count() > 1 || 
                                    qtoResults.Any(x => x.LevelName != "Không xác định Tầng");

            var groupedByCategory = qtoResults.GroupBy(x => x.CategoryName).ToList();

            foreach (var catGroup in groupedByCategory)
            {
                // Category Header Row
                worksheet.Cell(row, 1).Value = catGroup.Key;
                worksheet.Range(row, 1, row, 8).Merge().Style
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
                        worksheet.Range(row, 1, row, 8).Merge().Style
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
                        
                        worksheet.Cell(row, 5).Value = item.Count;
                        
                        worksheet.Cell(row, 6).Value = item.LengthM > 0 ? Math.Round(item.LengthM, 2) : "";
                        worksheet.Cell(row, 7).Value = item.AreaM2 > 0 ? Math.Round(item.AreaM2, 2) : "";
                        worksheet.Cell(row, 8).Value = item.VolumeM3 > 0 ? Math.Round(item.VolumeM3, 2) : "";

                        // Borders
                        for (int c = 1; c <= 8; c++)
                        {
                            worksheet.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        }

                        row++;
                    }
                }
            }

            // Auto fit columns
            worksheet.Columns().AdjustToContents();

            // Save
            xlWorkbook.SaveAs(targetPath);
        }

        return targetPath;
    }
}
