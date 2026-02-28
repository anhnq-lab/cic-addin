using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Tools.Views;

namespace CIC.BIM.Addin.Tools.Commands;

/// <summary>
/// Mở tool tô màu đối tượng theo Category.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ColorOverrideCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            var doc = uiDoc.Document;

            var window = new ColorOverrideWindow(doc, uiDoc);
            window.ShowDialog();

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            TaskDialog.Show("Lỗi", $"Color Override gặp lỗi:\n{ex.Message}");
            return Result.Failed;
        }
    }
}
