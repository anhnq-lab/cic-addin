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
public class FinishingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        // Show finishing window
        var window = new FinishingWindow(doc);
        window.ShowDialog();

        if (window.DialogResult != true)
            return Result.Cancelled;

        // Dispatch based on active tab
        switch (window.ActiveTab)
        {
            case FinishingTab.WallFinish:
                return ExecuteWallFinish(uiDoc, doc, window);

            case FinishingTab.BeamColumnFinish:
                return ExecuteBeamColFinish(uiDoc, doc, window);

            case FinishingTab.FloorFinish:
                return ExecuteFloorFinish(uiDoc, doc, window);

            default:
                return Result.Cancelled;
        }
    }

    // ═══ Tab 1: Wall Finish ═══
    private Result ExecuteWallFinish(UIDocument uiDoc, Document doc, FinishingWindow window)
    {
        var rooms = GetRoomsFromSelection(uiDoc, doc,
            window.WallSelectionMethod, window.WallSelectedParameter, window.WallParameterValue);

        if (rooms.Count == 0)
        {
            TaskDialog.Show("Tường hoàn thiện", "Chưa chọn Room nào.");
            return Result.Cancelled;
        }

        var options = new WallFinishService.WallFinishOptions
        {
            WallTypeId = window.SelectedWallTypeId,
            HeightMm = window.HeightMm,
            BaseOffsetMm = window.BaseOffsetMm,
            OffsetMm = window.BoundaryOffsetMm,
            JoinWithOriginal = window.JoinWithOriginal,
            AssignRoomName = window.AssignRoomName,
            RoomNameParam = window.RoomNameParam,
            CreateFloorFinish = false // Sàn giờ có tab riêng
        };

        var service = new WallFinishService();
        var result = service.Execute(doc, rooms, options);

        var msg = $"Hoàn tất!\n" +
                  $"Room đã xử lý: {result.RoomsProcessed}\n" +
                  $"Tường tạo mới: {result.WallsCreated}\n";

        if (options.OffsetMm != 0)
            msg += $"Offset biên dạng: {options.OffsetMm} mm\n";

        AppendWarnings(ref msg, result.Warnings);
        TaskDialog.Show("CIC Tool - Tường hoàn thiện", msg);
        return Result.Succeeded;
    }

    // ═══ Tab 3: Beam/Column Finish ═══
    private Result ExecuteBeamColFinish(UIDocument uiDoc, Document doc, FinishingWindow window)
    {
        var elements = new List<Element>();

        switch (window.BeamColMethod)
        {
            case FinishingWindow.BeamColSelectionMethod.AllBeams:
                elements = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .ToList();
                break;

            case FinishingWindow.BeamColSelectionMethod.AllColumns:
                elements = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WherePasses(new LogicalOrFilter(
                        new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns),
                        new ElementCategoryFilter(BuiltInCategory.OST_Columns)))
                    .WhereElementIsNotElementType()
                    .ToList();
                break;

            case FinishingWindow.BeamColSelectionMethod.AllBoth:
                var beams = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .ToList();
                var cols = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WherePasses(new LogicalOrFilter(
                        new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns),
                        new ElementCategoryFilter(BuiltInCategory.OST_Columns)))
                    .WhereElementIsNotElementType()
                    .ToList();
                elements.AddRange(beams);
                elements.AddRange(cols);
                break;

            default: // Pick
                elements = PickElements(uiDoc);
                break;
        }

        if (elements.Count == 0)
        {
            TaskDialog.Show("Dầm/Cột hoàn thiện", "Chưa chọn cấu kiện nào.");
            return Result.Cancelled;
        }

        var options = new BeamColumnFinishService.BeamColFinishOptions
        {
            WallTypeId = window.BeamColWallTypeId,
            JoinWithOriginal = window.BeamColJoinWithOriginal,
            IncludeBeamBottom = window.IncludeBeamBottom,
            OffsetMm = window.BeamColOffsetMm
        };

        var service = new BeamColumnFinishService();
        var result = service.Execute(doc, elements, options);

        var msg = $"Hoàn tất!\n" +
                  $"Cấu kiện đã xử lý: {result.ElementsProcessed}\n" +
                  $"Tường bọc tạo mới: {result.WallsCreated}\n";

        AppendWarnings(ref msg, result.Warnings);
        TaskDialog.Show("CIC Tool - Dầm/Cột hoàn thiện", msg);
        return Result.Succeeded;
    }

    // ═══ Tab 4: Floor Finish ═══
    private Result ExecuteFloorFinish(UIDocument uiDoc, Document doc, FinishingWindow window)
    {
        var rooms = GetRoomsFromSelection(uiDoc, doc,
            window.FloorSelectionMethod, window.FloorSelectedParameter, window.FloorParameterValue);

        if (rooms.Count == 0)
        {
            TaskDialog.Show("Sàn hoàn thiện", "Chưa chọn Room nào.");
            return Result.Cancelled;
        }

        // Reuse WallFinishService with floor-only options
        var options = new WallFinishService.WallFinishOptions
        {
            WallTypeId = ElementId.InvalidElementId, // Không tạo tường
            HeightMm = 0,
            BaseOffsetMm = 0,
            OffsetMm = window.FloorBoundaryOffsetMm,
            JoinWithOriginal = false,
            AssignRoomName = window.FloorAssignRoomName,
            RoomNameParam = "Comments",
            CreateFloorFinish = true,
            FloorTypeId = window.SelectedFloorTypeId,
            FloorOffsetMm = window.FloorOffsetMm
        };

        var service = new WallFinishService();
        var result = service.ExecuteFloorOnly(doc, rooms, options);

        var msg = $"Hoàn tất!\n" +
                  $"Room đã xử lý: {result.RoomsProcessed}\n" +
                  $"Sàn tạo mới: {result.FloorsCreated}\n";

        AppendWarnings(ref msg, result.Warnings);
        TaskDialog.Show("CIC Tool - Sàn hoàn thiện", msg);
        return Result.Succeeded;
    }

    // ═══ Helpers ═══
    private List<Room> GetRoomsFromSelection(UIDocument uiDoc, Document doc,
        RoomSelectionMethod method, string paramName, string paramValue)
    {
        var rooms = new List<Room>();

        switch (method)
        {
            case RoomSelectionMethod.AllInView:
                rooms = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0)
                    .ToList();
                break;

            case RoomSelectionMethod.ByParameter:
                var allRooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0)
                    .ToList();

                foreach (var r in allRooms)
                {
                    var p = r.GetParameters(paramName).FirstOrDefault();
                    if (p != null)
                    {
                        string pValue = p.AsValueString() ?? p.AsString() ?? "";
                        if (pValue.IndexOf(paramValue ?? "", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            rooms.Add(r);
                    }
                }

                if (rooms.Count == 0)
                {
                    TaskDialog.Show("Hoàn thiện",
                        $"Không tìm thấy Room nào có Parameter '{paramName}' chứa giá trị '{paramValue}'.");
                }
                break;

            default: // PickRooms
                rooms = PickRoomsByPoint(uiDoc);
                break;
        }

        return rooms;
    }

    private static List<Room> PickRoomsByPoint(UIDocument uiDoc)
    {
        var rooms = new List<Room>();
        var doc = uiDoc.Document;
        var pickedIds = new HashSet<int>();

        // Check current selection
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

        TaskDialog.Show("Chọn Room",
            "Click vào BÊN TRONG phòng để chọn Room.\nNhấn Esc để kết thúc chọn.");

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

                Room? pickedRoom = phase != null
                    ? doc.GetRoomAtPoint(point, phase)
                    : doc.GetRoomAtPoint(point);

                if (pickedRoom != null && pickedRoom.Area > 0)
                {
                    if (!pickedIds.Contains(pickedRoom.Id.IntegerValue))
                    {
                        rooms.Add(pickedRoom);
                        pickedIds.Add(pickedRoom.Id.IntegerValue);
                        TaskDialog.Show("Đã chọn",
                            $"Room: {pickedRoom.Name} (ID: {pickedRoom.Id.IntegerValue})\n" +
                            $"Tổng: {rooms.Count} room đã chọn.\n\nTiếp tục click hoặc Esc để chạy.");
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

    private static List<Element> PickElements(UIDocument uiDoc)
    {
        var elements = new List<Element>();
        var doc = uiDoc.Document;

        // Check current selection
        var currentSelection = uiDoc.Selection.GetElementIds();
        if (currentSelection.Count > 0)
        {
            foreach (var id in currentSelection)
            {
                var elem = doc.GetElement(id);
                if (elem != null)
                {
                    var catId = elem.Category?.Id.IntegerValue ?? 0;
                    if (catId == (int)BuiltInCategory.OST_StructuralFraming ||
                        catId == (int)BuiltInCategory.OST_StructuralColumns ||
                        catId == (int)BuiltInCategory.OST_Columns)
                    {
                        elements.Add(elem);
                    }
                }
            }
        }

        if (elements.Count > 0)
            return elements;

        TaskDialog.Show("Chọn cấu kiện",
            "Click chọn Dầm/Cột cần tạo hoàn thiện.\nNhấn Esc để kết thúc chọn.");

        while (true)
        {
            try
            {
                var pickedRef = uiDoc.Selection.PickObject(
                    Autodesk.Revit.UI.Selection.ObjectType.Element,
                    "Chọn Dầm/Cột (Esc để kết thúc)");

                var elem = doc.GetElement(pickedRef.ElementId);
                if (elem != null)
                {
                    elements.Add(elem);
                    TaskDialog.Show("Đã chọn",
                        $"{elem.Name} (ID: {elem.Id.IntegerValue})\nTổng: {elements.Count} cấu kiện.");
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                break;
            }
        }

        return elements;
    }

    private static void AppendWarnings(ref string msg, List<string> warnings)
    {
        if (warnings.Count > 0)
        {
            msg += $"\nCảnh báo ({warnings.Count}):\n";
            msg += string.Join("\n", warnings.Take(20));
            if (warnings.Count > 20)
                msg += $"\n... và {warnings.Count - 20} cảnh báo khác";
        }
    }
}
