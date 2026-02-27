using System.Collections.ObjectModel;
using System.Windows;

namespace CIC.BIM.Addin.Tools.Views;

public class BlockFamilyMapping
{
    public string BlockName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string TypeName { get; set; } = "";
}

public partial class BlockCadWindow : Window
{
    public ObservableCollection<BlockFamilyMapping> Mappings { get; } = new();

    public string LayerFilter => TxtLayer.Text;
    public double DefaultElevation => double.TryParse(TxtDefaultElevation.Text, out var v) ? v : 0;
    public bool UseDynamicBlock => ChkDynamicBlock.IsChecked == true;
    public bool RotateFromCad => RdoRotateFromCad.IsChecked == true;
    public double FixedRotation => double.TryParse(TxtFixedRotation.Text, out var v) ? v : 0;

    public BlockCadWindow()
    {
        InitializeComponent();

        // Seed with example row
        Mappings.Add(new BlockFamilyMapping
        {
            BlockName = "(Block name in CAD)",
            FamilyName = "(Family name in Revit)",
            TypeName = "(Type)"
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
