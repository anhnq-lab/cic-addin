using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace CIC.BIM.Addin.Tools.Views;

public enum FinishingTab
{
    WallFinish = 0,
    BeamColumnFinish = 1,
    FloorFinish = 2
}

public enum RoomCreationScope
{
    ActiveViewLevel,
    PickLevel,
    AllLevels
}

public partial class FinishingWindow : Window
{
    private readonly Document _doc;
    private List<WallType> _wallTypes = new();
    private List<FloorType> _floorTypes = new();

    // ═══ Public properties for active tab ═══
    public FinishingTab ActiveTab => (FinishingTab)(MainTabControl?.SelectedIndex ?? 0);

    // ═══ Tab 1: Wall Finish (from PlasterWindow) ═══
    public RoomSelectionMethod WallSelectionMethod
    {
        get
        {
            if (RbAllInView?.IsChecked == true) return RoomSelectionMethod.AllInView;
            if (RbByParameter?.IsChecked == true) return RoomSelectionMethod.ByParameter;
            return RoomSelectionMethod.PickRooms;
        }
    }

    public string WallSelectedParameter => CboWallRoomParameter?.SelectedItem as string ?? "";
    public string WallParameterValue => CboWallParameterValue?.Text ?? "";

    public ElementId SelectedWallTypeId =>
        CboWallType.SelectedItem is WallType wt ? wt.Id : ElementId.InvalidElementId;

    public double HeightMm
    {
        get
        {
            if (RbHeightAuto?.IsChecked == true) return 0;
            if (RbHeightCeiling?.IsChecked == true) return 0;
            return ParseMm(TxtCustomHeight.Text, 3000);
        }
    }

    public bool DetectCeiling => RbHeightCeiling?.IsChecked == true;
    public double CeilingOverlapMm => ParseMm(TxtCeilingOverlap?.Text ?? "50", 50);

    public double BaseOffsetMm => ParseMm(TxtBaseOffset.Text, 0);
    public double BoundaryOffsetMm => ParseMm(TxtBoundaryOffset.Text, 0);
    public bool JoinWithOriginal => ChkJoinGeometry.IsChecked == true;
    public bool AssignRoomName => ChkAssignRoomName.IsChecked == true;
    public string RoomNameParam => CboRoomNameParam?.Text ?? "Comments";

    // ═══ Tab 2: Beam/Column Finish ═══
    public enum BeamColSelectionMethod { Pick, AllBeams, AllColumns, AllBoth }

    public BeamColSelectionMethod BeamColMethod
    {
        get
        {
            if (RbAllBeamsInView?.IsChecked == true) return BeamColSelectionMethod.AllBeams;
            if (RbAllColumnsInView?.IsChecked == true) return BeamColSelectionMethod.AllColumns;
            if (RbAllBeamColInView?.IsChecked == true) return BeamColSelectionMethod.AllBoth;
            return BeamColSelectionMethod.Pick;
        }
    }

    public ElementId BeamColWallTypeId =>
        CboBeamColWallType.SelectedItem is WallType wt ? wt.Id : ElementId.InvalidElementId;

    public bool BeamColJoinWithOriginal => ChkBeamColJoin.IsChecked == true;
    public bool IncludeBeamBottom => ChkBeamBottom.IsChecked == true;
    public double BeamColOffsetMm => ParseMm(TxtBeamColOffset.Text, 0);

    // ═══ Tab 3: Floor Finish ═══
    public RoomSelectionMethod FloorSelectionMethod
    {
        get
        {
            if (RbFloorAllInView?.IsChecked == true) return RoomSelectionMethod.AllInView;
            if (RbFloorByParameter?.IsChecked == true) return RoomSelectionMethod.ByParameter;
            return RoomSelectionMethod.PickRooms;
        }
    }

    public string FloorSelectedParameter => CboFloorRoomParameter?.SelectedItem as string ?? "";
    public string FloorParameterValue => CboFloorParameterValue?.Text ?? "";

    public ElementId SelectedFloorTypeId =>
        CboFloorType.SelectedItem is FloorType ft ? ft.Id : ElementId.InvalidElementId;

    public double FloorOffsetMm => ParseMm(TxtFloorOffset.Text, 0);
    public double FloorBoundaryOffsetMm => ParseMm(TxtFloorBoundaryOffset.Text, 0);
    public bool FloorAssignRoomName => ChkFloorAssignRoomName.IsChecked == true;

    // ═══ Constructor ═══
    public FinishingWindow(Document doc)
    {
        _doc = doc;
        InitializeComponent();
        LoadWallTypes();
        LoadFloorTypes();
        LoadRoomParameters();
        LoadWallParamNames();
    }

    // ═══ Load data ═══

    private void LoadWallTypes()
    {
        _wallTypes = new FilteredElementCollector(_doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .OrderBy(w => w.Name)
            .ToList();

        CboWallType.ItemsSource = _wallTypes;
        CboBeamColWallType.ItemsSource = _wallTypes;

        var preferred = _wallTypes.FirstOrDefault(w =>
            !w.Name.StartsWith("_") && (
            w.Name.IndexOf("finish", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            w.Name.IndexOf("hoan thien", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            w.Name.IndexOf("hoàn thiện", System.StringComparison.OrdinalIgnoreCase) >= 0));

        if (preferred == null)
            preferred = _wallTypes.FirstOrDefault(w => !w.Name.StartsWith("_"));

        CboWallType.SelectedItem = preferred ?? _wallTypes.FirstOrDefault();
        CboBeamColWallType.SelectedItem = preferred ?? _wallTypes.FirstOrDefault();
    }

    private void LoadFloorTypes()
    {
        _floorTypes = new FilteredElementCollector(_doc)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .OrderBy(f => f.Name)
            .ToList();

        CboFloorType.ItemsSource = _floorTypes;

        var preferred = _floorTypes.FirstOrDefault(f =>
            f.Name.IndexOf("finish", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            f.Name.IndexOf("hoan thien", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            f.Name.IndexOf("hoàn thiện", System.StringComparison.OrdinalIgnoreCase) >= 0);

        CboFloorType.SelectedItem = preferred ?? _floorTypes.FirstOrDefault();
    }

    private void LoadRoomParameters()
    {
        var parameters = new List<string>();
        var room = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .FirstOrDefault() as Room;

        if (room != null)
        {
            foreach (Parameter p in room.Parameters)
            {
                if (!string.IsNullOrWhiteSpace(p.Definition.Name))
                    parameters.Add(p.Definition.Name);
            }
        }
        else
        {
            parameters.AddRange(new[] { "Name", "Number", "Department", "Comments" });
        }

        var sortedParams = parameters.Distinct().OrderBy(n => n).ToList();
        CboWallRoomParameter.ItemsSource = sortedParams;
        CboFloorRoomParameter.ItemsSource = sortedParams;
        if (sortedParams.Count > 0)
        {
            CboWallRoomParameter.SelectedIndex = 0;
            CboFloorRoomParameter.SelectedIndex = 0;
        }
    }

    private void LoadWallParamNames()
    {
        var paramNames = new List<string> { "Comments" };

        var sampleWall = new FilteredElementCollector(_doc)
            .OfClass(typeof(Wall))
            .WhereElementIsNotElementType()
            .FirstOrDefault();

        if (sampleWall != null)
        {
            foreach (Parameter p in sampleWall.Parameters)
            {
                if (p.StorageType == StorageType.String
                    && !p.IsReadOnly
                    && !string.IsNullOrWhiteSpace(p.Definition.Name))
                {
                    paramNames.Add(p.Definition.Name);
                }
            }
        }

        CboRoomNameParam.ItemsSource = paramNames.Distinct().OrderBy(n => n).ToList();
        var defaultIdx = paramNames.IndexOf("Comments");
        CboRoomNameParam.SelectedIndex = defaultIdx >= 0 ? defaultIdx : 0;
    }

    // ═══ Event Handlers — Tab 1: Wall ═══
    private void RbWallCreationMethod_Checked(object sender, RoutedEventArgs e)
    {
        if (PnlWallParameterOptions == null) return;
        PnlWallParameterOptions.Visibility = RbByParameter.IsChecked == true
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    private void CboWallRoomParameter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadParameterValues(CboWallRoomParameter, CboWallParameterValue);
    }

    private void TxtWallSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterWallTypes(TxtWallSearch.Text, CboWallType);
    }

    private void TxtBeamColWallSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterWallTypes(TxtBeamColWallSearch.Text, CboBeamColWallType);
    }

    // ═══ Event Handlers — Tab 3: Floor ═══
    private void RbFloorCreationMethod_Checked(object sender, RoutedEventArgs e)
    {
        if (PnlFloorParameterOptions == null) return;
        PnlFloorParameterOptions.Visibility = RbFloorByParameter.IsChecked == true
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    private void CboFloorRoomParameter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadParameterValues(CboFloorRoomParameter, CboFloorParameterValue);
    }

    private void TxtFloorSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = TxtFloorSearch.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            CboFloorType.ItemsSource = _floorTypes;
        }
        else
        {
            var filtered = _floorTypes
                .Where(f => f.Name.IndexOf(text, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            CboFloorType.ItemsSource = filtered;
        }

        if (CboFloorType.Items.Count > 0)
            CboFloorType.SelectedIndex = 0;
    }

    // ═══ Shared helpers ═══
    private void LoadParameterValues(ComboBox paramCombo, ComboBox valueCombo)
    {
        if (paramCombo.SelectedItem == null || valueCombo == null) return;

        var paramName = paramCombo.SelectedItem as string ?? "";
        if (string.IsNullOrEmpty(paramName)) return;

        var values = new HashSet<string>();
        var rooms = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .Where(r => r.Area > 0);

        foreach (var r in rooms)
        {
            var p = r.GetParameters(paramName).FirstOrDefault();
            if (p != null)
            {
                string val = GetParameterDisplayValue(p);
                if (!string.IsNullOrWhiteSpace(val))
                    values.Add(val);
            }
        }

        valueCombo.ItemsSource = values.OrderBy(v => v).ToList();
        if (valueCombo.Items.Count > 0)
            valueCombo.SelectedIndex = 0;
        else
            valueCombo.Text = "";
    }

    /// <summary>
    /// Lấy giá trị hiển thị của parameter, hỗ trợ mọi StorageType.
    /// </summary>
    private string GetParameterDisplayValue(Parameter p)
    {
        // AsValueString() trả về giá trị formatted (ví dụ: "3000 mm")
        var valStr = p.AsValueString();
        if (!string.IsNullOrWhiteSpace(valStr)) return valStr;

        switch (p.StorageType)
        {
            case StorageType.String:
                return p.AsString() ?? "";
            case StorageType.Integer:
                return p.AsInteger().ToString();
            case StorageType.Double:
                return p.AsDouble().ToString("F2");
            case StorageType.ElementId:
                var elemId = p.AsElementId();
                if (elemId != ElementId.InvalidElementId)
                {
                    var elem = _doc.GetElement(elemId);
                    return elem?.Name ?? elemId.ToString();
                }
                return "";
            default:
                return "";
        }
    }

    private void FilterWallTypes(string searchText, ComboBox targetCombo)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            targetCombo.ItemsSource = _wallTypes;
        }
        else
        {
            var filtered = _wallTypes
                .Where(w => w.Name.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            targetCombo.ItemsSource = filtered;
        }

        if (targetCombo.Items.Count > 0)
            targetCombo.SelectedIndex = 0;
    }

    // ═══ Footer buttons ═══
    private void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        // Validate based on active tab
        switch (ActiveTab)
        {
            case FinishingTab.WallFinish:
                if (CboWallType.SelectedItem == null)
                {
                    MessageBox.Show("Vui lòng chọn loại tường hoàn thiện!", "Thiếu thông tin",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                break;

            case FinishingTab.BeamColumnFinish:
                if (CboBeamColWallType.SelectedItem == null)
                {
                    MessageBox.Show("Vui lòng chọn loại tường bọc!", "Thiếu thông tin",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                break;

            case FinishingTab.FloorFinish:
                if (CboFloorType.SelectedItem == null)
                {
                    MessageBox.Show("Vui lòng chọn loại sàn!", "Thiếu thông tin",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                break;
        }

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static double ParseMm(string text, double fallback)
    {
        return double.TryParse(text, out var v) ? v : fallback;
    }
}
