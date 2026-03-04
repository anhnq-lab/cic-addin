using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Tools.Services;
using CIC.BIM.Addin.Tools.Views;

namespace CIC.BIM.Addin.Tools.Commands;

[Transaction(TransactionMode.Manual)]
public class CadAutoDrawCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        // Mở window — user chọn CAD link, scan layers, cấu hình mapping
        var window = new CadAutoDrawWindow(doc);
        window.ShowDialog();

        if (window.DialogResult != true || window.Config == null)
            return Result.Cancelled;

        var config = window.Config;

        // Chạy tạo đối tượng trong Transaction
        using var tx = new Transaction(doc, "CIC Auto-Draw from CAD");
        tx.Start();

        CadAutoDrawResult result;
        try
        {
            result = CadAutoDrawService.Execute(doc, config);
            tx.Commit();
        }
        catch (System.Exception ex)
        {
            tx.RollBack();
            TaskDialog.Show("Auto-Draw — Lỗi", $"❌ Lỗi khi tạo đối tượng:\n{ex.Message}");
            return Result.Failed;
        }

        // Hiển thị kết quả
        var summary = $"✅ Auto-Draw hoàn tất!\n\n" +
                      $"📊 Tổng đối tượng tạo: {result.TotalCreated}\n\n";

        if (result.CountByType.Count > 0)
        {
            summary += "📋 Chi tiết:\n";
            foreach (var kvp in result.CountByType)
            {
                var icon = kvp.Key switch
                {
                    RevitObjectType.Wall => "🧱",
                    RevitObjectType.Column => "🏛",
                    RevitObjectType.Beam => "📏",
                    RevitObjectType.Floor => "⬜",
                    RevitObjectType.Pipe => "🔵",
                    RevitObjectType.Duct => "💨",
                    RevitObjectType.CableTray => "⚡",
                    RevitObjectType.FamilyInstance => "📦",
                    _ => "•"
                };
                summary += $"  {icon} {kvp.Key}: {kvp.Value}\n";
            }
        }

        if (result.Errors.Count > 0)
        {
            summary += $"\n⚠️ Cảnh báo ({result.Errors.Count}):\n";
            foreach (var err in result.Errors.Take(5))
                summary += $"  • {err}\n";
            if (result.Errors.Count > 5)
                summary += $"  ... và {result.Errors.Count - 5} lỗi khác\n";
        }

        TaskDialog.Show("Auto-Draw từ CAD — B1.25", summary);
        return Result.Succeeded;
    }
}
