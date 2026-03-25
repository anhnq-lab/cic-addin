using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Tools.Services;
using CIC.BIM.Addin.Tools.Views;

namespace CIC.BIM.Addin.Tools.Commands;

[Transaction(TransactionMode.Manual)]
public class BlockCadCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (!AuthGuard.EnsureLoggedIn()) return Result.Cancelled;

        try
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            var doc = uiDoc.Document;

            // Mở window — user chọn CAD link, scan blocks, cấu hình mapping
            var window = new BlockCadWindow(doc);
            window.ShowDialog();

            if (window.DialogResult != true || window.Config == null)
                return Result.Cancelled;

            var config = window.Config;

            // Chạy đặt thiết bị trong Transaction
            using var tx = new Transaction(doc, "CIC Block CAD → Equipment");
            tx.Start();

            BlockCadResult result;
            try
            {
                result = BlockCadService.Execute(doc, config);
                tx.Commit();
            }
            catch (System.Exception ex)
            {
                tx.RollBack();
                TaskDialog.Show("Đặt thiết bị — Lỗi", $"❌ Lỗi khi đặt thiết bị:\n{ex.Message}");
                return Result.Failed;
            }

            // Hiển thị kết quả
            var summary = $"✅ Đặt thiết bị hoàn tất!\n\n" +
                          $"📊 Tổng thiết bị đã đặt: {result.TotalPlaced}\n\n";

            if (result.CountByBlock.Count > 0)
            {
                summary += "📋 Chi tiết:\n";
                foreach (var kvp in result.CountByBlock.OrderByDescending(k => k.Value))
                {
                    summary += $"  📦 {kvp.Key}: {kvp.Value} thiết bị\n";
                }
            }

            if (result.Errors.Count > 0)
            {
                summary += $"\n⚠️ Cảnh báo ({result.Errors.Count}):\n";
                foreach (var err in result.Errors.Take(10))
                    summary += $"  • {err}\n";
                if (result.Errors.Count > 10)
                    summary += $"  ... và {result.Errors.Count - 10} lỗi khác\n";
            }

            TaskDialog.Show("Đặt thiết bị từ Block CAD — B1.23", summary);
            return Result.Succeeded;
        }
        catch (System.Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("CIC Block CAD — Lỗi",
                $"❌ Lỗi khởi tạo tool:\n\n{ex.Message}\n\n{ex.StackTrace}");
            return Result.Failed;
        }
    }
}
