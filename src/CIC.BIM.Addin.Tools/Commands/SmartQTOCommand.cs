using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Tools.Services;
using CIC.BIM.Addin.Tools.Views;

namespace CIC.BIM.Addin.Tools.Commands;

[Transaction(TransactionMode.Manual)]
public class SmartQTOCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiapp = commandData.Application;
        var uidoc = uiapp.ActiveUIDocument;
        var doc = uidoc.Document;

        try
        {
            // Các Category phổ biến cho QTO
            var qtoCategories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralFoundation,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_CableTray
            };

            var vm = new SmartQTOViewModel();
            foreach (var cat in qtoCategories)
            {
                vm.Categories.Add(new CategoryItem 
                { 
                    Name = LabelUtils.GetLabelFor(cat), 
                    Category = cat, 
                    IsSelected = true // Mặc định chọn hết
                });
            }

            var dialog = new SmartQTOWindow(vm, doc, uidoc.Selection.GetElementIds(), doc.ProjectInformation.Name ?? doc.Title);
            dialog.ShowDialog();

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
