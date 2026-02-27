using System.Windows;

namespace CIC.BIM.Addin.Tools.Views;

public partial class PipeSlopeWindow : Window
{
    public double SlopeValue => double.TryParse(TxtSlope.Text, out var v) ? v : 1;
    public int SlopeUnitIndex => CboSlopeUnit.SelectedIndex; // 0=%, 1=‰, 2=1:x
    public bool FixedUpstream => RdoUpstream.IsChecked == true;
    public bool AutoRotateFitting => ChkAutoRotateFitting.IsChecked == true;
    public bool UpdateElevation => ChkUpdateElevation.IsChecked == true;
    public bool CheckClash => ChkCheckClash.IsChecked == true;

    public PipeSlopeWindow()
    {
        InitializeComponent();
    }

    private void BtnSelect_Click(object sender, RoutedEventArgs e)
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
