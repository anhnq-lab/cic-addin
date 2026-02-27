using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Tools.Views;

namespace CIC.BIM.Addin.Tools.Commands;

[Transaction(TransactionMode.Manual)]
public class DuctFromCadCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var window = new DuctFromCadWindow();
        window.ShowDialog();

        if (window.DialogResult != true)
            return Result.Cancelled;

        // TODO: Implement Duct from CAD line logic
        // window.Mappings, window.ShapeIndex, window.Elevation, etc.

        var shapes = new[] { "Rectangular", "Round", "Oval" };
        var systems = new[] { "SA", "RA", "EA", "FA" };
        var elevRefs = new[] { "BOD", "CL", "TOD" };

        TaskDialog.Show("Ống gió từ CAD",
            $"Chạy thành công!\n" +
            $"Mapping: {window.Mappings.Count} mục\n" +
            $"Hình dạng: {shapes[window.ShapeIndex]}\n" +
            $"Cao độ: {window.Elevation}mm ({elevRefs[window.ElevationRefIndex]})\n" +
            $"System: {systems[window.SystemIndex]}\n" +
            $"Auto-fitting: {(window.AutoFitting ? "Có" : "Không")}");

        return Result.Succeeded;
    }
}
