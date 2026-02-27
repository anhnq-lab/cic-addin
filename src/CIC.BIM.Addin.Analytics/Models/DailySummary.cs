namespace CIC.BIM.Addin.Analytics.Models;

/// <summary>
/// Aggregated daily summary for dashboard and Supabase sync
/// </summary>
public class DailySummary
{
    public string? Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public DateTime SummaryDate { get; set; }
    public double ModelingMinutes { get; set; }
    public double EditingMinutes { get; set; }
    public double ViewingMinutes { get; set; }
    public double DocumentingMinutes { get; set; }
    public double FileOpsMinutes { get; set; }
    public double CoordinatingMinutes { get; set; }
    public double IdleMinutes { get; set; }
    public double DialogWaitMinutes { get; set; }
    public double TotalActiveMinutes { get; set; }
    public int TotalSessions { get; set; }
    public string? TopWorkflowsJson { get; set; }

    // ═══ Element productivity metrics ═══
    public int ElementsCreated { get; set; }
    public int ElementsModified { get; set; }
    public int ElementsDeleted { get; set; }

    /// <summary>Elements (created+modified) per active hour — core productivity metric</summary>
    public double ElementsPerActiveHour { get; set; }

    /// <summary>Department of this user</summary>
    public string? DepartmentName { get; set; }
}
