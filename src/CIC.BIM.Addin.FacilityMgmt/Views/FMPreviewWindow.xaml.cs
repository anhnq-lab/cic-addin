using System.Data;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using ClosedXML.Excel;
using CIC.BIM.Addin.FacilityMgmt.Services;

namespace CIC.BIM.Addin.FacilityMgmt.Views;

public partial class FMPreviewWindow : Window
{
    private readonly Document _doc;
    private readonly Autodesk.Revit.ApplicationServices.Application _app;
    private List<Element>? _elements;
    private DataTable? _dataTable;

    /// <summary>After closing, indicates whether params were assigned.</summary>
    public bool ParamsAssigned { get; private set; }

    /// <summary>After closing, indicates whether data was auto-filled.</summary>
    public bool DataFilled { get; private set; }

    public FMPreviewWindow(Document doc, Autodesk.Revit.ApplicationServices.Application app)
    {
        InitializeComponent();
        _doc = doc;
        _app = app;

        // Auto-load on open
        Loaded += (_, _) => LoadData();

        // Track selection changes
        DgDevices.SelectionChanged += (_, _) => UpdateSelectionCount();
    }

    // ═══ Load data ═══
    private void LoadData()
    {
        _elements = FMDataService.CollectMEPElements(_doc);

        if (_elements.Count == 0)
        {
            TxtStatus.Text = "⚠ Không tìm thấy thiết bị MEP nào trong mô hình.";
            return;
        }

        _dataTable = FMDataService.BuildPreviewTable(_doc, _elements);
        DgDevices.ItemsSource = _dataTable.DefaultView;

        // Populate filter ComboBoxes
        PopulateFilters();

        // Update stats
        UpdateStats();

        // Enable action buttons
        BtnAutoFill.IsEnabled = true;
        BtnAssignParams.IsEnabled = true;
        BtnExportExcel.IsEnabled = true;
        BtnSelectAll.IsEnabled = true;
        BtnSelectNone.IsEnabled = true;

        TxtStatus.Text = $"✅ Đã tải {_elements.Count:N0} thiết bị MEP";
    }

    private void BtnLoad_Click(object sender, RoutedEventArgs e) => LoadData();

    // ═══ Populate filter ComboBoxes ═══
    private void PopulateFilters()
    {
        if (_dataTable == null) return;

        // Category filter
        CboCategory.Items.Clear();
        CboCategory.Items.Add("Tất cả");
        foreach (var cat in FMDataService.GetDistinctCategories(_dataTable))
            CboCategory.Items.Add(cat);
        CboCategory.SelectedIndex = 0;

        // Level filter
        CboLevel.Items.Clear();
        CboLevel.Items.Add("Tất cả");
        foreach (var level in FMDataService.GetDistinctLevels(_dataTable))
            CboLevel.Items.Add(level);
        CboLevel.SelectedIndex = 0;
    }

    // ═══ Update stats badges ═══
    private void UpdateStats()
    {
        if (_dataTable == null) return;

        var total = _dataTable.Rows.Count;
        var categories = FMDataService.GetDistinctCategories(_dataTable).Count;
        var levels = FMDataService.GetDistinctLevels(_dataTable).Count;
        var unassigned = _dataTable.AsEnumerable()
            .Count(r => r.Field<string>("Đã gán FM") == "⬜ Chưa gán");

        TxtTotalDevices.Text = total.ToString();
        TxtCategoryCount.Text = categories.ToString();
        TxtLevelCount.Text = levels.ToString();
        TxtAssignedCount.Text = unassigned.ToString();
    }

    // ═══ Filter logic ═══
    private void ApplyFilter()
    {
        if (_dataTable == null) return;

        var filters = new List<string>();

        // Category filter
        var selectedCat = CboCategory.SelectedItem?.ToString();
        if (!string.IsNullOrEmpty(selectedCat) && selectedCat != "Tất cả")
        {
            var escaped = selectedCat.Replace("'", "''");
            filters.Add($"[Phân loại FM] = '{escaped}'");
        }

        // Level filter
        var selectedLevel = CboLevel.SelectedItem?.ToString();
        if (!string.IsNullOrEmpty(selectedLevel) && selectedLevel != "Tất cả")
        {
            var escaped = selectedLevel.Replace("'", "''");
            filters.Add($"[Tầng] = '{escaped}'");
        }

        // Only unassigned
        if (ChkOnlyUnassigned.IsChecked == true)
        {
            filters.Add("[Đã gán FM] = '⬜ Chưa gán'");
        }

        // Text search
        var keyword = TxtSearch.Text.Trim();
        if (!string.IsNullOrEmpty(keyword))
        {
            var escaped = keyword.Replace("'", "''");
            var searchCols = new[] { "Tên thiết bị", "Family", "Type", "Vị trí", "Mã tài sản" };
            var searchFilters = searchCols.Select(c => $"[{c}] LIKE '%{escaped}%'");
            filters.Add($"({string.Join(" OR ", searchFilters)})");
        }

        try
        {
            _dataTable.DefaultView.RowFilter = filters.Count > 0
                ? string.Join(" AND ", filters)
                : "";
        }
        catch
        {
            _dataTable.DefaultView.RowFilter = "";
        }

        UpdateVisibleCount();
    }

    private void CboFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void ChkOnlyUnassigned_Changed(object sender, RoutedEventArgs e) => ApplyFilter();
    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    // ═══ Selection helpers ═══
    private void UpdateSelectionCount()
    {
        TxtSelectedCount.Text = DgDevices.SelectedItems.Count.ToString();
    }

    private void UpdateVisibleCount()
    {
        if (_dataTable == null) return;
        TxtVisibleCount.Text = _dataTable.DefaultView.Count.ToString();
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        DgDevices.SelectAll();
        UpdateSelectionCount();
    }

    private void BtnSelectNone_Click(object sender, RoutedEventArgs e)
    {
        DgDevices.UnselectAll();
        UpdateSelectionCount();
    }

    // ═══ Get selected element IDs ═══
    private List<long> GetSelectedElementIds()
    {
        var ids = new List<long>();

        if (DgDevices.SelectedItems.Count > 0)
        {
            foreach (DataRowView rowView in DgDevices.SelectedItems)
            {
                ids.Add((long)rowView["ElementId"]);
            }
        }
        else
        {
            // No selection = all visible
            if (_dataTable != null)
            {
                foreach (DataRowView rowView in _dataTable.DefaultView)
                {
                    ids.Add((long)rowView["ElementId"]);
                }
            }
        }

        return ids;
    }

    private List<Element> GetElementsByIds(List<long> ids)
    {
        if (_elements == null) return new List<Element>();

        var idSet = new HashSet<long>(ids);
        return _elements.Where(e =>
        {
            try { return idSet.Contains(e.Id.Value); }
            catch { return idSet.Contains(e.Id.IntegerValue); }
        }).ToList();
    }

    // ═══ ACTION: Gán tham số FM ═══
    private void BtnAssignParams_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var log = new System.Text.StringBuilder();

            // Step 1: Create shared param file
            log.AppendLine("▶ Tạo Shared Parameter file...");
            string paramFile;
            try
            {
                paramFile = ParameterService.EnsureSharedParamFile(_app);
                log.AppendLine($"  ✓ File: {paramFile}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi tạo file tham số:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Step 2: Open shared param file
            var defFile = _app.OpenSharedParameterFile();
            if (defFile == null)
            {
                MessageBox.Show("Không thể đọc file Shared Parameter.\nVui lòng thử lại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                ParameterService.RestoreOriginalParamFile(_app);
                return;
            }

            // Step 3: Get group
            var group = defFile.Groups.get_Item(FMParameters.GroupName);
            if (group == null)
            {
                MessageBox.Show($"Không tìm thấy group '{FMParameters.GroupName}' trong file.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                ParameterService.RestoreOriginalParamFile(_app);
                return;
            }

            // Step 4: Build category set
            var categories = _doc.Settings.Categories;
            var catSet = _app.Create.NewCategorySet();

            foreach (var builtInCat in FMParameters.TargetCategories)
            {
                try
                {
                    var cat = categories.get_Item(builtInCat);
                    if (cat != null && cat.AllowsBoundParameters)
                        catSet.Insert(cat);
                }
                catch { }
            }

            if (catSet.Size == 0)
            {
                MessageBox.Show("Không tìm thấy category MEP nào.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                ParameterService.RestoreOriginalParamFile(_app);
                return;
            }

            // Step 5: Bind parameters
            using var tx = new Transaction(_doc, "CIC - Gán tham số Vận hành");
            tx.Start();

            int boundCount = 0;
            int alreadyBound = 0;
            var bindingMap = _doc.ParameterBindings;

            foreach (var paramDef in FMParameters.All)
            {
                try
                {
                    Definition? def = null;
                    foreach (Definition d in group.Definitions)
                    {
                        if (d.Name == paramDef.Name) { def = d; break; }
                    }

                    if (def == null)
                    {
                        log.AppendLine($"  ✗ Không tìm thấy: {paramDef.Name}");
                        continue;
                    }

                    var existing = bindingMap.get_Item(def);
                    if (existing != null)
                    {
                        alreadyBound++;
                        continue;
                    }

                    var binding = _app.Create.NewInstanceBinding(catSet);
                    if (bindingMap.Insert(def, binding, FMParameters.ParameterGroup))
                        boundCount++;
                    else
                        log.AppendLine($"  ✗ Bind thất bại: {paramDef.Name}");
                }
                catch (Exception ex)
                {
                    log.AppendLine($"  ✗ {paramDef.Name}: {ex.Message}");
                }
            }

            tx.Commit();
            ParameterService.RestoreOriginalParamFile(_app);

            ParamsAssigned = true;

            // Show result
            var msg = $"✅ Gán tham số FM hoàn tất!\n\n" +
                     $"• Gán mới: {boundCount} tham số\n" +
                     $"• Đã có sẵn: {alreadyBound} tham số\n" +
                     $"• Tổng: {FMParameters.All.Length} tham số FM";

            if (log.Length > 0)
                msg += $"\n\n📋 Chi tiết:\n{log}";

            TxtStatus.Text = $"✅ Gán tham số: {boundCount} mới, {alreadyBound} đã có";

            MessageBox.Show(msg, "Gán tham số FM", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"❌ Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ═══ ACTION: Tự động điền ═══
    private void BtnAutoFill_Click(object sender, RoutedEventArgs e)
    {
        if (_elements == null || _dataTable == null) return;

        var selectedIds = GetSelectedElementIds();
        var targetElements = GetElementsByIds(selectedIds);

        if (targetElements.Count == 0)
        {
            MessageBox.Show("Không có thiết bị nào để điền dữ liệu.", "Thông báo", MessageBoxButton.OK);
            return;
        }

        // Confirm
        var confirm = MessageBox.Show(
            $"Tự động điền dữ liệu FM cho {targetElements.Count} thiết bị?\n\n" +
            "• Location (từ Room/Space)\n" +
            "• Category (phân loại FM)\n" +
            "• AssetCode (mã tài sản)\n" +
            "• Status = Active\n" +
            "• Condition = Good\n" +
            "• MaintenanceCycle = 180 ngày\n\n" +
            "Chỉ điền vào các ô trống.",
            "Tự động điền dữ liệu FM",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) return;

        try
        {
            using var tx = new Transaction(_doc, "CIC - Điền dữ liệu Vận hành");
            tx.Start();

            int filledCount = 0;
            var categoryCounters = new Dictionary<string, int>();

            foreach (var element in targetElements)
            {
                var filled = FillElementFMData(element, categoryCounters);
                if (filled) filledCount++;
            }

            tx.Commit();

            DataFilled = true;

            // Reload data to reflect changes
            _dataTable = FMDataService.BuildPreviewTable(_doc, _elements);
            DgDevices.ItemsSource = _dataTable.DefaultView;
            PopulateFilters();
            UpdateStats();
            ApplyFilter();

            TxtStatus.Text = $"🔄 Tự động điền: {filledCount}/{targetElements.Count} thiết bị đã cập nhật";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"❌ Lỗi điền dữ liệu:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Fill FM data for a single element (adapted from FillFMDataCommand).</summary>
    private bool FillElementFMData(Element element, Dictionary<string, int> counters)
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
            fmCategory = existingCategory;
        }

        if (!counters.ContainsKey(fmCategory)) counters[fmCategory] = 0;
        counters[fmCategory]++;

        // Location
        if (string.IsNullOrEmpty(ParameterService.GetStringParam(element, "CIC_FM_Location")))
        {
            var location = LocationService.GetElementLocation(element, _doc);
            if (!string.IsNullOrEmpty(location))
            {
                ParameterService.SetStringParam(element, "CIC_FM_Location", location);
                anyFilled = true;
            }
        }

        // AssetCode
        if (string.IsNullOrEmpty(ParameterService.GetStringParam(element, "CIC_FM_AssetCode")))
        {
            var assetCode = GenerateAssetCode(element, fmCategory, counters[fmCategory]);
            ParameterService.SetStringParam(element, "CIC_FM_AssetCode", assetCode);
            anyFilled = true;
        }

        // Status
        if (string.IsNullOrEmpty(ParameterService.GetStringParam(element, "CIC_FM_Status")))
        {
            ParameterService.SetStringParam(element, "CIC_FM_Status", "Active");
            anyFilled = true;
        }

        // Condition
        if (string.IsNullOrEmpty(ParameterService.GetStringParam(element, "CIC_FM_Condition")))
        {
            ParameterService.SetStringParam(element, "CIC_FM_Condition", "Good");
            anyFilled = true;
        }

        // MaintenanceCycle
        var existingCycle = ParameterService.GetIntParam(element, "CIC_FM_MaintenanceCycle");
        if (existingCycle == null || existingCycle == 0)
        {
            ParameterService.SetIntParam(element, "CIC_FM_MaintenanceCycle", 180);
            anyFilled = true;
        }

        return anyFilled;
    }

    private string GenerateAssetCode(Element element, string fmCategory, int counter)
    {
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
            _ => "TB"
        };

        var levelCode = "XX";
        Level? level = null;

        if (element.LevelId != ElementId.InvalidElementId)
            level = _doc.GetElement(element.LevelId) as Level;

        if (level == null)
        {
            var levelParam = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
            if (levelParam != null && levelParam.AsElementId() != ElementId.InvalidElementId)
                level = _doc.GetElement(levelParam.AsElementId()) as Level;
        }

        if (level != null)
        {
            var levelName = level.Name;
            var match = System.Text.RegularExpressions.Regex.Match(levelName, @"(\d+)");
            if (match.Success)
            {
                var num = match.Value;
                if (levelName.Contains("B") || levelName.IndexOf("basement", StringComparison.OrdinalIgnoreCase) >= 0
                    || levelName.IndexOf("hầm", StringComparison.OrdinalIgnoreCase) >= 0)
                    levelCode = $"B{num}";
                else
                    levelCode = $"T{num}";
            }
            else
            {
                levelCode = levelName.Length <= 4 ? levelName : levelName.Substring(0, 4);
            }
        }

        return $"{prefix}-{levelCode}-{counter:D3}";
    }

    // ═══ ACTION: Xuất Excel ═══
    private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
    {
        if (_elements == null || _elements.Count == 0)
        {
            MessageBox.Show("Chưa có dữ liệu. Vui lòng tải dữ liệu trước.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Ask for save location
        string? savePath = null;
        using (var saveDialog = new System.Windows.Forms.SaveFileDialog())
        {
            saveDialog.Title = "Lưu báo cáo thiết bị vận hành";
            saveDialog.Filter = "Excel Files (*.xlsx)|*.xlsx";
            saveDialog.FileName = $"FM_Report_{SanitizeFileName(_doc.Title)}_{DateTime.Now:yyyyMMdd}";
            saveDialog.DefaultExt = ".xlsx";
            saveDialog.OverwritePrompt = true;

            if (saveDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            savePath = saveDialog.FileName;
        }

        if (string.IsNullOrEmpty(savePath)) return;

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

            foreach (var element in _elements)
            {
                ws.Cell(row, 1).Value = stt++;
                ws.Cell(row, 2).Value = ParameterService.GetStringParam(element, "CIC_FM_AssetCode") ?? "";
                ws.Cell(row, 3).Value = element.Name ?? "";
                ws.Cell(row, 4).Value = GetFamilyName(element) ?? "";
                ws.Cell(row, 5).Value = GetTypeName(element) ?? "";
                ws.Cell(row, 6).Value = ParameterService.GetStringParam(element, "CIC_FM_Category") ?? "";
                ws.Cell(row, 7).Value = ParameterService.GetStringParam(element, "CIC_FM_Location") ?? "";
                ws.Cell(row, 8).Value = GetLevelName(element) ?? "";
                ws.Cell(row, 9).Value = ParameterService.GetStringParam(element, "CIC_FM_Manufacturer") ?? "";
                ws.Cell(row, 10).Value = ParameterService.GetStringParam(element, "CIC_FM_Model") ?? "";

                var cycle = ParameterService.GetIntParam(element, "CIC_FM_MaintenanceCycle");
                ws.Cell(row, 11).Value = cycle ?? 0;

                ws.Cell(row, 12).Value = ParameterService.GetStringParam(element, "CIC_FM_Status") ?? "";
                ws.Cell(row, 13).Value = ParameterService.GetStringParam(element, "CIC_FM_Condition") ?? "";
                ws.Cell(row, 14).Value = element.Id.Value;

                if (row % 2 == 0)
                {
                    ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor =
                        XLColor.FromHtml("#F2F7FC");
                }

                row++;
            }

            ws.Columns().AdjustToContents();

            // Summary sheet
            var summaryWs = workbook.Worksheets.Add("Tổng hợp");
            summaryWs.Cell(1, 1).Value = "TỔNG HỢP THIẾT BỊ VẬN HÀNH";
            summaryWs.Cell(1, 1).Style.Font.Bold = true;
            summaryWs.Cell(1, 1).Style.Font.FontSize = 14;
            summaryWs.Cell(2, 1).Value = $"Dự án: {_doc.Title}";
            summaryWs.Cell(3, 1).Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}";
            summaryWs.Cell(4, 1).Value = $"Tổng số thiết bị: {_elements.Count}";

            summaryWs.Cell(6, 1).Value = "Phân loại";
            summaryWs.Cell(6, 2).Value = "Số lượng";
            summaryWs.Cell(6, 1).Style.Font.Bold = true;
            summaryWs.Cell(6, 2).Style.Font.Bold = true;

            var categoryCounts = new Dictionary<string, int>();
            foreach (var elem in _elements)
            {
                var cat = ParameterService.GetStringParam(elem, "CIC_FM_Category") ?? "Chưa phân loại";
                if (!categoryCounts.ContainsKey(cat)) categoryCounts[cat] = 0;
                categoryCounts[cat]++;
            }

            int summaryRow = 7;
            foreach (var kvp in categoryCounts.OrderByDescending(x => x.Value))
            {
                summaryWs.Cell(summaryRow, 1).Value = kvp.Key;
                summaryWs.Cell(summaryRow, 2).Value = kvp.Value;
                summaryRow++;
            }
            summaryWs.Columns().AdjustToContents();

            workbook.SaveAs(savePath);

            TxtStatus.Text = $"📊 Đã xuất {_elements.Count} thiết bị → {System.IO.Path.GetFileName(savePath)}";

            MessageBox.Show(
                $"✅ Xuất thành công!\n\n📊 {_elements.Count} thiết bị\n📁 {savePath}",
                "Xuất báo cáo FM", MessageBoxButton.OK, MessageBoxImage.Information);

            // Open file
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = savePath,
                    UseShellExecute = true
                });
            }
            catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"❌ Lỗi xuất Excel:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

    private string? GetLevelName(Element element)
    {
        if (element.LevelId != ElementId.InvalidElementId)
        {
            var level = _doc.GetElement(element.LevelId) as Level;
            if (level != null) return level.Name;
        }

        var levelParam = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
        if (levelParam != null && levelParam.AsElementId() != ElementId.InvalidElementId)
        {
            var level = _doc.GetElement(levelParam.AsElementId()) as Level;
            if (level != null) return level.Name;
        }

        return null;
    }

    private string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }

    // ═══ Close ═══
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = ParamsAssigned || DataFilled;
        Close();
    }
}
