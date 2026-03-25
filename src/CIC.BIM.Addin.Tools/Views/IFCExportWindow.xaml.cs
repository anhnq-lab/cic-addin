using System.Diagnostics;
using System.IO;
using System.Windows;
using Autodesk.Revit.DB;
using CIC.BIM.Addin.Tools.Services;

namespace CIC.BIM.Addin.Tools.Views;

public partial class IFCExportWindow : Window
{
    private readonly Document _doc;
    private List<View3D>? _views3D;
    private string _outputFolder = "";
    private string _fileName = "";

    public IFCExportWindow(Document doc)
    {
        InitializeComponent();
        _doc = doc;
        Loaded += (_, _) => Initialize();
    }

    private void Initialize()
    {
        // Populate IFC versions
        foreach (var kvp in IFCExportService.VersionMap)
            CboVersion.Items.Add(kvp.Key);
        CboVersion.SelectedIndex = 0; // Default: IFC 2x3 CV2

        // Populate 3D views
        _views3D = IFCExportService.GetAvailable3DViews(_doc);
        CboView.Items.Add("(Không lọc — toàn bộ model)");
        foreach (var v in _views3D)
            CboView.Items.Add(v.Name);
        CboView.SelectedIndex = 0;

        // Default output path
        _outputFolder = IFCExportService.GenerateDefaultOutputFolder(_doc);
        _fileName = IFCExportService.GenerateDefaultFileName(_doc);
        TxtOutputPath.Text = Path.Combine(_outputFolder, _fileName + ".ifc");

        // Info
        var elemCount = new FilteredElementCollector(_doc)
            .WhereElementIsNotElementType()
            .GetElementCount();
        TxtInfo.Text = $"Model: {_doc.Title} | ~{elemCount:N0} phần tử | {_views3D.Count} 3D views";
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.SaveFileDialog
        {
            Title = "Chọn vị trí lưu file IFC",
            Filter = "IFC Files (*.ifc)|*.ifc",
            FileName = _fileName + ".ifc",
            InitialDirectory = _outputFolder,
            DefaultExt = "ifc"
        };

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _outputFolder = Path.GetDirectoryName(dlg.FileName) ?? _outputFolder;
            _fileName = Path.GetFileNameWithoutExtension(dlg.FileName);
            TxtOutputPath.Text = dlg.FileName;
        }
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtOutputPath.Text))
        {
            MessageBox.Show("Vui lòng chọn đường dẫn xuất.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Build config
        var config = new IFCExportService.ExportConfig
        {
            OutputFolder = _outputFolder,
            FileName = _fileName,
            ExportBaseQuantities = ChkBaseQuantities.IsChecked == true,
            WallAndColumnSplitting = ChkSplitWalls.IsChecked == true,
            VisibleElementsOnly = ChkVisibleOnly.IsChecked == true,
            IncludeFMProperties = ChkFMProperties.IsChecked == true,
            ExportRevitPropertySets = ChkRevitPsets.IsChecked == true
        };

        // IFC version
        var selectedVersion = CboVersion.SelectedItem?.ToString() ?? "";
        if (IFCExportService.VersionMap.TryGetValue(selectedVersion, out var version))
            config.Version = version;

        // View filter
        if (CboView.SelectedIndex > 0 && _views3D != null)
        {
            var idx = CboView.SelectedIndex - 1; // offset by 1 for "(Không lọc)" item
            if (idx >= 0 && idx < _views3D.Count)
                config.FilterViewId = _views3D[idx].Id;
        }

        // Export
        BtnExport.IsEnabled = false;
        TxtStatus.Text = "⏳ Đang xuất IFC...";

        try
        {
            var result = IFCExportService.ExportIFC(_doc, config);

            if (result != null && File.Exists(result))
            {
                var fileInfo = new FileInfo(result);
                var sizeMB = fileInfo.Length / 1024.0 / 1024.0;

                TxtStatus.Text = $"✅ Xuất thành công! ({sizeMB:F1} MB)";
                TxtInfo.Text = $"✅ File: {result}";

                var openResult = MessageBox.Show(
                    $"✅ Xuất IFC thành công!\n\n" +
                    $"📁 File: {result}\n" +
                    $"📊 Kích thước: {sizeMB:F1} MB\n" +
                    $"📋 Phiên bản: {selectedVersion}\n\n" +
                    "Mở thư mục chứa file?",
                    "Xuất IFC thành công",
                    MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (openResult == MessageBoxResult.Yes)
                {
                    Process.Start("explorer.exe", $"/select,\"{result}\"");
                }
            }
            else
            {
                TxtStatus.Text = "❌ Xuất thất bại.";
                MessageBox.Show(
                    "Xuất IFC không thành công.\n\nCó thể do:\n" +
                    "• View không hợp lệ\n" +
                    "• Đường dẫn không có quyền ghi\n" +
                    "• Model chưa được lưu",
                    "Lỗi xuất IFC",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"❌ Lỗi: {ex.Message}";
            MessageBox.Show($"❌ Lỗi xuất IFC:\n\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnExport.IsEnabled = true;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
