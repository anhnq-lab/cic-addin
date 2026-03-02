using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace CIC.BIM.Addin.Tools.Services;

public class SmartQTOResult
{
    public string LevelName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string FamilyAndType { get; set; } = string.Empty;
    public string SizeTag { get; set; } = string.Empty;
    public double VolumeM3 { get; set; }
    public double AreaM2 { get; set; }
    public double LengthM { get; set; }
    public int Count { get; set; }
    public string MaterialName { get; set; } = string.Empty;
}

public class SmartQTOService
{
    private readonly Document _doc;

    public SmartQTOService(Document doc)
    {
        _doc = doc;
    }

    public List<SmartQTOResult> CalculateQTO(List<BuiltInCategory> categories, bool onlySelection, ICollection<ElementId>? selectedIds, bool groupByLevel)
    {
        var results = new List<SmartQTOResult>();

        foreach (var category in categories)
        {
            var collector = new FilteredElementCollector(_doc);
            
            if (onlySelection && selectedIds != null && selectedIds.Any())
            {
                collector.WherePasses(new ElementIdSetFilter(selectedIds));
            }
            
            var elements = collector
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .ToElements();

            var categoryName = LabelUtils.GetLabelFor(category);

            // Group by Level (if requested) and Family Symbol / Type Name
            var grouped = elements.GroupBy(e => 
            {
                var typeId = e.GetTypeId();
                string typeName = e.Name;
                if (typeId != ElementId.InvalidElementId && _doc.GetElement(typeId) is ElementType type)
                {
                    var familyName = type.FamilyName;
                    typeName = string.IsNullOrEmpty(familyName) ? type.Name : $"{familyName} - {type.Name}";
                }
                
                string levelName = "Không xác định Tầng";
                if (groupByLevel)
                {
                    if (e.LevelId != ElementId.InvalidElementId && _doc.GetElement(e.LevelId) is Level lvl)
                        levelName = lvl.Name;
                    else
                        levelName = GetParamStringValue(e, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM) ?? "Không xác định Tầng";
                }

                // Append size for MEP
                string size = GetParamStringValue(e, BuiltInParameter.RBS_CALCULATED_SIZE) 
                           ?? GetParamStringValue(e, BuiltInParameter.RBS_REFERENCE_FREESIZE) 
                           ?? "";
                
                // For walls, let's extract width (thickness)
                if (category == BuiltInCategory.OST_Walls && string.IsNullOrEmpty(size))
                {
                    if (typeId != ElementId.InvalidElementId && _doc.GetElement(typeId) is WallType wType)
                        size = $"Dày {Math.Round(UnitUtils.ConvertFromInternalUnits(wType.Width, UnitTypeId.Millimeters))}mm";
                }

                return new { Level = levelName, Type = typeName, Size = size };
            });

            foreach (var group in grouped)
            {
                double totalVolume = 0;
                double totalArea = 0;
                double totalLength = 0;
                int count = 0;

                foreach (var element in group)
                {
                    count++;
                    totalVolume += GetParamValue(element, BuiltInParameter.HOST_VOLUME_COMPUTED);
                    totalArea += GetParamValue(element, BuiltInParameter.HOST_AREA_COMPUTED);
                    
                    // For framing/columns
                    var length = GetParamValue(element, BuiltInParameter.CURVE_ELEM_LENGTH);
                    if (length == 0) length = GetParamValue(element, BuiltInParameter.INSTANCE_LENGTH_PARAM);
                    totalLength += length;
                }

                // Convert internal units (cubic feet, square feet, feet) to metric
                results.Add(new SmartQTOResult
                {
                    LevelName = group.Key.Level,
                    CategoryName = categoryName,
                    FamilyAndType = group.Key.Type,
                    SizeTag = group.Key.Size,
                    Count = count,
                    VolumeM3 = UnitUtils.ConvertFromInternalUnits(totalVolume, UnitTypeId.CubicMeters),
                    AreaM2 = UnitUtils.ConvertFromInternalUnits(totalArea, UnitTypeId.SquareMeters),
                    LengthM = UnitUtils.ConvertFromInternalUnits(totalLength, UnitTypeId.Meters)
                });
            }
        }

        return results.OrderBy(x => x.LevelName).ThenBy(x => x.CategoryName).ThenBy(x => x.FamilyAndType).ToList();
    }

    private string? GetParamStringValue(Element element, BuiltInParameter bip)
    {
        var param = element.get_Parameter(bip);
        if (param != null && param.HasValue)
        {
            return param.AsValueString() ?? param.AsString();
        }
        return null;
    }

    private double GetParamValue(Element element, BuiltInParameter bip)
    {
        var param = element.get_Parameter(bip);
        if (param != null && param.HasValue)
        {
            return param.AsDouble();
        }
        return 0;
    }
}
