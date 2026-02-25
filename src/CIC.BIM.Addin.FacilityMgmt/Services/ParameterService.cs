using System.IO;
using System.Text;
using Autodesk.Revit.DB;

namespace CIC.BIM.Addin.FacilityMgmt.Services;

/// <summary>
/// Service for creating, reading, and managing Shared Parameters.
/// Uses the correct Revit shared parameter file format (matching ATool/Revit standards).
/// </summary>
public static class ParameterService
{
    private static string? _originalSharedParamFile;

    /// <summary>
    /// Ensures the shared parameter file exists with correct Revit format.
    /// Format reference: ATool and Revit Steel_Properties shared param files.
    /// 
    /// Key insight: *META is a HEADER DESCRIPTOR, META is the DATA ROW.
    /// Same pattern for *GROUP/GROUP and *PARAM/PARAM.
    /// </summary>
    public static string EnsureSharedParamFile(Autodesk.Revit.ApplicationServices.Application app)
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var cicFolder = Path.Combine(appDataPath, "CIC", "BIM Addin");
        Directory.CreateDirectory(cicFolder);

        var paramFilePath = Path.Combine(cicFolder, "CIC_FM_SharedParams.txt");

        // Always recreate to ensure clean correct format
        if (File.Exists(paramFilePath))
        {
            File.Delete(paramFilePath);
        }

        // Write file in exact Revit format (matching ATool/Revit standard)
        // MUST use tabs as separators
        // *LINE = header descriptor, LINE = data row
        var sb = new StringBuilder();
        sb.AppendLine("# This is a Revit shared parameter file.");
        sb.AppendLine("# Do not edit manually.");

        // META section: "*META" is header, "META" is data
        sb.AppendLine("*META\tVERSION\tMINVERSION");
        sb.AppendLine("META\t2\t1");

        // GROUP section
        sb.AppendLine("*GROUP\tID\tNAME");
        sb.AppendLine($"GROUP\t1\t{FMParameters.GroupName}");

        // PARAM section header
        sb.AppendLine("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE");

        // PARAM data rows
        foreach (var p in FMParameters.All)
        {
            var dataType = p.DataType == SpecTypeId.Int.Integer ? "INTEGER" : "TEXT";
            // Format: PARAM guid name datatype datacategory(empty) group visible description usermod hidewhennovalue
            sb.AppendLine($"PARAM\t{p.Guid}\t{p.Name}\t{dataType}\t\t1\t1\t{p.Description}\t1\t0");
        }

        // Write with ASCII encoding (standard for Revit shared param files)
        File.WriteAllText(paramFilePath, sb.ToString(), Encoding.ASCII);

        // Backup user's current shared param file
        _originalSharedParamFile = app.SharedParametersFilename;

        // Set our file as current
        app.SharedParametersFilename = paramFilePath;
        return paramFilePath;
    }

    /// <summary>
    /// Restore user's original shared parameter file.
    /// </summary>
    public static void RestoreOriginalParamFile(Autodesk.Revit.ApplicationServices.Application app)
    {
        if (!string.IsNullOrEmpty(_originalSharedParamFile))
        {
            try { app.SharedParametersFilename = _originalSharedParamFile; }
            catch { }
        }
    }

    /// <summary>Set a string parameter value on an element.</summary>
    public static bool SetStringParam(Element element, string paramName, string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var param = element.LookupParameter(paramName);
        if (param == null || param.IsReadOnly) return false;
        try { param.Set(value); return true; }
        catch { return false; }
    }

    /// <summary>Set an integer parameter value on an element.</summary>
    public static bool SetIntParam(Element element, string paramName, int value)
    {
        var param = element.LookupParameter(paramName);
        if (param == null || param.IsReadOnly) return false;
        try { param.Set(value); return true; }
        catch { return false; }
    }

    /// <summary>Get a string parameter value from an element.</summary>
    public static string? GetStringParam(Element element, string paramName)
    {
        var param = element.LookupParameter(paramName);
        return param?.AsString();
    }

    /// <summary>Get an integer parameter value from an element.</summary>
    public static int? GetIntParam(Element element, string paramName)
    {
        var param = element.LookupParameter(paramName);
        if (param == null) return null;
        return param.AsInteger();
    }
}
