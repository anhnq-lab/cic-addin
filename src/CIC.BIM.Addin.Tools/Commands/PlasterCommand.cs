using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Tools.Services;
using CIC.BIM.Addin.Tools.Views;

namespace CIC.BIM.Addin.Tools.Commands;

[Transaction(TransactionMode.Manual)]
public class PlasterCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        // Step 1: Show config window
        var window = new PlasterWindow(doc);
        window.ShowDialog();

        if (window.DialogResult != true)
            return Result.Cancelled;

        // Step 2: Find rooms based on Selection Method
        var rooms = new List<Room>();

        if (window.SelectionMethod == RoomSelectionMethod.AllInView)
        {
            rooms = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .ToList();
        }
        else if (window.SelectionMethod == RoomSelectionMethod.ByParameter)
        {
            var allRooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            string paramName = window.SelectedParameter;
            string expectedValue = window.ParameterValue ?? "";

            foreach (var r in allRooms)
            {
                var p = r.GetParameters(paramName).FirstOrDefault();
                if (p != null)
                {
                    string pValue = p.AsValueString() ?? p.AsString() ?? "";
                    if (pValue.IndexOf(expectedValue, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        rooms.Add(r);
                    }
                }
            }
            
            if (rooms.Count == 0)
            {
                TaskDialog.Show("Tường hoàn thiện", $"Không tìm thấy Room nào có Parameter '{paramName}' chứa giá trị '{expectedValue}'.");
                return Result.Cancelled;
            }
        }
        else
        {
            // PickRooms
            rooms = PickRoomsByPoint(uiDoc);
        }

        if (rooms.Count == 0)
        {
            TaskDialog.Show("Tường hoàn thiện", "Chưa chọn Room nào.");
            return Result.Cancelled;
        }

        // Step 3: Build options from window
        var options = new WallFinishService.WallFinishOptions
        {
            WallTypeId = window.SelectedWallTypeId,
            HeightMm = window.HeightMm,
            BaseOffsetMm = window.BaseOffsetMm,
            OffsetMm = window.BoundaryOffsetMm,
            JoinWithOriginal = window.JoinWithOriginal,
            AssignRoomName = window.AssignRoomName,
            RoomNameParam = window.RoomNameParam,
            CreateFloorFinish = window.CreateFloorFinish,
            FloorTypeId = window.SelectedFloorTypeId,
            FloorOffsetMm = window.FloorOffsetMm,
            DetectCeiling = window.DetectCeiling,
            CeilingOverlapMm = window.CeilingOverlapMm
        };

        // Step 4: Execute
        var service = new WallFinishService();
        var result = service.Execute(doc, rooms, options);

        // Step 5: Show results
        var msg = $"Hoàn tất!\n" +
                  $"Room đã xử lý: {result.RoomsProcessed}\n" +
                  $"Tường tạo mới: {result.WallsCreated}\n" +
                  $"Sàn tạo mới: {result.FloorsCreated}";

        if (options.OffsetMm != 0)
            msg += $"\nOffset biên dạng: {options.OffsetMm} mm";

        if (result.Warnings.Count > 0)
        {
            msg += $"\n\nCảnh báo ({result.Warnings.Count}):\n";
            msg += string.Join("\n", result.Warnings.Take(20));
            if (result.Warnings.Count > 20)
                msg += $"\n... và {result.Warnings.Count - 20} cảnh báo khác";
        }

        TaskDialog.Show("CIC Tool - Tường hoàn thiện", msg);
        return Result.Succeeded;
    }

    /// <summary>
    /// Pick Rooms by clicking INSIDE the room area.
    /// </summary>
    private static List<Room> PickRoomsByPoint(UIDocument uiDoc)
    {
        var rooms = new List<Room>();
        var doc = uiDoc.Document;
        var pickedIds = new HashSet<int>();

        // Check if user already selected rooms
        var currentSelection = uiDoc.Selection.GetElementIds();
        if (currentSelection.Count > 0)
        {
            foreach (var id in currentSelection)
            {
                if (doc.GetElement(id) is Room room && room.Area > 0)
                {
                    rooms.Add(room);
                    pickedIds.Add(room.Id.IntegerValue);
                }
            }
        }

        if (rooms.Count > 0)
            return rooms;

        // Interactive pick: click inside room to select
        TaskDialog.Show("Chọn Room",
            "Click vào BÊN TRONG phòng để chọn Room.\n" +
            "Nhấn Esc để kết thúc chọn.");

        // Get active view's phase for Room lookup
        var view = doc.ActiveView;
        var phaseParam = view.get_Parameter(BuiltInParameter.VIEW_PHASE);
        Phase? phase = null;
        if (phaseParam != null)
            phase = doc.GetElement(phaseParam.AsElementId()) as Phase;

        while (true)
        {
            try
            {
                var point = uiDoc.Selection.PickPoint("Click trong phòng (Esc để kết thúc)");

                Room? pickedRoom = null;

                if (phase != null)
                    pickedRoom = doc.GetRoomAtPoint(point, phase);
                else
                    pickedRoom = doc.GetRoomAtPoint(point);

                if (pickedRoom != null && pickedRoom.Area > 0)
                {
                    if (!pickedIds.Contains(pickedRoom.Id.IntegerValue))
                    {
                        rooms.Add(pickedRoom);
                        pickedIds.Add(pickedRoom.Id.IntegerValue);
                        TaskDialog.Show("Đã chọn",
                            $"Room: {pickedRoom.Name} (ID: {pickedRoom.Id.IntegerValue})\n" +
                            $"Tổng: {rooms.Count} room đã chọn.\n\n" +
                            $"Tiếp tục click hoặc Esc để chạy.");
                    }
                    else
                    {
                        TaskDialog.Show("Trùng", "Room này đã chọn rồi!");
                    }
                }
                else
                {
                    TaskDialog.Show("Không tìm thấy",
                        "Không tìm thấy Room tại vị trí này.\nHãy click vào bên trong vùng có Room.");
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                break;
            }
        }

        return rooms;
    }
}
