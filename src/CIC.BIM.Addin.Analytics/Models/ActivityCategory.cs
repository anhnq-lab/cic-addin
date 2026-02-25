namespace CIC.BIM.Addin.Analytics.Models;

/// <summary>
/// Classification of user activity in Revit
/// </summary>
public enum ActivityCategory
{
    /// <summary>Creating/deleting model elements (walls, columns, beams, pipes...)</summary>
    Modeling,

    /// <summary>Modifying properties, moving, rotating, mirroring elements</summary>
    Editing,

    /// <summary>Navigating views, zooming, panning (no model changes)</summary>
    Viewing,

    /// <summary>Creating sheets, dimensions, tags, annotations</summary>
    Documenting,

    /// <summary>File operations: open, save, sync, export</summary>
    FileOps,

    /// <summary>No user activity detected for > idle threshold</summary>
    Idle,

    /// <summary>Waiting on dialog box or warning</summary>
    DialogWait
}
