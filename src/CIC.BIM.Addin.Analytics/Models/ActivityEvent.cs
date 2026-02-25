namespace CIC.BIM.Addin.Analytics.Models;

/// <summary>
/// Represents a single activity event (aggregated every 30 seconds)
/// </summary>
public class ActivityEvent
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public ActivityCategory Category { get; set; }

    /// <summary>Sub-category: Wall, Column, Pipe, Sheet, etc.</summary>
    public string? SubCategory { get; set; }

    /// <summary>Number of elements affected</summary>
    public int ElementCount { get; set; }

    /// <summary>Duration of this activity in seconds (calculated from timestamps)</summary>
    public int DurationSeconds { get; set; }

    /// <summary>Comma-separated Revit transaction names</summary>
    public string? TransactionNames { get; set; }

    /// <summary>Active view name at time of event</summary>
    public string? ActiveViewName { get; set; }

    /// <summary>Active view type: FloorPlan, Section, 3D, Sheet, etc.</summary>
    public string? ActiveViewType { get; set; }

    /// <summary>Whether this event has been synced to Supabase</summary>
    public bool SyncedToCloud { get; set; }
}
