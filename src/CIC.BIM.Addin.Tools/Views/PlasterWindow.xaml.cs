using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace CIC.BIM.Addin.Tools.Views;

public enum RoomSelectionMethod
{
    PickRooms,
    AllInView,
    ByParameter
}

public partial class PlasterWindow : Window
{
    private readonly Document _doc;
    private List<WallType> _wallTypes = new();
    private List<FloorType> _floorTypes = new();
    private bool _isFilteringWall;
    private bool _isFilteringFloor;
    private bool _isFilteringColumn;

    // Selected options
    public RoomSelectionMethod SelectionMethod
    {
        get
        {
            if (RbAllInView?.IsChecked == true) return RoomSelectionMethod.AllInView;
            if (RbByParameter?.IsChecked == true) return RoomSelectionMethod.ByParameter;
            return RoomSelectionMethod.PickRooms;
        }
    }

    public string SelectedParameter => CboRoomParameter?.SelectedItem as string ?? "";
    
    public string ParameterValue => CboParameterValue?.Text ?? "";

    // Wall plaster
    public bool CreateWallPlaster => ChkWallPlaster.IsChecked == true;
    public ElementId SelectedWallTypeId =>
        CboWallType.SelectedItem is WallType wt ? wt.Id : ElementId.InvalidElementId;

    // Column plaster
    public bool CreateColumnPlaster => ChkColumnPlaster.IsChecked == true;
    public ElementId SelectedColumnTypeId =>
        CboColumnType.SelectedItem is WallType ct ? ct.Id : ElementId.InvalidElementId;

    public ElementId SelectedFloorTypeId =>
        CboFloorType.SelectedItem is FloorType ft ? ft.Id : ElementId.InvalidElementId;

    public double HeightMm =>
        RbHeightAuto.IsChecked == true ? 0 : ParseMm(TxtCustomHeight.Text, 3000);

    public double BaseOffsetMm => ParseMm(TxtBaseOffset.Text, 0);

    public bool JoinWithOriginal => ChkJoinGeometry.IsChecked == true;

    public bool AutoRoomBounding => ChkAutoRoomBounding.IsChecked == true;

    public bool CreateFloorFinish => ChkCreateFloor.IsChecked == true;

    public double FloorOffsetMm => ParseMm(TxtFloorOffset.Text, 0);

    public PlasterWindow(Document doc)
    {
        _doc = doc;
        InitializeComponent();
        LoadWallTypes();
        LoadColumnTypes();
        LoadFloorTypes();
        LoadRoomParameters();
    }

    private void LoadWallTypes()
    {
        _wallTypes = new FilteredElementCollector(_doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .OrderBy(w => w.Name)
            .ToList();

        CboWallType.ItemsSource = _wallTypes;

        var preferred = _wallTypes.FirstOrDefault(w =>
            !w.Name.StartsWith("_") && (
            w.Name.IndexOf("finish", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            w.Name.IndexOf("hoan thien", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            w.Name.IndexOf("hoàn thiện", System.StringComparison.OrdinalIgnoreCase) >= 0));

        if (preferred == null)
            preferred = _wallTypes.FirstOrDefault(w => !w.Name.StartsWith("_"));

        CboWallType.SelectedItem = preferred ?? _wallTypes.FirstOrDefault();
    }

    private void LoadColumnTypes()
    {
        // Share same wall types list, separate combobox
        CboColumnType.ItemsSource = _wallTypes;

        // Try to find same preferred type as wall
        var preferred = CboWallType.SelectedItem ?? _wallTypes.FirstOrDefault(w => !w.Name.StartsWith("_"));
        CboColumnType.SelectedItem = preferred ?? _wallTypes.FirstOrDefault();
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
                {
                    parameters.Add(p.Definition.Name);
                }
            }
        }
        else
        {
            parameters.Add("Name");
            parameters.Add("Number");
            parameters.Add("Department");
            parameters.Add("Comments");
        }

        CboRoomParameter.ItemsSource = parameters.Distinct().OrderBy(n => n).ToList();
        if (CboRoomParameter.Items.Count > 0)
            CboRoomParameter.SelectedIndex = 0;
    }

    private void CboRoomParameter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboRoomParameter.SelectedItem == null || CboParameterValue == null) return;

        var paramName = CboRoomParameter.SelectedItem as string ?? "";
        if (string.IsNullOrEmpty(paramName)) return;

        // Collect distinct values of the selected parameter from all rooms
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
                var val = p.AsValueString() ?? p.AsString() ?? "";
                if (!string.IsNullOrWhiteSpace(val))
                    values.Add(val);
            }
        }

        CboParameterValue.ItemsSource = values.OrderBy(v => v).ToList();
        if (CboParameterValue.Items.Count > 0)
            CboParameterValue.SelectedIndex = 0;
        else
            CboParameterValue.Text = "";
    }

    private void RbCreationMethod_Checked(object sender, RoutedEventArgs e)
    {
        if (PnlParameterOptions == null) return;
        
        PnlParameterOptions.Visibility = RbByParameter.IsChecked == true 
            ? System.Windows.Visibility.Visible 
            : System.Windows.Visibility.Collapsed;
    }

    private void TxtWallSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = TxtWallSearch.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            CboWallType.ItemsSource = _wallTypes;
        }
        else
        {
            var filtered = _wallTypes
                .Where(w => w.Name.IndexOf(text, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            CboWallType.ItemsSource = filtered;
        }

        // Auto-select first item if any
        if (CboWallType.Items.Count > 0)
            CboWallType.SelectedIndex = 0;
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

    private void TxtColumnSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = TxtColumnSearch.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            CboColumnType.ItemsSource = _wallTypes;
        }
        else
        {
            var filtered = _wallTypes
                .Where(w => w.Name.IndexOf(text, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            CboColumnType.ItemsSource = filtered;
        }

        if (CboColumnType.Items.Count > 0)
            CboColumnType.SelectedIndex = 0;
    }

    private void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        bool wallOk = !CreateWallPlaster || CboWallType.SelectedItem != null;
        bool colOk = !CreateColumnPlaster || CboColumnType.SelectedItem != null;

        if (!wallOk || !colOk)
        {
            MessageBox.Show("Vui lòng chọn Wall Type cho các loại trát đã bật!", "Thiếu thông tin",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!CreateWallPlaster && !CreateColumnPlaster && !CreateFloorFinish)
        {
            MessageBox.Show("Vui lòng chọn ít nhất một loại trát!", "Thiếu thông tin",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
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
