using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using CIC.BIM.Addin.Analytics.Models;

namespace CIC.BIM.Addin.Analytics.Services;

/// <summary>
/// Classifies Revit events into ActivityCategory based on element types and transaction names.
/// </summary>
public static class EventClassifier
{
    // Annotation category IDs for distinguishing Documenting vs Modeling
    private static readonly HashSet<int> AnnotationCategoryIds = new()
    {
        (int)BuiltInCategory.OST_Dimensions,
        (int)BuiltInCategory.OST_TextNotes,
        (int)BuiltInCategory.OST_GenericAnnotation,
        (int)BuiltInCategory.OST_DetailComponents,
        (int)BuiltInCategory.OST_Tags,
        (int)BuiltInCategory.OST_KeynoteTags,
        (int)BuiltInCategory.OST_MaterialTags,
        (int)BuiltInCategory.OST_SpotElevSymbols,
        (int)BuiltInCategory.OST_SpotCoordinateSymbols,
        (int)BuiltInCategory.OST_RevisionClouds,
        (int)BuiltInCategory.OST_Sheets,
        (int)BuiltInCategory.OST_TitleBlocks,
        (int)BuiltInCategory.OST_Viewports,
        (int)BuiltInCategory.OST_Schedules,
    };

    // Map BuiltInCategory → human-readable sub-category
    private static readonly Dictionary<BuiltInCategory, string> SubCategoryMap = new()
    {
        { BuiltInCategory.OST_Walls, "Wall" },
        { BuiltInCategory.OST_Floors, "Floor" },
        { BuiltInCategory.OST_StructuralColumns, "Column" },
        { BuiltInCategory.OST_StructuralFraming, "Beam" },
        { BuiltInCategory.OST_StructuralFoundation, "Foundation" },
        { BuiltInCategory.OST_Columns, "Column" },
        { BuiltInCategory.OST_Roofs, "Roof" },
        { BuiltInCategory.OST_Stairs, "Stairs" },
        { BuiltInCategory.OST_Doors, "Door" },
        { BuiltInCategory.OST_Windows, "Window" },
        { BuiltInCategory.OST_Ceilings, "Ceiling" },
        { BuiltInCategory.OST_MechanicalEquipment, "MEP-HVAC" },
        { BuiltInCategory.OST_ElectricalEquipment, "MEP-Electrical" },
        { BuiltInCategory.OST_PlumbingFixtures, "MEP-Plumbing" },
        { BuiltInCategory.OST_Sprinklers, "MEP-FireProtection" },
        { BuiltInCategory.OST_PipeSegments, "Pipe" },
        { BuiltInCategory.OST_PipeCurves, "Pipe" },
        { BuiltInCategory.OST_DuctCurves, "Duct" },
        { BuiltInCategory.OST_Conduit, "Conduit" },
        { BuiltInCategory.OST_CableTray, "CableTray" },
        { BuiltInCategory.OST_LightingFixtures, "Lighting" },
        { BuiltInCategory.OST_Rebar, "Rebar" },
        { BuiltInCategory.OST_Dimensions, "Dimension" },
        { BuiltInCategory.OST_TextNotes, "Text" },
        { BuiltInCategory.OST_Tags, "Tag" },
        { BuiltInCategory.OST_Sheets, "Sheet" },
        { BuiltInCategory.OST_Views, "View" },
        { BuiltInCategory.OST_Rooms, "Room" },
        { BuiltInCategory.OST_Areas, "Area" },
    };

    /// <summary>
    /// Classify a DocumentChanged event into an ActivityEvent
    /// </summary>
    public static ActivityEvent ClassifyDocumentChanged(
        DocumentChangedEventArgs args,
        string sessionId,
        string? activeViewName,
        string? activeViewType)
    {
        var doc = args.GetDocument();
        var addedIds = args.GetAddedElementIds();
        var modifiedIds = args.GetModifiedElementIds();
        var deletedIds = args.GetDeletedElementIds();
        var txnNames = args.GetTransactionNames();

        var totalElements = addedIds.Count + modifiedIds.Count + deletedIds.Count;
        var txnString = string.Join(", ", txnNames);

        // Determine category and sub-category
        var category = ActivityCategory.Editing; // default
        string? subCategory = null;

        if (addedIds.Count > 0)
        {
            // Check if added elements are annotations or model elements
            var (isAnnotation, detectedSub) = AnalyzeElements(doc, addedIds);
            category = isAnnotation ? ActivityCategory.Documenting : ActivityCategory.Modeling;
            subCategory = detectedSub;
        }
        else if (modifiedIds.Count > 0)
        {
            var (isAnnotation, detectedSub) = AnalyzeElements(doc, modifiedIds);
            category = isAnnotation ? ActivityCategory.Documenting : ActivityCategory.Editing;
            subCategory = detectedSub;
        }
        else if (deletedIds.Count > 0)
        {
            // Deleted elements can't be queried, use transaction name as hint
            category = ActivityCategory.Modeling;
            subCategory = InferSubCategoryFromTransaction(txnString);
        }

        return new ActivityEvent
        {
            SessionId = sessionId,
            Timestamp = DateTime.UtcNow,
            Category = category,
            SubCategory = subCategory,
            ElementCount = totalElements,
            TransactionNames = txnString,
            ActiveViewName = activeViewName,
            ActiveViewType = activeViewType
        };
    }

    /// <summary>
    /// Analyze element IDs to determine if they are annotations and detect sub-category
    /// </summary>
    private static (bool IsAnnotation, string? SubCategory) AnalyzeElements(
        Document doc, ICollection<ElementId> elementIds)
    {
        int annotationCount = 0;
        int modelCount = 0;
        var subCategories = new Dictionary<string, int>();

        foreach (var id in elementIds)
        {
            try
            {
                var element = doc.GetElement(id);
                if (element == null) continue;

                var catId = element.Category?.Id.IntegerValue ?? 0;

                if (AnnotationCategoryIds.Contains(catId))
                {
                    annotationCount++;
                }
                else
                {
                    modelCount++;
                }

                // Track sub-category frequency
                if (element.Category != null)
                {
                    var builtIn = (BuiltInCategory)element.Category.Id.IntegerValue;
                    if (SubCategoryMap.TryGetValue(builtIn, out var sub))
                    {
                        subCategories[sub] = subCategories.GetValueOrDefault(sub, 0) + 1;
                    }
                }
            }
            catch
            {
                // Element might be invalid, skip
            }
        }

        bool isAnnotation = annotationCount > modelCount;
        string? topSub = subCategories.Count > 0
            ? subCategories.OrderByDescending(kv => kv.Value).First().Key
            : null;

        return (isAnnotation, topSub);
    }

    /// <summary>
    /// Try to infer sub-category from transaction name string
    /// </summary>
    private static string? InferSubCategoryFromTransaction(string txnNames)
    {
        var lower = txnNames.ToLowerInvariant();
        if (lower.Contains("wall")) return "Wall";
        if (lower.Contains("floor") || lower.Contains("slab")) return "Floor";
        if (lower.Contains("column")) return "Column";
        if (lower.Contains("beam") || lower.Contains("framing")) return "Beam";
        if (lower.Contains("door")) return "Door";
        if (lower.Contains("window")) return "Window";
        if (lower.Contains("pipe")) return "Pipe";
        if (lower.Contains("duct")) return "Duct";
        if (lower.Contains("dimension") || lower.Contains("dim")) return "Dimension";
        if (lower.Contains("tag")) return "Tag";
        if (lower.Contains("sheet")) return "Sheet";
        if (lower.Contains("rebar")) return "Rebar";
        return null;
    }

    /// <summary>
    /// Map Revit ViewType to display string
    /// </summary>
    public static string GetViewTypeString(ViewType viewType)
    {
        return viewType switch
        {
            ViewType.FloorPlan => "FloorPlan",
            ViewType.CeilingPlan => "CeilingPlan",
            ViewType.Section => "Section",
            ViewType.Elevation => "Elevation",
            ViewType.ThreeD => "3D",
            ViewType.DrawingSheet => "Sheet",
            ViewType.Schedule => "Schedule",
            ViewType.Detail => "Detail",
            ViewType.Legend => "Legend",
            ViewType.DraftingView => "Drafting",
            _ => viewType.ToString()
        };
    }
}
