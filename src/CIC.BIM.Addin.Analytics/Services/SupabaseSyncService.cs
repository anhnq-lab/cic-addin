using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CIC.BIM.Addin.Analytics.Models;

namespace CIC.BIM.Addin.Analytics.Services;

/// <summary>
/// Syncs local SQLite data to Supabase (CIC-ERP project).
/// Runs on background thread, tolerates offline scenarios.
/// </summary>
public class SupabaseSyncService
{
    private readonly LocalStore _store;
    private readonly HttpClient _httpClient;
    private bool _isEnabled;

    // Supabase config — loaded from settings file
    private string _supabaseUrl = "";
    private string _supabaseKey = "";

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CIC", "BIM", "analytics_settings.json");

    public SupabaseSyncService(LocalStore store)
    {
        _store = store;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        LoadSettings();
    }

    /// <summary>
    /// Load Supabase connection settings from local config file
    /// </summary>
    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (settings != null)
                {
                    _supabaseUrl = settings.GetValueOrDefault("supabase_url", "");
                    _supabaseKey = settings.GetValueOrDefault("supabase_key", "");
                    _isEnabled = !string.IsNullOrEmpty(_supabaseUrl) && !string.IsNullOrEmpty(_supabaseKey);
                }
            }
        }
        catch { _isEnabled = false; }
    }

    /// <summary>
    /// Save Supabase connection settings
    /// </summary>
    public static void SaveSettings(string supabaseUrl, string supabaseKey)
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        var settings = new Dictionary<string, string>
        {
            ["supabase_url"] = supabaseUrl,
            ["supabase_key"] = supabaseKey
        };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    /// <summary>
    /// Check if admin list file exists to determine admin status
    /// </summary>
    public static bool IsCurrentUserAdmin()
    {
        try
        {
            var adminListPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CIC", "BIM", "admin_users.txt");

            if (!File.Exists(adminListPath)) return false;

            var adminUsers = File.ReadAllLines(adminListPath)
                .Select(l => l.Trim().ToLowerInvariant())
                .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#"))
                .ToHashSet();

            return adminUsers.Contains(Environment.UserName.ToLowerInvariant());
        }
        catch { return false; }
    }

    /// <summary>
    /// Attempt to sync unsynced data to Supabase.
    /// Silently fails if offline or not configured.
    /// </summary>
    public async Task TrySyncAsync()
    {
        if (!_isEnabled) return;

        try
        {
            // Sync sessions
            var sessions = _store.GetUnsyncedSessions();
            if (sessions.Count > 0)
            {
                var success = await PostToSupabase("analytics_sessions", sessions.Select(s => new
                {
                    id = s.Id,
                    user_name = s.UserName,
                    machine_name = s.MachineName,
                    revit_version = s.RevitVersion,
                    project_name = s.ProjectName,
                    file_path = s.FilePath,
                    started_at = s.StartedAt.ToString("o"),
                    ended_at = s.EndedAt?.ToString("o"),
                    total_active_seconds = s.TotalActiveSeconds,
                    total_idle_seconds = s.TotalIdleSeconds,
                    total_elements_created = s.TotalElementsCreated,
                    total_elements_modified = s.TotalElementsModified,
                    total_elements_deleted = s.TotalElementsDeleted,
                    department_name = s.DepartmentName
                }));

                if (success)
                    _store.MarkSessionsSynced(sessions.Select(s => s.Id));
            }

            // Sync events (in batches of 500)
            var events = _store.GetUnsyncedEvents(500);
            if (events.Count > 0)
            {
                var success = await PostToSupabase("analytics_events", events.Select(e => new
                {
                    session_id = e.SessionId,
                    timestamp = e.Timestamp.ToString("o"),
                    category = e.Category.ToString(),
                    sub_category = e.SubCategory,
                    element_count = e.ElementCount,
                    duration_seconds = e.DurationSeconds,
                    transaction_names = e.TransactionNames,
                    active_view_name = e.ActiveViewName,
                    active_view_type = e.ActiveViewType
                }));

                if (success)
                    _store.MarkEventsSynced(events.Select(e => e.Id));
            }
        }
        catch
        {
            // Silent — sync failure should never affect user
        }
    }

    /// <summary>
    /// POST data to Supabase REST API
    /// </summary>
    private async Task<bool> PostToSupabase<T>(string table, IEnumerable<T> data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data.ToArray());
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_supabaseUrl}/rest/v1/{table}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("apikey", _supabaseKey);
            request.Headers.Add("Authorization", $"Bearer {_supabaseKey}");
            request.Headers.Add("Prefer", "resolution=merge-duplicates");

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
