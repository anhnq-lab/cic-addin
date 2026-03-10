using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Tools.Services;

namespace CIC.BIM.Addin.Tools.Views;

public partial class ColorOverrideWindow : Window
{
    private readonly Document _doc;
    private readonly UIDocument _uiDoc;
    private readonly View _activeView;

    // Mode: By Category
    private List<ColorOverrideService.CategoryColorInfo> _categories;
    private readonly List<CheckBox> _checkBoxes = new();

    // Mode: By Parameter
    private List<ColorOverrideService.ParameterValueColorInfo>? _paramGroups;
    private readonly List<CheckBox> _paramCheckBoxes = new();

    private bool _isByParameter = false;

    public bool Applied { get; private set; }

    public ColorOverrideWindow(Document doc, UIDocument uiDoc)
    {
        InitializeComponent();
        _doc = doc;
        _uiDoc = uiDoc;
        _activeView = doc.ActiveView;
        _categories = ColorOverrideService.GetCategoriesInView(doc, _activeView);
        BuildCategoryList();
        TxtStatus.Text = $"Tìm thấy {_categories.Count} categories trong view \"{_activeView.Name}\".";
    }

    // ═══ Mode Toggle ═══

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (PnlParamSelect == null) return;

        _isByParameter = RbByParameter.IsChecked == true;
        PnlParamSelect.Visibility = _isByParameter ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        TxtColHeader.Text = _isByParameter ? "Giá trị" : "Category";

        if (_isByParameter)
        {
            // Load parameter names
            var paramNames = ColorOverrideService.GetParameterNamesInView(_doc, _activeView);
            CboParameter.ItemsSource = paramNames;
            if (paramNames.Count > 0)
                CboParameter.SelectedIndex = 0;
            else
            {
                PanelCategories.Children.Clear();
                TxtStatus.Text = "Không tìm thấy parameter nào trong view.";
            }
        }
        else
        {
            BuildCategoryList();
            TxtStatus.Text = $"Tìm thấy {_categories.Count} categories.";
        }
    }

    private void CboParameter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboParameter.SelectedItem == null) return;
        var paramName = CboParameter.SelectedItem as string ?? "";
        if (string.IsNullOrEmpty(paramName)) return;

        _paramGroups = ColorOverrideService.GetElementsByParameterValue(_doc, _activeView, paramName);
        BuildParameterValueList();
        TxtStatus.Text = $"Parameter \"{paramName}\": {_paramGroups.Count} giá trị khác nhau.";
    }

    // ═══ Build Lists ═══

    private void BuildCategoryList()
    {
        PanelCategories.Children.Clear();
        _checkBoxes.Clear();

        for (int i = 0; i < _categories.Count; i++)
        {
            var info = _categories[i];
            var row = CreateRow(info.CategoryName, info.ElementCount, info.Color, info.IsEnabled, i,
                (idx, enabled) => _categories[idx].IsEnabled = enabled,
                (idx, color) => _categories[idx].Color = color);
            PanelCategories.Children.Add(row);
        }
    }

    private void BuildParameterValueList()
    {
        PanelCategories.Children.Clear();
        _paramCheckBoxes.Clear();

        if (_paramGroups == null) return;

        for (int i = 0; i < _paramGroups.Count; i++)
        {
            var group = _paramGroups[i];
            var row = CreateRow(group.ParameterValue, group.ElementCount, group.Color, group.IsEnabled, i,
                (idx, enabled) => { if (_paramGroups != null) _paramGroups[idx].IsEnabled = enabled; },
                (idx, color) => { if (_paramGroups != null) _paramGroups[idx].Color = color; });
            PanelCategories.Children.Add(row);
        }
    }

    /// <summary>
    /// Tạo 1 dòng trong danh sách (dùng chung cho cả Category và Parameter mode).
    /// </summary>
    private Border CreateRow(string name, int count, Autodesk.Revit.DB.Color color, bool enabled,
        int index, Action<int, bool> onToggle, Action<int, Autodesk.Revit.DB.Color> onColorChange)
    {
        var grid = new System.Windows.Controls.Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        var cb = new CheckBox
        {
            IsChecked = enabled,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = index
        };
        cb.Checked += (s, e) => onToggle(index, true);
        cb.Unchecked += (s, e) => onToggle(index, false);
        if (_isByParameter)
            _paramCheckBoxes.Add(cb);
        else
            _checkBoxes.Add(cb);
        System.Windows.Controls.Grid.SetColumn(cb, 0);
        grid.Children.Add(cb);

        var colorRect = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(color.Red, color.Green, color.Blue)),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Click để đổi màu",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = index
        };
        colorRect.MouseLeftButtonDown += (s, e) => ShowColorPopup(s as Border, index, onColorChange);
        System.Windows.Controls.Grid.SetColumn(colorRect, 1);
        grid.Children.Add(colorRect);

        var nameText = new TextBlock
        {
            Text = name,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        System.Windows.Controls.Grid.SetColumn(nameText, 2);
        grid.Children.Add(nameText);

        var countText = new TextBlock
        {
            Text = count.ToString(),
            FontSize = 12,
            Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xF9, 0xE2, 0xAF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Controls.Grid.SetColumn(countText, 3);
        grid.Children.Add(countText);

        var border = new Border
        {
            Child = grid,
            Padding = new Thickness(8, 5, 8, 5),
            Background = new SolidColorBrush(
                index % 2 == 0
                    ? System.Windows.Media.Color.FromRgb(0x31, 0x32, 0x44)
                    : System.Windows.Media.Color.FromRgb(0x2A, 0x2B, 0x3D)),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 1, 0, 1)
        };

        return border;
    }

    // ═══ Color Popup ═══

    private void ShowColorPopup(Border? swatch, int index, Action<int, Autodesk.Revit.DB.Color> onColorChange)
    {
        if (swatch == null) return;

        var popup = new Popup
        {
            PlacementTarget = swatch,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true
        };

        var wrapPanel = new WrapPanel
        {
            Width = 160,
            Background = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x31, 0x32, 0x44))
        };

        var popupBorder = new Border
        {
            Child = wrapPanel,
            BorderBrush = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x58, 0x5B, 0x70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Background = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x31, 0x32, 0x44))
        };

        foreach (var c in ColorOverrideService.DefaultPalette)
        {
            var colorBtn = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(2),
                Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(c.Red, c.Green, c.Blue)),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = c
            };

            colorBtn.MouseLeftButtonDown += (s2, e2) =>
            {
                if (s2 is Border btn && btn.Tag is Autodesk.Revit.DB.Color newColor)
                {
                    onColorChange(index, newColor);
                    swatch.Background = new SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(newColor.Red, newColor.Green, newColor.Blue));
                    popup.IsOpen = false;
                }
            };

            wrapPanel.Children.Add(colorBtn);
        }

        popup.Child = popupBorder;
        popup.IsOpen = true;
    }

    // ═══ Actions ═══

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isByParameter && _paramGroups != null)
            {
                var result = ColorOverrideService.ApplyParameterColorOverrides(_doc, _activeView, _paramGroups);
                Applied = true;
                try { _uiDoc.RefreshActiveView(); } catch { }
                TxtStatus.Text = $"✅ Đã tô màu {result.CategoriesApplied} nhóm ({result.ElementsColored} elements).";
            }
            else
            {
                var result = ColorOverrideService.ApplyColorOverrides(_doc, _activeView, _categories);
                Applied = true;
                try { _uiDoc.RefreshActiveView(); } catch { }
                TxtStatus.Text = $"✅ Đã tô màu {result.CategoriesApplied} categories ({result.ElementsColored} elements).";
            }
        }
        catch (Exception ex)
        {
            TaskDialog.Show("CIC Tools - Lỗi tô màu", $"Lỗi: {ex.Message}\n\n{ex.StackTrace}");
            TxtStatus.Text = $"❌ Lỗi: {ex.Message}";
        }
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isByParameter && _paramGroups != null)
            {
                int resetCount = ColorOverrideService.ResetElementOverrides(_doc, _activeView, _paramGroups);
                try { _uiDoc.RefreshActiveView(); } catch { }
                TxtStatus.Text = $"↺ Đã reset {resetCount} elements về màu gốc.";
            }
            else
            {
                int resetCount = ColorOverrideService.ResetOverrides(_doc, _activeView, _categories);
                try { _uiDoc.RefreshActiveView(); } catch { }
                TxtStatus.Text = $"↺ Đã reset {resetCount} categories về màu gốc.";
            }
        }
        catch (Exception ex)
        {
            TaskDialog.Show("CIC Tools - Lỗi reset", $"Lỗi: {ex.Message}\n\n{ex.StackTrace}");
            TxtStatus.Text = $"❌ Lỗi: {ex.Message}";
        }
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        var boxes = _isByParameter ? _paramCheckBoxes : _checkBoxes;
        foreach (var cb in boxes) cb.IsChecked = true;
    }

    private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        var boxes = _isByParameter ? _paramCheckBoxes : _checkBoxes;
        foreach (var cb in boxes) cb.IsChecked = false;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
