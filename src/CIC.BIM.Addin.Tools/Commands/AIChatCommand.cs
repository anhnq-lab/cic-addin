using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Tools.Views;

namespace CIC.BIM.Addin.Tools.Commands;

[Transaction(TransactionMode.ReadOnly)]
public class AIChatCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument.Document;

        var window = new AIChatWindow(doc);
        window.ShowDialog();

        return Result.Succeeded;
    }
}
