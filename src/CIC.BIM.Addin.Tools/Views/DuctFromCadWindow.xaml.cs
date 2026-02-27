using System.Collections.ObjectModel;
using System.Windows;

namespace CIC.BIM.Addin.Tools.Views;

public class LineDuctMapping
{
    public string LineTypeOrLayer { get; set; } = "";
    public string DuctType { get; set; } = "";
    public string Width { get; set; } = "";
    public string Height { get; set; } = "";
}

public partial class DuctFromCadWindow : Window
{
    public ObservableCollection<LineDuctMapping> Mappings { get; } = new();

    public int ShapeIndex => CboShape.SelectedIndex; // 0=Rect, 1=Round, 2=Oval
    public double Elevation => double.TryParse(TxtElevation.Text, out var v) ? v : 3000;
    public int ElevationRefIndex => CboElevRef.SelectedIndex; // 0=BOD, 1=CL, 2=TOD
    public int SystemIndex => CboSystem.SelectedIndex; // 0=SA, 1=RA, 2=EA, 3=FA
    public bool AutoFitting => ChkAutoFitting.IsChecked == true;

    public DuctFromCadWindow()
    {
        InitializeComponent();

        Mappings.Add(new LineDuctMapping
        {
            LineTypeOrLayer = "(Line Type / Layer)",
            DuctType = "(Duct Type)",
            Width = "400",
            Height = "300"
        });

        MappingGrid.ItemsSource = Mappings;
    }

    private void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
