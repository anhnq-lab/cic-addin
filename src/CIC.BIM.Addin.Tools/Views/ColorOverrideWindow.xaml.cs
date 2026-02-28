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
    private readonly List<ColorOverrideService.CategoryColorInfo> _categories;
    private readonly List<CheckBox> _checkBoxes = new();

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

    private void BuildCategoryList()
    {
        PanelCategories.Children.Clear();
        _checkBoxes.Clear();

        for (int i = 0; i < _categories.Count; i++)
        {
            var info = _categories[i];
            var row = CreateCategoryRow(info, i);
            PanelCategories.Children.Add(row);
        }
    }

    private Border CreateCategoryRow(ColorOverrideService.CategoryColorInfo info, int index)
    {
        var grid = new System.Windows.Controls.Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        var cb = new CheckBox
        {
            IsChecked = info.IsEnabled,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = index
        };
        cb.Checked += (s, e) => info.IsEnabled = true;
        cb.Unchecked += (s, e) => info.IsEnabled = false;
        _checkBoxes.Add(cb);
        System.Windows.Controls.Grid.SetColumn(cb, 0);
        grid.Children.Add(cb);

        var colorRect = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(info.Color.Red, info.Color.Green, info.Color.Blue)),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Click để đổi màu",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = index
        };
        colorRect.MouseLeftButtonDown += ColorSwatch_Click;
        System.Windows.Controls.Grid.SetColumn(colorRect, 1);
        grid.Children.Add(colorRect);

        var nameText = new TextBlock
        {
            Text = info.CategoryName,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        System.Windows.Controls.Grid.SetColumn(nameText, 2);
        grid.Children.Add(nameText);

        var countText = new TextBlock
        {
            Text = info.ElementCount.ToString(),
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

    private void ColorSwatch_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Border swatch || swatch.Tag is not int index) return;

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
                    _categories[index].Color = newColor;
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

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = ColorOverrideService.ApplyColorOverrides(_doc, _activeView, _categories);
            Applied = true;

            // Force refresh view
            try { _uiDoc.RefreshActiveView(); } catch { }

            TxtStatus.Text = $"✅ Đã tô màu {result.CategoriesApplied} categories ({result.ElementsColored} elements).";
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
            int resetCount = ColorOverrideService.ResetOverrides(_doc, _activeView, _categories);

            try { _uiDoc.RefreshActiveView(); } catch { }

            TxtStatus.Text = $"↺ Đã reset {resetCount} categories về màu gốc.";
        }
        catch (Exception ex)
        {
            TaskDialog.Show("CIC Tools - Lỗi reset", $"Lỗi: {ex.Message}\n\n{ex.StackTrace}");
            TxtStatus.Text = $"❌ Lỗi: {ex.Message}";
        }
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var cb in _checkBoxes)
            cb.IsChecked = true;
    }

    private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var cb in _checkBoxes)
            cb.IsChecked = false;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
