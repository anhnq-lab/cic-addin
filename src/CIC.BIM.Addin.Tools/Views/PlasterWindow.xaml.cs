using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

    public ElementId SelectedWallTypeId =>
        CboWallType.SelectedItem is WallType wt ? wt.Id : ElementId.InvalidElementId;

    public ElementId SelectedFloorTypeId =>
        CboFloorType.SelectedItem is FloorType ft ? ft.Id : ElementId.InvalidElementId;

    public double HeightMm
    {
        get
        {
            if (RbHeightAuto?.IsChecked == true) return 0;
            if (RbHeightCeiling?.IsChecked == true) return 0; // Dùng ceiling detect
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

    public bool CreateFloorFinish => ChkCreateFloor.IsChecked == true;

    public double FloorOffsetMm => ParseMm(TxtFloorOffset.Text, 0);

    public PlasterWindow(Document doc)
    {
        _doc = doc;
        InitializeComponent();
        LoadWallTypes();
        LoadFloorTypes();
        LoadRoomParameters();
        LoadWallParamNames();
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

    private void LoadWallParamNames()
    {
        // Lấy danh sách parameter kiểu String từ 1 wall instance
        var paramNames = new List<string> { "Comments" }; // Mặc định luôn có

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

        // Chọn mặc định "Comments"
        var defaultIdx = paramNames.IndexOf("Comments");
        CboRoomNameParam.SelectedIndex = defaultIdx >= 0 ? defaultIdx : 0;
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

    private void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        if (CboWallType.SelectedItem == null)
        {
            MessageBox.Show("Vui lòng chọn loại tường hoàn thiện!", "Thiếu thông tin",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CreateFloorFinish && CboFloorType.SelectedItem == null)
        {
            MessageBox.Show("Vui lòng chọn loại sàn!", "Thiếu thông tin",
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
