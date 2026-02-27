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

    // Selected options
    public ElementId SelectedWallTypeId =>
        CboWallType.SelectedItem is WallType wt ? wt.Id : ElementId.InvalidElementId;

    public ElementId SelectedFloorTypeId =>
        CboFloorType.SelectedItem is FloorType ft ? ft.Id : ElementId.InvalidElementId;

    public double HeightMm =>
        RbHeightAuto.IsChecked == true ? 0 : ParseMm(TxtCustomHeight.Text, 3000);

    public double BaseOffsetMm => ParseMm(TxtBaseOffset.Text, 0);

    public bool JoinWithOriginal => ChkJoinGeometry.IsChecked == true;

    public bool CreateFloorFinish => ChkCreateFloor.IsChecked == true;

    public double FloorOffsetMm => ParseMm(TxtFloorOffset.Text, 0);

    public PlasterWindow(Document doc)
    {
        _doc = doc;
        InitializeComponent();
        LoadWallTypes();
        LoadFloorTypes();
        LoadRoomParameters();

        // Subscribe to text changes for search/filter
        CboWallType.AddHandler(TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(CboWallType_TextChanged));
        CboFloorType.AddHandler(TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(CboFloorType_TextChanged));
    }

    private void LoadWallTypes()
    {
        _wallTypes = new FilteredElementCollector(_doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .OrderBy(w => w.Name)
            .ToList();

        CboWallType.ItemsSource = _wallTypes;

        // Try to preselect a thin wall type (name contains "finish", "hoàn thiện")
        // Skip items starting with "_" like "_Not Defined"
        var preferred = _wallTypes.FirstOrDefault(w =>
            !w.Name.StartsWith("_") && (
            w.Name.IndexOf("finish", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            w.Name.IndexOf("hoan thien", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            w.Name.IndexOf("hoàn thiện", System.StringComparison.OrdinalIgnoreCase) >= 0));

        // If no preferred, pick first non-underscore type
        if (preferred == null)
            preferred = _wallTypes.FirstOrDefault(w => !w.Name.StartsWith("_"));

        CboWallType.SelectedItem = preferred ?? _wallTypes.FirstOrDefault();
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

    private void CboWallType_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isFilteringWall) return;
        _isFilteringWall = true;
        try
        {
            var text = CboWallType.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                CboWallType.ItemsSource = _wallTypes;
            }
            else
            {
                CboWallType.ItemsSource = _wallTypes
                    .Where(w => w.Name.IndexOf(text, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }
            CboWallType.IsDropDownOpen = true;
            CboWallType.Text = text;
            // Move caret to end of text
            var tb = CboWallType.Template.FindName("PART_EditableTextBox", CboWallType) as TextBox;
            if (tb != null) tb.CaretIndex = tb.Text.Length;
        }
        finally { _isFilteringWall = false; }
    }

    private void CboFloorType_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isFilteringFloor) return;
        _isFilteringFloor = true;
        try
        {
            var text = CboFloorType.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                CboFloorType.ItemsSource = _floorTypes;
            }
            else
            {
                CboFloorType.ItemsSource = _floorTypes
                    .Where(f => f.Name.IndexOf(text, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }
            CboFloorType.IsDropDownOpen = true;
            CboFloorType.Text = text;
            var tb = CboFloorType.Template.FindName("PART_EditableTextBox", CboFloorType) as TextBox;
            if (tb != null) tb.CaretIndex = tb.Text.Length;
        }
        finally { _isFilteringFloor = false; }
    }

    private void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        if (CboWallType.SelectedItem == null)
        {
            MessageBox.Show("Vui long chon Wall Type!", "Thieu thong tin",
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
