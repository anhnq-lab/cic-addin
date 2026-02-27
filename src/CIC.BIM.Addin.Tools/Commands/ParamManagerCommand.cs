using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Tools.Views;

namespace CIC.BIM.Addin.Tools.Commands;

[Transaction(TransactionMode.Manual)]
public class ParamManagerCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            var window = new ParamManagerWindow(doc);
            window.ShowDialog();

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            TaskDialog.Show("Error", ex.ToString());
            return Result.Failed;
        }
    }
}
