using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CIC.BIM.Addin.Tools.Commands;

/// <summary>
/// Bật Room Bounding cho tất cả RevitLinkInstance và cột.
/// Sau khi chạy, Room sẽ nhận diện được tường/cột từ file link.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class SetRoomBoundingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument.Document;
        int linksSet = 0;
        int columnsSet = 0;

        using var tx = new Transaction(doc, "Bật Room Bounding cho Link & Cột");
        tx.Start();

        // 1. Bật Room Bounding cho tất cả RevitLinkInstance
        var linkInstances = new FilteredElementCollector(doc)
            .OfClass(typeof(RevitLinkInstance))
            .ToList();

        foreach (var linkInst in linkInstances)
        {
            var rbParam = linkInst.LookupParameter("Room Bounding");
            if (rbParam != null && !rbParam.IsReadOnly && rbParam.AsInteger() == 0)
            {
                rbParam.Set(1);
                linksSet++;
            }
        }

        // 2. Bật Room Bounding cho tất cả cột (host)
        var columns = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .WherePasses(new LogicalOrFilter(
                new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns),
                new ElementCategoryFilter(BuiltInCategory.OST_Columns)))
            .ToList();

        foreach (var col in columns)
        {
            var rbParam = col.LookupParameter("Room Bounding");
            if (rbParam != null && !rbParam.IsReadOnly && rbParam.AsInteger() == 0)
            {
                rbParam.Set(1);
                columnsSet++;
            }
        }

        tx.Commit();

        // 3. Hiện kết quả
        var totalLinks = linkInstances.Count;
        var totalColumns = columns.Count;

        var msg = "✅ Hoàn tất bật Room Bounding!\n\n";
        msg += $"📁 Link Instances: {linksSet}/{totalLinks} đã bật\n";
        msg += $"🏛 Cột (host): {columnsSet}/{totalColumns} đã bật\n\n";

        if (linksSet > 0 || columnsSet > 0)
        {
            msg += "👉 Bây giờ bạn có thể:\n";
            msg += "• Tạo Room mới → Room sẽ nhận diện linked walls/columns\n";
            msg += "• Room hiện tại tự động cập nhật biên dạng\n\n";
        }

        if (linksSet == 0 && columnsSet == 0)
        {
            msg += "ℹ️ Tất cả đã được bật sẵn từ trước.\n\n";
        }

        msg += "⚠️ Lưu ý: Cột trong file LINK cũng cần Room Bounding = Yes.\n";
        msg += "Mở file link → chạy tool này → Save → Reload link.";

        TaskDialog.Show("CIC Tool - Room Bounding", msg);
        return Result.Succeeded;
    }
}
