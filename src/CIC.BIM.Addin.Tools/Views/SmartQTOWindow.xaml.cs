using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using Autodesk.Revit.DB;
using CIC.BIM.Addin.Tools.Services;
using Microsoft.Win32;

namespace CIC.BIM.Addin.Tools.Views;

public partial class SmartQTOWindow : Window
{
    public SmartQTOViewModel ViewModel { get; }
    private Document _doc;
    private ICollection<ElementId> _selectedIds;
    private string _projectName;
    private List<SmartQTOResult> _currentResults = new();

    public SmartQTOWindow(SmartQTOViewModel viewModel, Document doc, ICollection<ElementId> selectedIds, string projectName)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _doc = doc;
        _selectedIds = selectedIds;
        _projectName = projectName;
        
        DataContext = ViewModel;
        lstCategories.ItemsSource = ViewModel.Categories;
    }

    private void BtnCalculate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            txtStatus.Text = "Đang tính toán...";
            txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DarkOrange);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

            var selectedCats = ViewModel.Categories.Where(x => x.IsSelected).Select(x => x.Category).ToList();
            if (!selectedCats.Any())
            {
                MessageBox.Show("Vui lòng chọn ít nhất 1 hạng mục để bóc khối lượng.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtStatus.Text = "Chưa chọn hạng mục.";
                return;
            }

            bool onlySelection = radActiveSelection.IsChecked == true;
            if (onlySelection && !_selectedIds.Any())
            {
                MessageBox.Show("Bạn chọn 'Chỉ đối tượng đang chọn' nhưng chưa chọn gì trong mô hình.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtStatus.Text = "Lỗi phạm vi bóc tách.";
                return;
            }

            bool groupByLevel = chkGroupByLevel.IsChecked == true;

            var service = new SmartQTOService(_doc);
            _currentResults = service.CalculateQTO(selectedCats, onlySelection, _selectedIds, groupByLevel);

            var viewSource = new CollectionViewSource { Source = _currentResults };
            if (groupByLevel)
            {
                viewSource.GroupDescriptions.Add(new PropertyGroupDescription("LevelName"));
            }
            viewSource.GroupDescriptions.Add(new PropertyGroupDescription("CategoryName"));
            
            dgPreview.ItemsSource = viewSource.View;

            if (_currentResults.Any())
            {
                txtStatus.Text = $"Đã tính xong {selectedCats.Count} hạng mục.";
                txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                txtSummary.Text = $"Tổng cộng: {_currentResults.Sum(x => x.Count)} cấu kiện.";
                btnExport.IsEnabled = true;
            }
            else
            {
                txtStatus.Text = "Không tìm thấy cấu kiện nào.";
                txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                txtSummary.Text = "Không có dữ liệu.";
                btnExport.IsEnabled = false;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Lỗi trong quá trình tính toán: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "Lỗi tính toán.";
        }
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentResults.Any()) return;

        var saveDialog = new SaveFileDialog
        {
            Title = "Lưu file BOQ Excel",
            Filter = "Excel Files|*.xlsx",
            FileName = $"CIC_BIM_BOQ_{_projectName}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };

        // Remove invalid characters from filename
        saveDialog.FileName = string.Join("_", saveDialog.FileName.Split(System.IO.Path.GetInvalidFileNameChars()));

        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                txtStatus.Text = "Đang xuất file Excel...";
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var exportService = new SmartQTOExportService();
                var filePath = exportService.ExportToExcel(_currentResults, _projectName, saveDialog.FileName);

                txtStatus.Text = "Xuất file thành công!";

                if (MessageBox.Show("Đã xuất file BOQ thành công! Bạn có muốn mở file lên ngay không?", "Thành công", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi lưu file Excel (Có thể file đang được mở bởi ứng dụng khác):\n" + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Text = "Lỗi lưu file.";
            }
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public class SmartQTOViewModel
{
    public List<CategoryItem> Categories { get; set; } = new();
    public QTOScope Scope { get; set; } = QTOScope.EntireProject;
    public bool GroupByLevel { get; set; } = false;
}

public enum QTOScope
{
    EntireProject,
    ActiveSelection
}

public class CategoryItem : INotifyPropertyChanged
{
    private bool _isSelected;
    
    public string Name { get; set; } = string.Empty;
    public BuiltInCategory Category { get; set; }
    
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
