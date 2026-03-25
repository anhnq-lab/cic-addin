using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Tools.Views;

namespace CIC.BIM.Addin.Tools.Commands;

[Transaction(TransactionMode.ReadOnly)]
public class LoginCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var window = new LoginWindow();
            window.ShowDialog();
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            TaskDialog.Show("CIC Tools", $"Lỗi: {ex.Message}");
            return Result.Failed;
        }
    }
}
