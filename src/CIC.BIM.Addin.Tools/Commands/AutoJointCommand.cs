using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Tools.Services;
using CIC.BIM.Addin.Tools.Views;

namespace CIC.BIM.Addin.Tools.Commands;

[Transaction(TransactionMode.Manual)]
public class AutoJointCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            var doc = uiDoc.Document;

            var window = new AutoJointWindow(doc, uiDoc);
            window.ShowDialog();

            // Nếu scope = PickRegion → cần xử lý sau khi window đóng
            if (window.DialogResult == true)
            {
                if (window.SelectedScope == AutoJointService.JoinScope.PickRegion)
                {
                    var rules = window.Rules;
                    var categories = new System.Collections.Generic.HashSet<BuiltInCategory>();
                    foreach (var r in rules)
                    {
                        categories.Add(r.CuttingCategory);
                        categories.Add(r.CutCategory);
                    }

                    var pickedElements = AutoJointService.CollectByScope(
                        doc, uiDoc, AutoJointService.JoinScope.PickRegion, categories);

                    if (pickedElements.Count == 0)
                    {
                        TaskDialog.Show("Tự động nối", "Chưa chọn cấu kiện nào.");
                        return Result.Cancelled;
                    }

                    AutoJointService.JoinResult result;

                    if (window.ExecuteJoin)
                    {
                        result = AutoJointService.JoinAndSwitch(doc, rules, pickedElements);
                        TaskDialog.Show("Tự động nối cấu kiện",
                            $"✅ Hoàn tất!\n\n" +
                            $"🔗 Nối mới: {result.Joined}\n" +
                            $"🔄 Đổi thứ tự: {result.Switched}\n" +
                            $"🏗 Đầu dầm: {result.BeamEndsConnected}\n" +
                            $"✓ Đã nối sẵn: {result.AlreadyJoined}\n" +
                            $"⚠ Lỗi: {result.Failed}");
                    }
                    else if (window.ExecuteUnjoin)
                    {
                        result = AutoJointService.UnjoinAll(doc, rules, pickedElements);
                        TaskDialog.Show("Bỏ nối cấu kiện",
                            $"✅ Hoàn tất!\n\n" +
                            $"✂ Đã bỏ nối: {result.Unjoined}\n" +
                            $"⚠ Lỗi: {result.Failed}");
                    }
                }
            }

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            TaskDialog.Show("Lỗi", $"Auto Joint gặp lỗi:\n{ex.Message}");
            return Result.Failed;
        }
    }
}
