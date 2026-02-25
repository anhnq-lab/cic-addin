namespace CIC.BIM.Addin.Analytics.Models;

/// <summary>
/// Aggregated daily summary for Supabase sync
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
    public double IdleMinutes { get; set; }
    public double TotalActiveMinutes { get; set; }
    public int TotalSessions { get; set; }
    public string? TopWorkflowsJson { get; set; }
}
