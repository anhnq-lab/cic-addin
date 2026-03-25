using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CIC.BIM.Addin.Tools.Services;

namespace CIC.BIM.Addin.Tools.Views;

/// <summary>
/// ViewModel cho mỗi dòng block trong DataGrid.
/// </summary>
public class BlockRow : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _blockName = "";
    private string _layerName = "";
    private int _count;
    private ElementId _familySymbolId = ElementId.InvalidElementId;
    private string _familyDisplayName = "";

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
    }

    public string BlockName
    {
        get => _blockName;
        set { _blockName = value; OnPropertyChanged(nameof(BlockName)); }
    }

    public string LayerName
    {
        get => _layerName;
        set { _layerName = value; OnPropertyChanged(nameof(LayerName)); }
    }

    public int Count
    {
        get => _count;
        set { _count = value; OnPropertyChanged(nameof(Count)); }
    }

    public ElementId FamilySymbolId
    {
        get => _familySymbolId;
        set { _familySymbolId = value; OnPropertyChanged(nameof(FamilySymbolId)); }
    }

    public string FamilyDisplayName
    {
        get => _familyDisplayName;
        set { _familyDisplayName = value; OnPropertyChanged(nameof(FamilyDisplayName)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class BlockCadWindow : Window
{
    private readonly Document _doc;
    private readonly ObservableCollection<BlockRow> _allBlocks = new();
    private readonly ObservableCollection<BlockRow> _filteredBlocks = new();
    private string? _dwgFilePath;

    // Category → Family → Type data
    private readonly List<CategoryItem> _categories = new();
    private readonly Dictionary<string, List<FamilyItem>> _familiesByCategory = new();
    private readonly Dictionary<string, List<FamilyTypeItem>> _typesByFamily = new();

    /// <summary>Config sau khi user bấm Run.</summary>
    public BlockCadConfig? Config { get; private set; }

    public BlockCadWindow(Document doc)
    {
        _doc = doc;
        InitializeComponent();

        MappingGrid.ItemsSource = _filteredBlocks;

        LoadCadLinks();
        LoadLevels();
        BuildFamilyTree();
    }

    // ═══════ MODELS cho ComboBox ═══════

    private class CategoryItem
    {
        public BuiltInCategory BIC { get; set; }
        public string Name { get; set; } = "";
        public override string ToString() => Name;
    }

    private class FamilyItem
    {
        public string FamilyName { get; set; } = "";
        public override string ToString() => FamilyName;
    }

    // ═══════ LOAD DATA ═══════

    private void LoadCadLinks()
    {
        var links = BlockCadService.ScanCadLinks(_doc);
        CboCadLink.Items.Clear();
        foreach (var link in links)
            CboCadLink.Items.Add(new CadLinkItem(link.Id, link.FileName, link.RevitLinkInstanceId));
        if (CboCadLink.Items.Count > 0)
            CboCadLink.SelectedIndex = 0;
        else
            TxtPreview.Text = "⚠ Không tìm thấy file CAD link nào.";
    }

    private void LoadLevels()
    {
        var levels = new FilteredElementCollector(_doc)
            .OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(l => l.Elevation).ToList();
        CboLevel.Items.Clear();
        foreach (var level in levels)
            CboLevel.Items.Add(new LevelItem(level.Id, level.Name));
        if (CboLevel.Items.Count > 0)
            CboLevel.SelectedIndex = 0;
    }

    /// <summary>
    /// Build cây Category → Family → Type cho bộ chọn bên phải.
    /// </summary>
    private void BuildFamilyTree()
    {
        var catDefs = new (BuiltInCategory bic, string name)[]
        {
            (BuiltInCategory.OST_LightingFixtures, "Lighting Fixtures"),
            (BuiltInCategory.OST_ElectricalFixtures, "Electrical Fixtures"),
            (BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment"),
            (BuiltInCategory.OST_CommunicationDevices, "Communication Devices"),
            (BuiltInCategory.OST_DataDevices, "Data Devices"),
            (BuiltInCategory.OST_FireAlarmDevices, "Fire Alarm Devices"),
            (BuiltInCategory.OST_LightingDevices, "Lighting Devices"),
            (BuiltInCategory.OST_NurseCallDevices, "Nurse Call Devices"),
            (BuiltInCategory.OST_SecurityDevices, "Security Devices"),
            (BuiltInCategory.OST_TelephoneDevices, "Telephone Devices"),
            (BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment"),
            (BuiltInCategory.OST_PlumbingFixtures, "Plumbing Fixtures"),
            (BuiltInCategory.OST_Sprinklers, "Sprinklers"),
            (BuiltInCategory.OST_DuctTerminal, "Duct Terminals"),
            (BuiltInCategory.OST_DuctAccessory, "Duct Accessories"),
            (BuiltInCategory.OST_PipeAccessory, "Pipe Accessories"),
            (BuiltInCategory.OST_GenericModel, "Generic Models"),
        };

        CboCategory.Items.Clear();
        foreach (var (bic, name) in catDefs)
        {
            try
            {
                var symbols = new FilteredElementCollector(_doc)
                    .OfCategory(bic).OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>().ToList();

                if (symbols.Count == 0) continue;

                var catItem = new CategoryItem { BIC = bic, Name = name };
                _categories.Add(catItem);
                CboCategory.Items.Add(catItem);

                // Group by family name
                var familiesByName = symbols.GroupBy(s => s.FamilyName).OrderBy(g => g.Key);
                var familyItems = new List<FamilyItem>();

                foreach (var famGroup in familiesByName)
                {
                    var famItem = new FamilyItem { FamilyName = famGroup.Key };
                    familyItems.Add(famItem);

                    _typesByFamily[famGroup.Key] = famGroup
                        .Select(s => new FamilyTypeItem(s.Id, s.FamilyName, s.Name))
                        .OrderBy(t => t.TypeName).ToList();
                }

                _familiesByCategory[name] = familyItems;
            }
            catch { }
        }

        if (CboCategory.Items.Count > 0)
            CboCategory.SelectedIndex = 0;
    }

    // ═══════ EVENT HANDLERS ═══════

    private void CboCadLink_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _allBlocks.Clear();
        _filteredBlocks.Clear();
        BtnRun.IsEnabled = false;
        TxtPreview.Text = "Bấm 🔍 Scan Blocks để quét danh sách block.";
        TxtBlockCount.Text = "";
    }

    private void BtnScanBlocks_Click(object sender, RoutedEventArgs e)
    {
        if (CboCadLink.SelectedItem is not CadLinkItem selectedLink)
        {
            MessageBox.Show("Vui lòng chọn file CAD link.", "Thông báo",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ScanBlocks(selectedLink);
    }

    private void ScanBlocks(CadLinkItem selectedLink)
    {
        _allBlocks.Clear();
        _filteredBlocks.Clear();
        BtnScanBlocks.IsEnabled = false;
        BtnScanBlocks.Content = "⏳ Đang quét...";

        try
        {
            List<CadBlockInfo> blocks;
            _dwgFilePath = BlockCadService.GetDwgFilePath(_doc, selectedLink.Id, selectedLink.RevitLinkInstanceId);

            // 1. Ưu tiên JSON từ CIC CAD Plugin (dynamic block chính xác)
            var jsonBlocks = BlockCadService.TryLoadBlockJson(_dwgFilePath);
            if (jsonBlocks != null && jsonBlocks.Count > 0)
            {
                blocks = jsonBlocks;
                TxtPreview.Text = $"✅ Đọc từ CIC CAD Export — dynamic blocks chính xác";
            }
            // 2. Parse DWG bằng ACadSharp (block names đúng, dynamic block chia sẻ vị trí)
            else if (!string.IsNullOrEmpty(_dwgFilePath))
            {
                blocks = BlockCadService.ScanBlocksFromDwg(_dwgFilePath);
                TxtPreview.Text = $"✅ Đọc từ: {System.IO.Path.GetFileName(_dwgFilePath)}";
            }
            // 3. Fallback: Revit geometry (chỉ layer names)
            else
            {
                Document targetDoc = _doc;
                if (selectedLink.RevitLinkInstanceId != null)
                {
                    var linkInst = _doc.GetElement(selectedLink.RevitLinkInstanceId) as RevitLinkInstance;
                    targetDoc = linkInst?.GetLinkDocument() ?? _doc;
                }
                blocks = BlockCadService.ScanBlocksFromRevit(targetDoc, selectedLink.Id);
                TxtPreview.Text = "⚠ Không tìm file DWG — scan từ Revit (chỉ layer names)";
            }

            foreach (var info in blocks)
            {
                var row = new BlockRow
                {
                    BlockName = info.BlockName,
                    LayerName = info.LayerName,
                    Count = info.Count,
                    IsSelected = false // Mặc định không tick — chỉ tick khi gán Family
                };
                _allBlocks.Add(row);
                _filteredBlocks.Add(row);
            }

            TxtBlockCount.Text = $"{blocks.Count} blocks, {blocks.Sum(b => b.Count)} instances";
            UpdateSummary();
            BtnRun.IsEnabled = blocks.Count > 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi scan: {ex.Message}", "Lỗi",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnScanBlocks.IsEnabled = true;
            BtnScanBlocks.Content = "🔍 Scan Blocks";
        }
    }

    private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        var filter = TxtFilter.Text?.Trim().ToLowerInvariant() ?? "";
        _filteredBlocks.Clear();
        foreach (var row in _allBlocks)
        {
            if (string.IsNullOrEmpty(filter) ||
                row.BlockName.ToLowerInvariant().Contains(filter) ||
                row.LayerName.ToLowerInvariant().Contains(filter))
            {
                _filteredBlocks.Add(row);
            }
        }
    }

    // ═══════ CATEGORY → FAMILY → TYPE CASCADING ═══════

    private void CboCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CboFamily.Items.Clear();
        CboType.Items.Clear();
        BtnAssign.IsEnabled = false;
        ImgPreview.Source = null;
        TxtNoPreview.Visibility = System.Windows.Visibility.Visible;

        if (CboCategory.SelectedItem is not CategoryItem catItem) return;

        if (_familiesByCategory.TryGetValue(catItem.Name, out var families))
        {
            foreach (var fam in families)
                CboFamily.Items.Add(fam);
            if (CboFamily.Items.Count > 0)
                CboFamily.SelectedIndex = 0;
        }
    }

    private void CboFamily_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CboType.Items.Clear();
        BtnAssign.IsEnabled = false;

        if (CboFamily.SelectedItem is not FamilyItem famItem) return;

        if (_typesByFamily.TryGetValue(famItem.FamilyName, out var types))
        {
            foreach (var t in types)
                CboType.Items.Add(t);
            if (CboType.Items.Count > 0)
                CboType.SelectedIndex = 0;
        }
    }

    private void CboType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboType.SelectedItem is FamilyTypeItem typeItem)
        {
            BtnAssign.IsEnabled = MappingGrid.SelectedItem is BlockRow;
            UpdateFamilyPreview(typeItem.Id);
        }
        else
        {
            BtnAssign.IsEnabled = false;
        }
    }

    // ═══════ ASSIGN FAMILY TO BLOCK ═══════

    private void BtnAssign_Click(object sender, RoutedEventArgs e)
    {
        if (MappingGrid.SelectedItem is not BlockRow selectedRow) return;
        if (CboType.SelectedItem is not FamilyTypeItem typeItem) return;

        selectedRow.FamilySymbolId = typeItem.Id;
        selectedRow.FamilyDisplayName = typeItem.DisplayName;
        selectedRow.IsSelected = true; // Auto-tick khi gán Family

        UpdateSummary();

        // Auto-select next row that doesn't have a family assigned
        var currentIndex = _filteredBlocks.IndexOf(selectedRow);
        for (int i = currentIndex + 1; i < _filteredBlocks.Count; i++)
        {
            if (_filteredBlocks[i].IsSelected && _filteredBlocks[i].FamilySymbolId == ElementId.InvalidElementId)
            {
                MappingGrid.SelectedIndex = i;
                MappingGrid.ScrollIntoView(_filteredBlocks[i]);
                break;
            }
        }
    }

    private void MappingGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MappingGrid.SelectedItem is not BlockRow row)
        {
            TxtSelectedBlock.Text = "— Chưa chọn block —";
            BtnAssign.IsEnabled = false;
            return;
        }

        TxtSelectedBlock.Text = $"Block: {row.BlockName}  ({row.Count} instances)";
        BtnAssign.IsEnabled = CboType.SelectedItem is FamilyTypeItem;

        // Nếu block đã gán family, hiện preview + select lại combo
        if (row.FamilySymbolId != ElementId.InvalidElementId)
        {
            UpdateFamilyPreview(row.FamilySymbolId);
            // Tự động chọn lại Category/Family/Type tương ứng
            SelectFamilyInCombos(row.FamilySymbolId);
        }
    }

    /// <summary>
    /// Khi chọn block đã gán family, auto-select lại đúng Category/Family/Type trong combo.
    /// </summary>
    private void SelectFamilyInCombos(ElementId symbolId)
    {
        var symbol = _doc.GetElement(symbolId) as FamilySymbol;
        if (symbol == null) return;

        // Tìm Category
        for (int i = 0; i < CboCategory.Items.Count; i++)
        {
            if (CboCategory.Items[i] is CategoryItem catItem)
            {
                try
                {
                    if ((int)catItem.BIC == symbol.Category?.Id?.Value)
                    {
                        CboCategory.SelectedIndex = i;
                        break;
                    }
                }
                catch { }
            }
        }

        // Tìm Family
        for (int i = 0; i < CboFamily.Items.Count; i++)
        {
            if (CboFamily.Items[i] is FamilyItem famItem && famItem.FamilyName == symbol.FamilyName)
            {
                CboFamily.SelectedIndex = i;
                break;
            }
        }

        // Tìm Type
        for (int i = 0; i < CboType.Items.Count; i++)
        {
            if (CboType.Items[i] is FamilyTypeItem typeItem && typeItem.Id == symbolId)
            {
                CboType.SelectedIndex = i;
                break;
            }
        }
    }

    private void UpdateFamilyPreview(ElementId symbolId)
    {
        if (symbolId == ElementId.InvalidElementId)
        {
            ImgPreview.Source = null;
            TxtNoPreview.Visibility = System.Windows.Visibility.Visible;
            return;
        }

        var symbol = _doc.GetElement(symbolId) as FamilySymbol;
        if (symbol == null) return;

        try
        {
            var bitmap = symbol.GetPreviewImage(new System.Drawing.Size(200, 200));
            if (bitmap != null)
            {
                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Seek(0, SeekOrigin.Begin);
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = ms;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();
                    ImgPreview.Source = bitmapImage;
                    TxtNoPreview.Visibility = System.Windows.Visibility.Collapsed;
                }
            }
            else
            {
                ImgPreview.Source = null;
                TxtNoPreview.Visibility = System.Windows.Visibility.Visible;
            }
        }
        catch
        {
            ImgPreview.Source = null;
            TxtNoPreview.Visibility = System.Windows.Visibility.Visible;
        }
    }

    private void UpdateSummary()
    {
        var assigned = _allBlocks.Where(b => b.IsSelected && b.FamilySymbolId != ElementId.InvalidElementId).ToList();
        var totalSelected = _allBlocks.Count(b => b.IsSelected);

        if (assigned.Count == 0)
        {
            TxtPreview.Text = $"{totalSelected} block types đã chọn — hãy gán Family cho từng block.";
        }
        else
        {
            var totalInstances = assigned.Sum(b => b.Count);
            TxtPreview.Text = $"Sẽ đặt: {totalInstances} thiết bị từ {assigned.Count}/{totalSelected} block types đã gán Family.";
        }
    }

    // ═══════ PICKING LOGIC ═══════

    private void BtnPickCad_Click(object sender, RoutedEventArgs e)
    {
        var uiDoc = new UIDocument(_doc);
        this.Hide();
        try
        {
            var picked = uiDoc.Selection.PickObject(ObjectType.Element, "Hãy chọn file CAD link hoặc Revit link chứa CAD...");
            if (picked != null)
            {
                var elem = _doc.GetElement(picked.ElementId);
                if (elem is ImportInstance import)
                {
                    var name = import.IsLinked ? (_doc.GetElement(import.GetTypeId())?.Name ?? import.Name) : import.Name;
                    var item = new CadLinkItem(import.Id, name);
                    CboCadLink.SelectedItem = item ?? CboCadLink.Items.Cast<CadLinkItem>().FirstOrDefault(i => i.Id == import.Id);
                    ScanBlocks(item);
                }
                else if (elem is RevitLinkInstance linkInst)
                {
                    var linkDoc = linkInst.GetLinkDocument();
                    if (linkDoc != null)
                    {
                        var linkImports = new FilteredElementCollector(linkDoc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();
                        if (linkImports.Count == 1)
                        {
                            var importInLink = linkImports[0];
                            var name = $"[{linkInst.Name}] " + (linkDoc.GetElement(importInLink.GetTypeId())?.Name ?? importInLink.Name);
                            var item = new CadLinkItem(importInLink.Id, name, linkInst.Id);
                            CboCadLink.SelectedItem = item;
                            ScanBlocks(item);
                        }
                        else if (linkImports.Count > 1)
                        {
                            MessageBox.Show("Revit Link chứa nhiều CAD link. Vui lòng chọn trong dropdown.", "Thông báo");
                        }
                    }
                }
            }
        }
        catch { }
        finally { this.Show(); }
    }

    private void BtnPickBlock_Click(object sender, RoutedEventArgs e)
    {
        if (CboCadLink.SelectedItem is not CadLinkItem) return;

        var uiDoc = new UIDocument(_doc);
        this.Hide();
        try
        {
            var picked = uiDoc.Selection.PickObject(ObjectType.PointOnElement, "Bấm vào 1 block trong file CAD...");
            if (picked != null)
            {
                Element targetElem = _doc.GetElement(picked.ElementId);
                var geo = targetElem.GetGeometryObjectFromReference(picked);
                if (geo != null)
                {
                    var styleId = geo.GraphicsStyleId;
                    if (styleId != ElementId.InvalidElementId)
                    {
                        var style = targetElem.Document.GetElement(styleId) as GraphicsStyle;
                        var layerName = style?.GraphicsStyleCategory?.Name;
                        if (!string.IsNullOrEmpty(layerName))
                            TxtFilter.Text = layerName;
                    }
                }
            }
        }
        catch { }
        finally { this.Show(); }
    }

    // ═══════ RUN ═══════

    private void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        if (CboCadLink.SelectedItem is not CadLinkItem selectedLink) return;
        if (CboLevel.SelectedItem is not LevelItem selectedLevel) return;

        var mappings = _allBlocks
            .Where(b => b.IsSelected && b.FamilySymbolId != ElementId.InvalidElementId)
            .Select(b => new BlockMapping
            {
                BlockName = b.BlockName,
                FamilySymbolId = b.FamilySymbolId
            })
            .ToList();

        if (mappings.Count == 0)
        {
            MessageBox.Show("Chưa gán Family cho block nào.\n\n" +
                "1. Chọn block trong danh sách\n" +
                "2. Chọn Category → Family → Type bên phải\n" +
                "3. Bấm ✅ Gán Family",
                "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Config = new BlockCadConfig
        {
            CadLinkId = selectedLink.Id,
            RevitLinkInstanceId = selectedLink.RevitLinkInstanceId,
            LevelId = selectedLevel.Id,
            Elevation = double.TryParse(TxtElevation.Text, out var elev) ? elev : 0,
            IncludeRotation = ChkRotation.IsChecked == true,
            RotationOffset = double.TryParse(TxtRotationOffset.Text, out var rotOff) ? rotOff : 0,
            PlacementMode = BlockPlacementMode.PlaceOnLevel,
            Mappings = mappings,
            DwgFilePath = _dwgFilePath
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
