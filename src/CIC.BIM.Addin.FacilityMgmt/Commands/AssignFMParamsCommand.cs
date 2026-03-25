using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.FacilityMgmt.Views;
using CIC.BIM.Addin.Tools.Services;

namespace CIC.BIM.Addin.FacilityMgmt.Commands;

/// <summary>
/// Command: Gán tham số FM
/// Opens the FMPreviewWindow to show device list before binding FM parameters.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class AssignFMParamsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (!AuthGuard.EnsureLoggedIn()) return Result.Cancelled;

        var doc = commandData.Application.ActiveUIDocument.Document;
        var app = commandData.Application.Application;

        try
        {
            var window = new FMPreviewWindow(doc, app);
            window.ShowDialog();

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            TaskDialog.Show("CIC Tool - Lỗi", $"❌ Không thể mở cửa sổ quản lý vận hành:\n{ex.Message}");
            message = ex.Message;
            return Result.Failed;
        }
    }
}
