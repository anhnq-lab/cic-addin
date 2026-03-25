using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Tools.Services;
using CIC.BIM.Addin.Tools.Views;

namespace CIC.BIM.Addin.Tools.Commands;

/// <summary>
/// Command: Xuất IFC chuẩn
/// Opens the IFCExportWindow for standardized IFC export.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class IFCExportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (!AuthGuard.EnsureLoggedIn()) return Result.Cancelled;

        var doc = commandData.Application.ActiveUIDocument.Document;

        try
        {
            var window = new IFCExportWindow(doc);
            window.ShowDialog();
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            TaskDialog.Show("CIC Tool - Lỗi", $"❌ Không thể mở cửa sổ xuất IFC:\n{ex.Message}");
            message = ex.Message;
            return Result.Failed;
        }
    }
}
