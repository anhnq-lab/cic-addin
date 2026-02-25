using Autodesk.Revit.DB;

namespace CIC.BIM.Addin.FacilityMgmt;

/// <summary>
/// Defines the 8 FM Shared Parameters and their metadata.
/// These map 1-1 to the FacilityAsset schema in qa-bqldacn.
/// </summary>
public static class FMParameters
{
    /// <summary>Shared parameter group name in the .txt file</summary>
    public const string GroupName = "CIC_FacilityManagement";

    /// <summary>Parameter group in Revit Properties panel</summary>
    public static readonly ForgeTypeId ParameterGroup = GroupTypeId.Data;

    /// <summary>
    /// All FM parameter definitions.
    /// GUIDs are fixed to ensure consistency across projects.
    /// </summary>
    public static readonly FMParamDef[] All = new FMParamDef[]
    {
        new("CIC_FM_AssetCode",        new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567801"), SpecTypeId.String.Text, "Mã tài sản (VD: HVAC-T2-001)"),
        new("CIC_FM_Category",         new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567802"), SpecTypeId.String.Text, "Phân loại: Cơ điện, PCCC, HVAC, Thang máy,..."),
        new("CIC_FM_Location",         new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567803"), SpecTypeId.String.Text, "Vị trí (Room/Space)"),
        new("CIC_FM_Manufacturer",     new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567804"), SpecTypeId.String.Text, "Nhà sản xuất"),
        new("CIC_FM_Model",            new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567805"), SpecTypeId.String.Text, "Model thiết bị"),
        new("CIC_FM_MaintenanceCycle", new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567806"), SpecTypeId.Int.Integer, "Chu kỳ bảo trì (ngày)"),
        new("CIC_FM_Status",           new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567807"), SpecTypeId.String.Text, "Trạng thái: Active/Maintenance/Broken/Retired"),
        new("CIC_FM_Condition",        new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567808"), SpecTypeId.String.Text, "Tình trạng: Good/Fair/Poor/Critical"),
    };

    /// <summary>
    /// MEP categories to which FM parameters will be bound.
    /// </summary>
    public static readonly BuiltInCategory[] TargetCategories = new[]
    {
        BuiltInCategory.OST_MechanicalEquipment,
        BuiltInCategory.OST_ElectricalEquipment,
        BuiltInCategory.OST_PlumbingFixtures,
        BuiltInCategory.OST_Sprinklers,
        BuiltInCategory.OST_LightingFixtures,
        BuiltInCategory.OST_ElectricalFixtures,
        BuiltInCategory.OST_CommunicationDevices,
        BuiltInCategory.OST_SecurityDevices,
        BuiltInCategory.OST_DataDevices,
        BuiltInCategory.OST_FireAlarmDevices,
        BuiltInCategory.OST_NurseCallDevices,
        BuiltInCategory.OST_TelephoneDevices,
    };
}

/// <summary>
/// Definition of a single FM shared parameter.
/// </summary>
public record FMParamDef(
    string Name,
    Guid Guid,
    ForgeTypeId DataType,
    string Description
);
