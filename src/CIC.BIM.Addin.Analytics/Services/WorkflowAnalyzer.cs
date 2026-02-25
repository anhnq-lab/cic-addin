using System.Text.Json;
using CIC.BIM.Addin.Analytics.Models;

namespace CIC.BIM.Addin.Analytics.Services;

/// <summary>
/// Analyzes sequences of activities to detect workflow patterns.
/// Detects common patterns like "Design iteration", "Documentation sprint", "Long idle".
/// </summary>
public class WorkflowAnalyzer
{
    private readonly List<ActivityCategory> _recentCategories = new();
    private DateTime _sequenceStart = DateTime.UtcNow;
    private const int SequenceWindowSize = 10; // Analyze in windows of 10 events

    /// <summary>
    /// Add an activity to the rolling window for pattern analysis
    /// </summary>
    public void RecordActivity(ActivityCategory category)
    {
        if (_recentCategories.Count == 0)
            _sequenceStart = DateTime.UtcNow;

        _recentCategories.Add(category);
    }

    /// <summary>
    /// Check if enough events have been collected to analyze a workflow.
    /// Returns workflow data if a complete window is ready, null otherwise.
    /// </summary>
    public (string SequenceJson, DateTime StartedAt, int DurationSeconds)? TryExtractWorkflow()
    {
        if (_recentCategories.Count < SequenceWindowSize)
            return null;

        // Take the window
        var window = _recentCategories.Take(SequenceWindowSize).ToList();
        var json = JsonSerializer.Serialize(window.Select(c => c.ToString()).ToArray());
        var duration = (int)(DateTime.UtcNow - _sequenceStart).TotalSeconds;

        // Remove processed items, keep overlap for continuity
        _recentCategories.RemoveRange(0, SequenceWindowSize / 2);
        _sequenceStart = DateTime.UtcNow;

        return (json, _sequenceStart, duration);
    }

    /// <summary>
    /// Flush remaining activities as a partial workflow
    /// </summary>
    public (string SequenceJson, DateTime StartedAt, int DurationSeconds)? FlushRemaining()
    {
        if (_recentCategories.Count < 3)
        {
            _recentCategories.Clear();
            return null;
        }

        var json = JsonSerializer.Serialize(_recentCategories.Select(c => c.ToString()).ToArray());
        var duration = (int)(DateTime.UtcNow - _sequenceStart).TotalSeconds;
        var start = _sequenceStart;
        _recentCategories.Clear();

        return (json, start, duration);
    }

    /// <summary>
    /// Detect waste indicators from a sequence of categories
    /// </summary>
    public static List<string> DetectWaste(IList<ActivityCategory> sequence)
    {
        var warnings = new List<string>();

        // Pattern: Too much idle
        var idleRatio = sequence.Count(c => c == ActivityCategory.Idle) / (double)sequence.Count;
        if (idleRatio > 0.4)
            warnings.Add($"Tỷ lệ idle cao: {idleRatio:P0}");

        // Pattern: Excessive view switching without model changes
        int viewSwitchCount = 0;
        for (int i = 1; i < sequence.Count; i++)
        {
            if (sequence[i] == ActivityCategory.Viewing && sequence[i - 1] == ActivityCategory.Viewing)
                viewSwitchCount++;
        }
        if (viewSwitchCount > sequence.Count * 0.3)
            warnings.Add($"Chuyển view liên tục {viewSwitchCount} lần — có thể đang tìm kiếm thông tin");

        // Pattern: Modeling → Editing loop (potential rework)
        int reworkCount = 0;
        for (int i = 2; i < sequence.Count; i++)
        {
            if (sequence[i] == ActivityCategory.Modeling &&
                sequence[i - 1] == ActivityCategory.Editing &&
                sequence[i - 2] == ActivityCategory.Modeling)
                reworkCount++;
        }
        if (reworkCount > 2)
            warnings.Add($"Lặp mô hình → chỉnh sửa {reworkCount} lần — có thể cần xem xét lại quy trình");

        return warnings;
    }

    /// <summary>
    /// Detect the workflow pattern name from a category sequence
    /// </summary>
    public static string DetectPatternName(IList<ActivityCategory> sequence)
    {
        var dominant = sequence
            .Where(c => c != ActivityCategory.Idle && c != ActivityCategory.DialogWait)
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;

        var hasModeling = sequence.Any(c => c == ActivityCategory.Modeling);
        var hasEditing = sequence.Any(c => c == ActivityCategory.Editing);
        var hasDocumenting = sequence.Any(c => c == ActivityCategory.Documenting);
        var hasViewing = sequence.Any(c => c == ActivityCategory.Viewing);

        if (hasModeling && hasEditing && hasViewing)
            return "Design Iteration";
        if (hasDocumenting && !hasModeling)
            return "Documentation Sprint";
        if (hasModeling && !hasEditing && !hasDocumenting)
            return "Focused Modeling";
        if (hasEditing && !hasModeling)
            return "Editing Session";
        if (dominant == ActivityCategory.Idle)
            return "Long Idle";
        if (dominant == ActivityCategory.FileOps)
            return "File Management";

        return "Mixed Activity";
    }
}
