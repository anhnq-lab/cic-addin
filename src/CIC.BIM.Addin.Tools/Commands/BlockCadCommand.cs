using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Tools.Views;

namespace CIC.BIM.Addin.Tools.Commands;

[Transaction(TransactionMode.Manual)]
public class BlockCadCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var window = new BlockCadWindow();
        window.ShowDialog();

        if (window.DialogResult != true)
            return Result.Cancelled;

        // TODO: Implement Block CAD → Family placement logic
        // window.Mappings, window.UseDynamicBlock, window.RotateFromCad, etc.

        TaskDialog.Show("Đặt thiết bị từ Block CAD",
            $"Chạy thành công!\n" +
            $"Mapping: {window.Mappings.Count} mục\n" +
            $"Dynamic Block: {(window.UseDynamicBlock ? "Có" : "Không")}\n" +
            $"Góc xoay: {(window.RotateFromCad ? "Từ CAD" : $"Cố định {window.FixedRotation}°")}");

        return Result.Succeeded;
    }
}
