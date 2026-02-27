using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Tools.Services;
using static CIC.BIM.Addin.Tools.Services.AutoJointService;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfGrid = System.Windows.Controls.Grid;

namespace CIC.BIM.Addin.Tools.Views;

public partial class AutoJointWindow : Window
{
    private readonly Document? _doc;
    private readonly UIDocument? _uiDoc;
    private readonly List<RuleRow> _ruleRows = new();

    /// <summary>Nếu scope = PickRegion, cần đóng window trước khi pick.</summary>
    public JoinScope SelectedScope { get; private set; }
    public List<JoinRule> Rules => _ruleRows.Select(r => r.ToJoinRule()).Where(r => r != null).ToList()!;
    public bool ExecuteJoin { get; private set; }
    public bool ExecuteUnjoin { get; private set; }

    public AutoJointWindow(Document? doc = null, UIDocument? uiDoc = null)
    {
        InitializeComponent();
        _doc = doc;
        _uiDoc = uiDoc;
        LoadDefaultRules();
    }

    // ═══ Quản lý quy tắc ═══

    private void LoadDefaultRules()
    {
        PanelRules.Children.Clear();
        _ruleRows.Clear();
        foreach (var rule in DefaultRules)
            AddRuleRow(rule);
    }

    private void AddRuleRow(JoinRule? rule = null)
    {
        var row = new RuleRow(rule, SupportedCategories, this);
        row.OnDelete += () =>
        {
            PanelRules.Children.Remove(row.Container);
            _ruleRows.Remove(row);
        };

        _ruleRows.Add(row);
        PanelRules.Children.Add(row.Container);
    }

    private void BtnAddRule_Click(object sender, RoutedEventArgs e) => AddRuleRow();
    private void BtnResetRules_Click(object sender, RoutedEventArgs e) => LoadDefaultRules();

    // ═══ Scope ═══

    private JoinScope GetScope()
    {
        if (RdCurrentView.IsChecked == true) return JoinScope.CurrentView;
        if (RdEntireProject.IsChecked == true) return JoinScope.EntireProject;
        if (RdSelected.IsChecked == true) return JoinScope.SelectedElements;
        if (RdPickRegion.IsChecked == true) return JoinScope.PickRegion;
        return JoinScope.CurrentView;
    }

    // ═══ Join / Switch Join ═══

    private void BtnJoin_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null || _uiDoc == null) return;

        var rules = Rules;
        if (rules.Count == 0)
        {
            MessageBox.Show("Chưa có quy tắc nào. Thêm ít nhất 1 quy tắc ưu tiên.",
                "Thiếu quy tắc", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var scope = GetScope();

        // Nếu PickRegion → đóng window, pick, xử lý, mở lại
        if (scope == JoinScope.PickRegion)
        {
            SelectedScope = scope;
            ExecuteJoin = true;
            DialogResult = true;
            Close();
            return;
        }

        ExecuteJoinLogic(rules, scope);
    }

    public void ExecuteJoinLogic(List<JoinRule> rules, JoinScope scope)
    {
        if (_doc == null || _uiDoc == null) return;

        // Thu thập categories từ rules
        var categories = rules
            .SelectMany(r => new[] { r.CuttingCategory, r.CutCategory })
            .Distinct()
            .ToList();

        TxtStatus.Text = "⏳ Đang thu thập cấu kiện...";
        BtnJoin.IsEnabled = false;
        BtnUnjoin.IsEnabled = false;

        var elements = CollectByScope(_doc, _uiDoc, scope, categories);

        if (elements.Count == 0)
        {
            TxtStatus.Text = "⚠ Không tìm thấy cấu kiện nào trong phạm vi đã chọn.";
            BtnJoin.IsEnabled = true;
            BtnUnjoin.IsEnabled = true;
            return;
        }

        TxtStatus.Text = $"⏳ Đang xử lý {elements.Count} cấu kiện...";

        var result = JoinAndSwitch(_doc, rules, elements, (current, total) =>
        {
            if (total > 0)
            {
                ProgressBar.Value = (double)current / total * 100;
            }
        });

        ProgressBar.Value = 100;
        BtnJoin.IsEnabled = true;
        BtnUnjoin.IsEnabled = true;

        TxtStatus.Text = $"✅ Hoàn tất — Nối mới: {result.Joined} | Đổi TT: {result.Switched} | " +
                         $"Đầu dầm: {result.BeamEndsConnected} | Đã nối: {result.AlreadyJoined} | Lỗi: {result.Failed}";

        if (result.Errors.Count > 0)
        {
            var msg = string.Join("\n", result.Errors.Take(5));
            if (result.Errors.Count > 5)
                msg += $"\n... và {result.Errors.Count - 5} lỗi khác";
            MessageBox.Show(msg, "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ═══ UnJoin ═══

    private void BtnUnjoin_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null || _uiDoc == null) return;

        var rules = Rules;
        if (rules.Count == 0)
        {
            MessageBox.Show("Chưa có quy tắc nào.", "Thiếu quy tắc", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var scope = GetScope();

        if (scope == JoinScope.PickRegion)
        {
            SelectedScope = scope;
            ExecuteUnjoin = true;
            DialogResult = true;
            Close();
            return;
        }

        ExecuteUnjoinLogic(rules, scope);
    }

    public void ExecuteUnjoinLogic(List<JoinRule> rules, JoinScope scope)
    {
        if (_doc == null || _uiDoc == null) return;

        var categories = rules
            .SelectMany(r => new[] { r.CuttingCategory, r.CutCategory })
            .Distinct()
            .ToList();

        TxtStatus.Text = "⏳ Đang thu thập cấu kiện...";
        BtnJoin.IsEnabled = false;
        BtnUnjoin.IsEnabled = false;

        var elements = CollectByScope(_doc, _uiDoc, scope, categories);

        if (elements.Count == 0)
        {
            TxtStatus.Text = "⚠ Không tìm thấy cấu kiện nào.";
            BtnJoin.IsEnabled = true;
            BtnUnjoin.IsEnabled = true;
            return;
        }

        TxtStatus.Text = $"⏳ Đang bỏ nối {elements.Count} cấu kiện...";

        var result = UnjoinAll(_doc, rules, elements, (current, total) =>
        {
            if (total > 0)
                ProgressBar.Value = (double)current / total * 100;
        });

        ProgressBar.Value = 100;
        BtnJoin.IsEnabled = true;
        BtnUnjoin.IsEnabled = true;

        TxtStatus.Text = $"✅ Đã bỏ nối: {result.Unjoined} cặp | Lỗi: {result.Failed}";
    }

    // ═══ Close ═══

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    #region Inner Class — RuleRow (dynamic UI row)

    /// <summary>Một hàng quy tắc trong UI: [ComboBox cắt] ↔ [ComboBox bị cắt] [Xóa]</summary>
    public class RuleRow
    {
        public Border Container { get; }
        public WpfComboBox CboCutting { get; }
        public WpfComboBox CboCut { get; }
        public event Action? OnDelete;

        private readonly (BuiltInCategory Cat, string Name)[] _categories;
        private readonly Window _owner;

        public RuleRow(JoinRule? rule, (BuiltInCategory Cat, string Name)[] categories, Window owner)
        {
            _categories = categories;
            _owner = owner;

            Container = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x31, 0x32, 0x44)),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(8, 4, 4, 4)
            };

            var grid = new WpfGrid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            // ComboBox: Đối tượng cắt
            CboCutting = CreateComboBox();
            WpfGrid.SetColumn(CboCutting, 0);
            grid.Children.Add(CboCutting);

            // Arrow button — click để đổi chiều ưu tiên
            var arrow = new Button
            {
                Content = "⇄",
                FontSize = 16,
                Width = 40,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x89, 0xB4, 0xFA)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Đổi chiều ưu tiên"
            };
            arrow.Click += (s, e) =>
            {
                int idxCutting = CboCutting.SelectedIndex;
                int idxCut = CboCut.SelectedIndex;
                CboCutting.SelectedIndex = idxCut;
                CboCut.SelectedIndex = idxCutting;
            };
            WpfGrid.SetColumn(arrow, 1);
            grid.Children.Add(arrow);

            // ComboBox: Đối tượng bị cắt
            CboCut = CreateComboBox();
            WpfGrid.SetColumn(CboCut, 2);
            grid.Children.Add(CboCut);

            // Delete button
            var btnDel = new Button
            {
                Content = "✕",
                FontSize = 11,
                Width = 22, Height = 22,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            btnDel.Click += (s, e) => OnDelete?.Invoke();
            WpfGrid.SetColumn(btnDel, 3);
            grid.Children.Add(btnDel);

            Container.Child = grid;

            // Preset values
            if (rule != null)
            {
                SelectCategory(CboCutting, rule.CuttingCategory);
                SelectCategory(CboCut, rule.CutCategory);
            }
        }

        private WpfComboBox CreateComboBox()
        {
            var cbo = new WpfComboBox
            {
                FontSize = 12,
                Margin = new Thickness(2),
            };

            // Áp dụng DarkComboBox style từ ToolStyles (merged vào Window resources)
            var style = _owner.TryFindResource("DarkComboBox") as Style;
            if (style != null)
                cbo.Style = style;

            foreach (var (cat, name) in _categories)
            {
                cbo.Items.Add(new WpfComboBoxItem
                {
                    Content = name,
                    Tag = cat,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCD, 0xD6, 0xF4))
                });
            }

            return cbo;
        }

        private void SelectCategory(WpfComboBox cbo, BuiltInCategory cat)
        {
            for (int i = 0; i < cbo.Items.Count; i++)
            {
                if (cbo.Items[i] is WpfComboBoxItem item && item.Tag is BuiltInCategory c && c == cat)
                {
                    cbo.SelectedIndex = i;
                    break;
                }
            }
        }

        public JoinRule? ToJoinRule()
        {
            if (CboCutting.SelectedItem is not WpfComboBoxItem cuttingItem ||
                CboCut.SelectedItem is not WpfComboBoxItem cutItem)
                return null;

            return new JoinRule
            {
                CuttingCategory = (BuiltInCategory)cuttingItem.Tag,
                CutCategory = (BuiltInCategory)cutItem.Tag,
                CuttingName = cuttingItem.Content.ToString() ?? "",
                CutName = cutItem.Content.ToString() ?? ""
            };
        }
    }

    #endregion
}
