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
                TaskDialog.Show("Tuong hoan thien", $"Khong tim thay Room nao co Parameter '{paramName}' chua gia tri '{expectedValue}'.");
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
            TaskDialog.Show("Tuong hoan thien", "Chua chon Room nao.");
            return Result.Cancelled;
        }

        // Step 3: Build options from window
        var options = new WallFinishService.WallFinishOptions
        {
            WallTypeId = window.SelectedWallTypeId,
            HeightMm = window.HeightMm,
            BaseOffsetMm = window.BaseOffsetMm,
            JoinWithOriginal = window.JoinWithOriginal,
            CreateFloorFinish = window.CreateFloorFinish,
            FloorTypeId = window.SelectedFloorTypeId,
            FloorOffsetMm = window.FloorOffsetMm
        };

        // Step 4: Execute
        var service = new WallFinishService();
        var result = service.Execute(doc, rooms, options);

        // Step 5: Show results
        var msg = $"Hoan tat!\n" +
                  $"Room da xu ly: {result.RoomsProcessed}\n" +
                  $"Tuong tao moi: {result.WallsCreated}\n" +
                  $"San tao moi: {result.FloorsCreated}";

        if (result.Warnings.Count > 0)
        {
            msg += $"\n\nCanh bao ({result.Warnings.Count}):\n";
            msg += string.Join("\n", result.Warnings.Take(10));
            if (result.Warnings.Count > 10)
                msg += $"\n... va {result.Warnings.Count - 10} canh bao khac";
        }

        TaskDialog.Show("Tuong hoan thien", msg);
        return Result.Succeeded;
    }

    /// <summary>
    /// Pick Rooms by clicking INSIDE the room area.
    /// Uses PickPoint → GetRoomAtPoint (vì Room là spatial element, không click trực tiếp được).
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
        TaskDialog.Show("Chon Room",
            "Click vao BEN TRONG phong de chon Room.\n" +
            "Nhan Esc de ket thuc chon.");

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
                var point = uiDoc.Selection.PickPoint("Click trong phong (Esc de ket thuc)");

                // Find Room at picked point
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
                        TaskDialog.Show("Da chon",
                            $"Room: {pickedRoom.Name} (ID: {pickedRoom.Id.IntegerValue})\n" +
                            $"Tong: {rooms.Count} room da chon.\n\n" +
                            $"Tiep tuc click hoac Esc de chay.");
                    }
                    else
                    {
                        TaskDialog.Show("Trung", "Room nay da chon roi!");
                    }
                }
                else
                {
                    TaskDialog.Show("Khong tim thay",
                        "Khong tim thay Room tai vi tri nay.\nHay click vao ben trong vung co Room.");
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
