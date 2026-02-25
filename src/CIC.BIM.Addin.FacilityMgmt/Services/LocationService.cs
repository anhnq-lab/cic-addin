using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;

namespace CIC.BIM.Addin.FacilityMgmt.Services;

/// <summary>
/// Provides location information for elements based on Room/Space containment.
/// </summary>
public static class LocationService
{
    /// <summary>
    /// Get the location string for an element by finding the Room/Space that contains it.
    /// Format: "{Room Name} - {Level Name}"
    /// If element spans multiple rooms, returns comma-separated list.
    /// </summary>
    public static string? GetElementLocation(Element element, Document doc)
    {
        // Try to get location point
        var locationPoint = GetElementLocationPoint(element);
        if (locationPoint == null) return null;

        // Try Room first (Architecture)
        var room = GetRoomAtPoint(doc, locationPoint);
        if (room != null)
        {
            var roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name;
            var levelName = GetLevelName(doc, room.LevelId);
            return FormatLocation(roomName, levelName);
        }

        // Try Space (MEP)
        var space = GetSpaceAtPoint(doc, locationPoint);
        if (space != null)
        {
            var spaceName = space.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? space.Name;
            var levelName = GetLevelName(doc, space.LevelId);
            return FormatLocation(spaceName, levelName);
        }

        // Fallback: just use Level name
        var elemLevel = GetElementLevel(element, doc);
        if (elemLevel != null)
        {
            return elemLevel.Name;
        }

        return null;
    }

    /// <summary>
    /// Get the XYZ location point of an element.
    /// </summary>
    private static XYZ? GetElementLocationPoint(Element element)
    {
        if (element.Location is LocationPoint lp)
            return lp.Point;

        if (element.Location is LocationCurve lc)
            return lc.Curve.Evaluate(0.5, true); // Midpoint

        // Try bounding box center
        var bb = element.get_BoundingBox(null);
        if (bb != null)
            return (bb.Min + bb.Max) / 2.0;

        return null;
    }

    /// <summary>
    /// Find Room at a given point using phase of the document.
    /// </summary>
    private static Room? GetRoomAtPoint(Document doc, XYZ point)
    {
        try
        {
            // Get the last phase
            var phases = new FilteredElementCollector(doc)
                .OfClass(typeof(Phase))
                .Cast<Phase>()
                .ToList();

            if (phases.Count == 0) return null;

            var lastPhase = phases.Last();
            return doc.GetRoomAtPoint(point, lastPhase);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Find MEP Space at a given point.
    /// </summary>
    private static Space? GetSpaceAtPoint(Document doc, XYZ point)
    {
        try
        {
            // Search for spaces that contain this point
            var spaces = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_MEPSpaces)
                .WhereElementIsNotElementType()
                .Cast<Space>()
                .ToList();

            foreach (var space in spaces)
            {
                if (space.IsPointInSpace(point))
                    return space;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Get the Level of an element.
    /// </summary>
    private static Level? GetElementLevel(Element element, Document doc)
    {
        // Try LevelId
        if (element.LevelId != ElementId.InvalidElementId)
        {
            return doc.GetElement(element.LevelId) as Level;
        }

        // Try FAMILY_LEVEL_PARAM
        var levelParam = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
        if (levelParam != null && levelParam.AsElementId() != ElementId.InvalidElementId)
        {
            return doc.GetElement(levelParam.AsElementId()) as Level;
        }

        // Try RBS_START_LEVEL_PARAM (MEP)
        var startLevel = element.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
        if (startLevel != null && startLevel.AsElementId() != ElementId.InvalidElementId)
        {
            return doc.GetElement(startLevel.AsElementId()) as Level;
        }

        return null;
    }

    /// <summary>Get level name from level ID.</summary>
    private static string GetLevelName(Document doc, ElementId levelId)
    {
        if (levelId == ElementId.InvalidElementId) return "";
        var level = doc.GetElement(levelId) as Level;
        return level?.Name ?? "";
    }

    /// <summary>Format location string.</summary>
    private static string FormatLocation(string roomName, string levelName)
    {
        if (string.IsNullOrWhiteSpace(levelName))
            return roomName;
        return $"{roomName} - {levelName}";
    }
}
