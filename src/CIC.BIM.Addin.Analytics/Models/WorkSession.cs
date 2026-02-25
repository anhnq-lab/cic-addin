namespace CIC.BIM.Addin.Analytics.Models;

/// <summary>
/// Represents a work session (from document open to close)
/// </summary>
public class WorkSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserName { get; set; } = Environment.UserName;
    public string MachineName { get; set; } = Environment.MachineName;
    public string? RevitVersion { get; set; }
    public string? ProjectName { get; set; }
    public string? FilePath { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public int TotalActiveSeconds { get; set; }
    public int TotalIdleSeconds { get; set; }
    public bool SyncedToCloud { get; set; }
}
