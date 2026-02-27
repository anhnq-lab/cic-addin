using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Tools.Views;

namespace CIC.BIM.Addin.Tools.Commands;

[Transaction(TransactionMode.Manual)]
public class PipeSlopeCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var window = new PipeSlopeWindow();
        window.ShowDialog();

        if (window.DialogResult != true)
            return Result.Cancelled;

        // TODO: Implement pipe slope adjustment logic
        // window.SlopeValue, window.FixedUpstream, window.AutoRotateFitting, etc.

        var units = new[] { "%", "‰", "1:x" };

        TaskDialog.Show("Thay đổi độ dốc ống",
            $"Chạy thành công!\n" +
            $"Độ dốc: {window.SlopeValue}{units[window.SlopeUnitIndex]}\n" +
            $"Điểm cố định: {(window.FixedUpstream ? "Upstream" : "Downstream")}\n" +
            $"Tự xoay fitting: {(window.AutoRotateFitting ? "Có" : "Không")}\n" +
            $"Cập nhật cao độ: {(window.UpdateElevation ? "Có" : "Không")}");

        return Result.Succeeded;
    }
}
