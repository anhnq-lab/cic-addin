using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using CIC.BIM.Addin.Tools.Services;

namespace CIC.BIM.Addin.Tools.Views;

/// <summary>
/// ViewModel cho mỗi dòng trong mapping grid.
/// </summary>
public class CadLayerRow : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _layerName = "";
    private int _lineCount;
    private int _blockCount;
    private RevitObjectType _objectType = RevitObjectType.Ignore;
    private string _revitTypeName = "";
    private double _size;

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
    }

    public string LayerName
    {
        get => _layerName;
        set { _layerName = value; OnPropertyChanged(nameof(LayerName)); }
    }

    public int LineCount
    {
        get => _lineCount;
        set { _lineCount = value; OnPropertyChanged(nameof(LineCount)); }
    }

    public int BlockCount
    {
        get => _blockCount;
        set { _blockCount = value; OnPropertyChanged(nameof(BlockCount)); }
    }

    public RevitObjectType ObjectType
    {
        get => _objectType;
        set { _objectType = value; OnPropertyChanged(nameof(ObjectType)); }
    }

    public string RevitTypeName
    {
        get => _revitTypeName;
        set { _revitTypeName = value; OnPropertyChanged(nameof(RevitTypeName)); }
    }

    public double Size
    {
        get => _size;
        set { _size = value; OnPropertyChanged(nameof(Size)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Helper record cho Level ComboBox items.
/// </summary>
public record LevelItem(ElementId Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Helper record cho CAD Link ComboBox items.
/// </summary>
public record CadLinkItem(ElementId Id, string FileName)
{
    public override string ToString() => FileName;
}

public partial class CadAutoDrawWindow : Window
{
    private readonly Document _doc;
    private readonly ObservableCollection<CadLayerRow> _layers = new();

    /// <summary>
    /// Config sau khi user bấm Run.
    /// </summary>
    public CadAutoDrawConfig? Config { get; private set; }

    public CadAutoDrawWindow(Document doc)
    {
        _doc = doc;
        InitializeComponent();

        // Populate ObjectType enum vào ComboBox column
        ColObjectType.ItemsSource = Enum.GetValues(typeof(RevitObjectType));

        MappingGrid.ItemsSource = _layers;

        LoadCadLinks();
        LoadLevels();
    }

    // ══════════ LOAD DATA ══════════

    private void LoadCadLinks()
    {
        var links = CadAutoDrawService.ScanCadLinks(_doc);

        CboCadLink.Items.Clear();
        foreach (var link in links)
            CboCadLink.Items.Add(new CadLinkItem(link.Id, link.FileName));

        if (CboCadLink.Items.Count > 0)
            CboCadLink.SelectedIndex = 0;
        else
            TxtPreview.Text = "⚠ Không tìm thấy file CAD link nào. Hãy Insert → Link CAD trước.";
    }

    private void LoadLevels()
    {
        var levels = new FilteredElementCollector(_doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .ToList();

        CboLevel.Items.Clear();
        foreach (var level in levels)
            CboLevel.Items.Add(new LevelItem(level.Id, level.Name));

        if (CboLevel.Items.Count > 0)
            CboLevel.SelectedIndex = 0;
    }

    // ══════════ EVENT HANDLERS ══════════

    private void CboCadLink_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Reset layer list when CAD link changes
        _layers.Clear();
        BtnRun.IsEnabled = false;
        TxtPreview.Text = "Bấm 🔍 Scan Layers để quét danh sách layer.";
        TxtLayerCount.Text = "";
    }

    private void BtnScanLayers_Click(object sender, RoutedEventArgs e)
    {
        if (CboCadLink.SelectedItem is not CadLinkItem selectedLink)
        {
            MessageBox.Show("Vui lòng chọn file CAD link.", "Thông báo",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _layers.Clear();
        BtnScanLayers.IsEnabled = false;
        BtnScanLayers.Content = "⏳ Đang quét...";

        try
        {
            var layerInfos = CadAutoDrawService.ScanLayers(_doc, selectedLink.Id);

            foreach (var info in layerInfos)
            {
                _layers.Add(new CadLayerRow
                {
                    LayerName = info.LayerName,
                    LineCount = info.LineCount,
                    BlockCount = info.BlockCount,
                    IsSelected = info.LineCount > 0 || info.BlockCount > 0,
                    ObjectType = RevitObjectType.Ignore,
                    Size = 0
                });
            }

            TxtLayerCount.Text = $"{layerInfos.Count} layers";
            UpdatePreview();
            BtnRun.IsEnabled = layerInfos.Count > 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi scan: {ex.Message}", "Lỗi",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnScanLayers.IsEnabled = true;
            BtnScanLayers.Content = "🔍 Scan Layers";
        }
    }

    /// <summary>
    /// Load danh sách Revit type khi user thay đổi ObjectType cho 1 layer.
    /// Xử lý thông qua DataGrid CellEditEnding event.
    /// </summary>
    private void UpdateRevitTypeOptions(CadLayerRow row)
    {
        if (row.ObjectType == RevitObjectType.Ignore)
        {
            row.RevitTypeName = "";
            return;
        }

        var types = CadAutoDrawService.GetAvailableTypes(_doc, row.ObjectType);
        if (types.Count > 0 && string.IsNullOrEmpty(row.RevitTypeName))
        {
            row.RevitTypeName = types.First();
        }

        // Cập nhật ColRevitType ItemsSource cho row hiện tại
        // (WPF DataGrid ComboBox column - ItemsSource phải cập nhật)
        ColRevitType.ItemsSource = types;
    }

    private void UpdatePreview()
    {
        var selected = _layers.Where(l => l.IsSelected && l.ObjectType != RevitObjectType.Ignore).ToList();

        if (selected.Count == 0)
        {
            TxtPreview.Text = "Chọn layer và cấu hình đối tượng Revit để bắt đầu.";
            return;
        }

        var parts = selected
            .GroupBy(s => s.ObjectType)
            .Select(g => $"{g.Key}: {g.Sum(l => l.LineCount + l.BlockCount)} elements trên {g.Count()} layers")
            .ToList();

        TxtPreview.Text = $"Sẽ tạo: {string.Join(" | ", parts)}";
    }

    // ══════════ BUTTONS ══════════

    private void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        if (CboCadLink.SelectedItem is not CadLinkItem selectedLink)
        {
            MessageBox.Show("Chưa chọn file CAD link.", "Thông báo",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CboLevel.SelectedItem is not LevelItem selectedLevel)
        {
            MessageBox.Show("Chưa chọn Level.", "Thông báo",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var mappings = _layers
            .Where(l => l.IsSelected && l.ObjectType != RevitObjectType.Ignore)
            .Select(l => new CadLayerMapping
            {
                LayerName = l.LayerName,
                ObjectType = l.ObjectType,
                RevitTypeName = l.RevitTypeName,
                Size = l.Size
            })
            .ToList();

        if (mappings.Count == 0)
        {
            MessageBox.Show(
                "Chưa cấu hình mapping nào.\n\n" +
                "Hãy:\n" +
                "1. Tick ✓ layer muốn vẽ\n" +
                "2. Chọn loại đối tượng Revit (Wall, Beam...)\n" +
                "3. Chọn Revit Type tương ứng",
                "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Config = new CadAutoDrawConfig
        {
            CadLinkId = selectedLink.Id,
            LevelId = selectedLevel.Id,
            DefaultHeight = double.TryParse(TxtHeight.Text, out var h) ? h : 3000,
            Offset = double.TryParse(TxtOffset.Text, out var o) ? o : 0,
            Mappings = mappings
        };

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
