using Autodesk.Revit.DB;

namespace CIC.BIM.Addin.FacilityMgmt.Services;

/// <summary>
/// Maps Revit BuiltInCategory to FM Category.
/// Categories match ASSET_CATEGORIES in qa-bqldacn/facilityAssetService.ts
/// </summary>
public static class CategoryMappingService
{
    private static readonly Dictionary<BuiltInCategory, string> CategoryMap = new()
    {
        // HVAC
        { BuiltInCategory.OST_MechanicalEquipment, "HVAC" },

        // Cơ điện
        { BuiltInCategory.OST_ElectricalEquipment, "Cơ điện" },
        { BuiltInCategory.OST_ElectricalFixtures, "Cơ điện" },

        // Cấp thoát nước
        { BuiltInCategory.OST_PlumbingFixtures, "Cấp thoát nước" },

        // PCCC
        { BuiltInCategory.OST_Sprinklers, "PCCC" },
        { BuiltInCategory.OST_FireAlarmDevices, "PCCC" },

        // Điện chiếu sáng
        { BuiltInCategory.OST_LightingFixtures, "Điện chiếu sáng" },

        // Hệ thống IT/Mạng
        { BuiltInCategory.OST_CommunicationDevices, "Hệ thống IT/Mạng" },
        { BuiltInCategory.OST_DataDevices, "Hệ thống IT/Mạng" },
        { BuiltInCategory.OST_TelephoneDevices, "Hệ thống IT/Mạng" },

        // Camera/An ninh
        { BuiltInCategory.OST_SecurityDevices, "Camera/An ninh" },

        // Nurse Call
        { BuiltInCategory.OST_NurseCallDevices, "Cơ điện" },
    };

    /// <summary>
    /// Get FM category for a Revit element based on its BuiltInCategory.
    /// Also checks Family name for more specific categorization.
    /// </summary>
    public static string GetFMCategory(Element element)
    {
        var cat = element.Category;
        if (cat == null) return "Khác";

        var builtIn = (BuiltInCategory)cat.Id.Value;

        // Try direct mapping first
        if (CategoryMap.TryGetValue(builtIn, out var fmCategory))
        {
            // Refine based on family name for some categories
            var familyName = GetFamilyName(element)?.ToLower() ?? "";

            // Check if mechanical equipment is actually an elevator
            if (builtIn == BuiltInCategory.OST_MechanicalEquipment)
            {
                if (familyName.Contains("thang máy") || familyName.Contains("elevator") ||
                    familyName.Contains("lift") || familyName.Contains("escalator"))
                    return "Thang máy";

                if (familyName.Contains("máy phát") || familyName.Contains("generator") ||
                    familyName.Contains("genset"))
                    return "Máy phát điện";
            }

            return fmCategory;
        }

        return "Khác";
    }

    /// <summary>
    /// Get the Family name for an element (works for FamilyInstance).
    /// </summary>
    private static string? GetFamilyName(Element element)
    {
        if (element is FamilyInstance fi)
        {
            return fi.Symbol?.Family?.Name;
        }
        return null;
    }
}
