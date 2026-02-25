using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Analytics.Views;

namespace CIC.BIM.Addin.Analytics.Commands;

/// <summary>
/// Opens the Analytics Dashboard (admin only)
/// </summary>
[Transaction(TransactionMode.Manual)]
public class DashboardCommand : IExternalCommand
{
    /// <summary>
    /// Static reference to the tracker — set by App.cs on startup
    /// </summary>
    public static Services.ActivityTracker? Tracker { get; set; }

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            if (Tracker == null)
            {
                TaskDialog.Show("CIC Analytics", "Hệ thống tracking chưa được khởi tạo.");
                return Result.Failed;
            }

            var store = Tracker.GetStore();
            var window = new DashboardWindow(store);
            window.ShowDialog();

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
