using System.IO;
using Autodesk.Revit.DB;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Service for standardized IFC export with FM property set support.
/// </summary>
public static class IFCExportService
{
    /// <summary>IFC export configuration.</summary>
    public class ExportConfig
    {
        public IFCVersion Version { get; set; } = IFCVersion.IFC2x3CV2;
        public ElementId? FilterViewId { get; set; }
        public string OutputFolder { get; set; } = "";
        public string FileName { get; set; } = "";
        public bool ExportBaseQuantities { get; set; } = true;
        public bool WallAndColumnSplitting { get; set; } = true;
        public bool VisibleElementsOnly { get; set; } = false;
        public bool IncludeFMProperties { get; set; } = true;
        public bool ExportRevitPropertySets { get; set; } = true;
    }

    /// <summary>Get all 3D views in the document.</summary>
    public static List<View3D> GetAvailable3DViews(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .Where(v => !v.IsTemplate)
            .OrderBy(v => v.Name)
            .ToList();
    }

    /// <summary>Get all floor plan views.</summary>
    public static List<ViewPlan> GetFloorPlanViews(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewPlan))
            .Cast<ViewPlan>()
            .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan)
            .OrderBy(v => v.Name)
            .ToList();
    }

    /// <summary>Generate a default file name from the document.</summary>
    public static string GenerateDefaultFileName(Document doc)
    {
        var title = doc.Title;
        if (string.IsNullOrEmpty(title))
            title = "Untitled";

        foreach (var c in Path.GetInvalidFileNameChars())
            title = title.Replace(c, '_');

        return $"{title}_IFC";
    }

    /// <summary>Generate default output folder from the document path.</summary>
    public static string GenerateDefaultOutputFolder(Document doc)
    {
        if (!string.IsNullOrEmpty(doc.PathName))
            return Path.GetDirectoryName(doc.PathName) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    /// <summary>
    /// Create the IFC Property Set Definition file for FM parameters.
    /// This file tells Revit which parameters to export as IFC property sets.
    /// Format: PropertySet: &lt;Name&gt; I &lt;IFC entity types&gt;
    ///         &lt;Property Name&gt;\t&lt;Data Type&gt;\t&lt;Revit Parameter Name&gt;
    /// </summary>
    public static string CreateFMPropertySetFile()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var cicDir = Path.Combine(appData, "CIC", "BIM.Addin");
        if (!Directory.Exists(cicDir))
            Directory.CreateDirectory(cicDir);

        var filePath = Path.Combine(cicDir, "CIC_FM_IFC_PropertySets.txt");

        // IFC Property Set Definition format:
        // PropertySet: <PsetName> I[nstance]/T[ype] <comma-separated IFC entity list>
        // <tab> <PropertyName> <tab> <DataType> <tab> <RevitParameterName>
        // 
        // DataType: Text, Real, Integer, Boolean
        // I = Instance, T = Type
        var content = @"
PropertySet:	Pset_CIC_FacilityManagement	I	IfcDistributionElement,IfcEnergyConversionDevice,IfcFlowController,IfcFlowFitting,IfcFlowMovingDevice,IfcFlowSegment,IfcFlowStorageDevice,IfcFlowTerminal,IfcFlowTreatmentDevice,IfcDistributionControlElement,IfcBuildingElementProxy,IfcFurnishingElement
	AssetCode	Text	CIC_FM_AssetCode
	Category	Text	CIC_FM_Category
	Location	Text	CIC_FM_Location
	Manufacturer	Text	CIC_FM_Manufacturer
	Model	Text	CIC_FM_Model
	Status	Text	CIC_FM_Status
	Condition	Text	CIC_FM_Condition
	MaintenanceCycle	Integer	CIC_FM_MaintenanceCycle
".TrimStart();

        File.WriteAllText(filePath, content, System.Text.Encoding.UTF8);
        return filePath;
    }

    /// <summary>
    /// Export the document to IFC with the given configuration.
    /// Returns the full path of the exported file, or null on failure.
    /// </summary>
    public static string? ExportIFC(Document doc, ExportConfig config)
    {
        var options = new IFCExportOptions();

        // IFC version
        options.FileVersion = config.Version;

        // Base quantities
        options.ExportBaseQuantities = config.ExportBaseQuantities;

        // Wall/column splitting
        options.WallAndColumnSplitting = config.WallAndColumnSplitting;

        // View filter
        if (config.FilterViewId != null && config.FilterViewId != ElementId.InvalidElementId)
        {
            options.FilterViewId = config.FilterViewId;
        }

        // Export Revit internal property sets
        if (config.ExportRevitPropertySets)
        {
            options.AddOption("ExportInternalRevitPropertySets", "true");
        }

        // Export FM properties using a Property Set Definition file
        if (config.IncludeFMProperties)
        {
            options.AddOption("ExportUserDefinedPsets", "true");

            // Create the mapping file that tells Revit how to export CIC_FM_* params
            var psetFile = CreateFMPropertySetFile();
            options.AddOption("ExportUserDefinedPsetsFileName", psetFile);
        }

        // Visible elements only
        if (config.VisibleElementsOnly)
        {
            options.AddOption("VisibleElementsOfCurrentView", "true");
        }

        // Space boundaries
        options.SpaceBoundaryLevel = 1;

        // Ensure output directory exists
        if (!Directory.Exists(config.OutputFolder))
            Directory.CreateDirectory(config.OutputFolder);

        // Transaction required for export
        using var tx = new Transaction(doc, "CIC - Xuất IFC");
        tx.Start();

        var success = doc.Export(config.OutputFolder, config.FileName, options);

        tx.Commit();

        if (success)
        {
            var fullPath = Path.Combine(config.OutputFolder, config.FileName + ".ifc");
            return fullPath;
        }

        return null;
    }

    /// <summary>Map IFCVersion to display name.</summary>
    public static readonly Dictionary<string, IFCVersion> VersionMap = new()
    {
        { "IFC 2x3 Coordination View 2.0", IFCVersion.IFC2x3CV2 },
        { "IFC 2x3", IFCVersion.IFC2x3 },
        { "IFC 4", IFCVersion.IFC4 },
        { "IFC 4 Reference View", IFCVersion.IFC4RV },
        { "IFC 4 Design Transfer View", IFCVersion.IFC4DTV },
    };
}
