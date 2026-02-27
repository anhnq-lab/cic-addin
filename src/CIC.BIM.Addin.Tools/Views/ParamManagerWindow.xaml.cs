using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Autodesk.Revit.DB;
using CIC.BIM.Addin.Tools.Services;
using static CIC.BIM.Addin.Tools.Services.ParamManagerService;

namespace CIC.BIM.Addin.Tools.Views;

public partial class ParamManagerWindow : Window
{
    private readonly Document? _doc;
    private List<Element>? _elements;
    private DataTable? _dataTable;
    private List<string> _editableColumns = new();
    private readonly Dictionary<CheckBox, CategoryInfo> _categoryCheckboxes = new();

    public ParamManagerWindow(Document? doc = null)
    {
        InitializeComponent();
        _doc = doc;
        InitDisciplineComboBox();
    }

    // ═══ Khởi tạo ComboBox Bộ môn ═══
    private void InitDisciplineComboBox()
    {
        var disciplines = new[]
        {
            Discipline.TatCa,
            Discipline.KetCau,
            Discipline.KienTruc,
            Discipline.CoDien,
            Discipline.DuongOng
        };

        foreach (var d in disciplines)
            CboDiscipline.Items.Add(GetDisciplineName(d));

        CboDiscipline.SelectedIndex = 0;
    }

    // ═══ Bộ môn thay đổi → cập nhật CheckBox cấu kiện ═══
    private void CboDiscipline_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboDiscipline.SelectedIndex < 0) return;

        var disciplines = new[]
        {
            Discipline.TatCa,
            Discipline.KetCau,
            Discipline.KienTruc,
            Discipline.CoDien,
            Discipline.DuongOng
        };

        var selectedDiscipline = disciplines[CboDiscipline.SelectedIndex];
        var categories = GetCategoriesByDiscipline(selectedDiscipline);

        // Cập nhật badge
        TxtBadge.Text = GetDisciplineName(selectedDiscipline);

        // Xóa checkbox cũ
        PanelCategories.Children.Clear();
        _categoryCheckboxes.Clear();

        // Thêm nhãn
        var label = new TextBlock
        {
            Text = "Cấu kiện:",
            FontSize = 12,
            Foreground = FindResource("TextSecBrush") as System.Windows.Media.Brush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        PanelCategories.Children.Add(label);

        // Tạo CheckBox cho từng loại cấu kiện
        // Tránh trùng tên hiển thị
        var addedNames = new HashSet<string>();
        foreach (var catInfo in categories)
        {
            if (!addedNames.Add(catInfo.DisplayName)) continue;

            var chk = new CheckBox
            {
                Content = catInfo.DisplayName,
                IsChecked = true,
                Foreground = FindResource("TextBrush") as System.Windows.Media.Brush,
                FontSize = 12,
                Margin = new Thickness(0, 3, 12, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            PanelCategories.Children.Add(chk);
            _categoryCheckboxes[chk] = catInfo;
        }

        // Thêm nút "Chọn tất cả / Bỏ chọn"
        var btnSelectAll = new Button
        {
            Content = "✓ Tất cả",
            FontSize = 10,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(8, 0, 0, 0),
            Background = FindResource("SurfaceBgBrush") as System.Windows.Media.Brush,
            Foreground = FindResource("TextBrush") as System.Windows.Media.Brush,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        btnSelectAll.Click += (s, ev) =>
        {
            foreach (var chk in _categoryCheckboxes.Keys)
                chk.IsChecked = true;
        };
        PanelCategories.Children.Add(btnSelectAll);
    }

    // ═══ Lấy danh sách CategoryInfo đã chọn ═══
    private List<CategoryInfo> GetSelectedCategories()
    {
        var selected = new List<CategoryInfo>();
        foreach (var kvp in _categoryCheckboxes)
        {
            if (kvp.Key.IsChecked == true)
                selected.Add(kvp.Value);
        }
        return selected;
    }

    // ═══ Tải dữ liệu ═══
    private void BtnLoad_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null) return;

        var selectedCats = GetSelectedCategories();
        if (selectedCats.Count == 0)
        {
            TxtStatus.Text = "⚠ Chưa chọn loại cấu kiện nào.";
            return;
        }

        _elements = ParamManagerService.CollectElements(_doc, selectedCats);

        if (_elements.Count == 0)
        {
            TxtStatus.Text = "⚠ Không tìm thấy đối tượng nào trong mô hình.";
            BtnAutoFill.IsEnabled = false;
            BtnWrite.IsEnabled = false;
            BtnExport.IsEnabled = false;
            return;
        }

        _dataTable = ParamManagerService.ReadParamsToTable(_doc, _elements);
        DgParams.ItemsSource = _dataTable.DefaultView;

        TxtStatus.Text = $"✅ Đã tải {_elements.Count:N0} đối tượng, {_dataTable.Columns.Count} cột tham số";
        BtnAutoFill.IsEnabled = true;
        BtnWrite.IsEnabled = true;
        BtnExport.IsEnabled = true;
    }

    // ═══ Tìm kiếm/lọc trong DataGrid ═══
    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_dataTable == null) return;

        var keyword = TxtSearch.Text.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            _dataTable.DefaultView.RowFilter = "";
            return;
        }

        // Tìm kiếm trong tất cả cột string
        var filters = new List<string>();
        foreach (DataColumn col in _dataTable.Columns)
        {
            if (col.DataType == typeof(string))
            {
                // Escape single quotes in keyword
                var escaped = keyword.Replace("'", "''");
                filters.Add($"[{col.ColumnName}] LIKE '%{escaped}%'");
            }
        }

        if (filters.Count > 0)
        {
            try
            {
                _dataTable.DefaultView.RowFilter = string.Join(" OR ", filters);
            }
            catch
            {
                _dataTable.DefaultView.RowFilter = "";
            }
        }
    }

    // ═══ Auto-Generate Column Handler ═══
    private void DgParams_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        var readOnlyCols = new[] { "ElementId", "Loại cấu kiện", "Bộ môn", "Type", "Tầng",
                                   "Rộng (mm)", "Cao (mm)", "Dài (mm)",
                                   "Diện tích (m²)", "Thể tích (m³)" };

        if (readOnlyCols.Contains(e.Column.Header.ToString()))
        {
            e.Column.IsReadOnly = true;
            if (e.Column is DataGridTextColumn tc)
            {
                tc.ElementStyle = new Style(typeof(TextBlock))
                {
                    Setters = { new Setter(TextBlock.ForegroundProperty,
                        new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x6C, 0x70, 0x86))) }
                };
            }
        }
        else
        {
            e.Column.IsReadOnly = false;
            _editableColumns.Add(e.Column.Header.ToString()!);
        }

        e.Column.MinWidth = 60;
        e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);

        // Ẩn cột ElementId
        if (e.Column.Header.ToString() == "ElementId")
            e.Column.Visibility = System.Windows.Visibility.Collapsed;
    }

    // ═══ Tự động điền ═══
    private void BtnAutoFill_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null || _elements == null) return;

        var result = ParamManagerService.AutoPopulate(_doc, _elements);

        // Tải lại dữ liệu
        _dataTable = ParamManagerService.ReadParamsToTable(_doc, _elements);
        _editableColumns.Clear();
        DgParams.ItemsSource = _dataTable.DefaultView;

        TxtStatus.Text = $"🔄 Tự động điền: {result.ValuesWritten} giá trị cho {result.ElementsProcessed} đối tượng";

        if (result.Warnings.Count > 0)
        {
            var msg = string.Join("\n", result.Warnings.Take(5));
            MessageBox.Show(msg, "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ═══ Gán hàng loạt ═══
    private void BtnBatch_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null || _elements == null || _dataTable == null) return;

        var paramName = TxtBatchParam.Text.Trim();
        var value = TxtBatchValue.Text.Trim();

        if (string.IsNullOrEmpty(paramName))
        {
            MessageBox.Show("Vui lòng nhập tên tham số cần gán.", "Thiếu thông tin", MessageBoxButton.OK);
            return;
        }

        // Lấy ID đối tượng đã chọn hoặc tất cả
        var selectedRows = DgParams.SelectedItems.Cast<System.Data.DataRowView>().ToList();
        IEnumerable<long> ids;

        if (selectedRows.Count > 0)
        {
            ids = selectedRows.Select(r => (long)r["ElementId"]);
        }
        else
        {
            ids = _dataTable.Rows.Cast<DataRow>().Select(r => (long)r["ElementId"]);
        }

        var count = ParamManagerService.BatchWrite(_doc, ids, paramName, value);

        // Tải lại
        _dataTable = ParamManagerService.ReadParamsToTable(_doc, _elements);
        _editableColumns.Clear();
        DgParams.ItemsSource = _dataTable.DefaultView;

        var scope = selectedRows.Count > 0 ? $"{selectedRows.Count} dòng đã chọn" : "tất cả";
        TxtStatus.Text = $"📝 Gán hàng loạt: {paramName} = \"{value}\" — {count} giá trị ({scope})";
    }

    // ═══ Ghi vào Mô hình ═══
    private void BtnWrite_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null || _dataTable == null || _editableColumns.Count == 0) return;

        var result = ParamManagerService.WriteFromTable(_doc, _dataTable, _editableColumns);

        TxtStatus.Text = $"💾 Đã ghi {result.ValuesWritten} giá trị cho {result.ElementsUpdated} đối tượng" +
                         (result.Errors > 0 ? $" ({result.Errors} lỗi)" : "");
    }

    // ═══ Xuất Excel (placeholder) ═══
    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (_dataTable == null || _dataTable.Rows.Count == 0)
        {
            MessageBox.Show("Chưa có dữ liệu để xuất.", "Thông báo", MessageBoxButton.OK);
            return;
        }

        // Export CSV (Excel-compatible)
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"CIC_ThamSo_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            Title = "Xuất dữ liệu tham số"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                using var writer = new System.IO.StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8);

                // Header
                var headers = _dataTable.Columns.Cast<DataColumn>()
                    .Where(c => c.ColumnName != "ElementId")
                    .Select(c => $"\"{c.ColumnName}\"");
                writer.WriteLine(string.Join(",", headers));

                // Rows
                foreach (DataRow row in _dataTable.Rows)
                {
                    var values = _dataTable.Columns.Cast<DataColumn>()
                        .Where(c => c.ColumnName != "ElementId")
                        .Select(c => $"\"{row[c]?.ToString()?.Replace("\"", "\"\"")}\"");
                    writer.WriteLine(string.Join(",", values));
                }

                TxtStatus.Text = $"📊 Đã xuất {_dataTable.Rows.Count} dòng → {dlg.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi xuất file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // ═══ Đóng ═══
    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
