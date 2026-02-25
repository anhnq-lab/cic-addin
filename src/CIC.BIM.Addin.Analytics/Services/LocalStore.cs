using System.IO;
using Microsoft.Data.Sqlite;
using CIC.BIM.Addin.Analytics.Models;

namespace CIC.BIM.Addin.Analytics.Services;

/// <summary>
/// SQLite local storage for analytics data.
/// All writes are async to avoid blocking Revit UI thread.
/// DB path: %APPDATA%/CIC/BIM/analytics.db
/// </summary>
public class LocalStore : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;
    private readonly object _lock = new();

    public LocalStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "CIC", "BIM");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "analytics.db");
    }

    /// <summary>
    /// Initialize database and create tables if needed
    /// </summary>
    public void Initialize()
    {
        // SQLitePCLRaw needs explicit init in Revit context because
        // Revit's runtime doesn't probe standard NuGet native paths
        InitializeSQLiteNative();

        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                user_name TEXT NOT NULL,
                machine_name TEXT,
                revit_version TEXT,
                project_name TEXT,
                file_path TEXT,
                started_at TEXT NOT NULL,
                ended_at TEXT,
                total_active_seconds INTEGER DEFAULT 0,
                total_idle_seconds INTEGER DEFAULT 0,
                synced_to_cloud INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS activity_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                category TEXT NOT NULL,
                sub_category TEXT,
                element_count INTEGER DEFAULT 0,
                duration_seconds INTEGER DEFAULT 30,
                transaction_names TEXT,
                active_view_name TEXT,
                active_view_type TEXT,
                synced_to_cloud INTEGER DEFAULT 0,
                FOREIGN KEY (session_id) REFERENCES sessions(id)
            );

            CREATE TABLE IF NOT EXISTS workflow_sequences (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                sequence TEXT NOT NULL,
                started_at TEXT NOT NULL,
                duration_seconds INTEGER,
                synced_to_cloud INTEGER DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_events_session ON activity_events(session_id);
            CREATE INDEX IF NOT EXISTS idx_events_timestamp ON activity_events(timestamp);
            CREATE INDEX IF NOT EXISTS idx_events_sync ON activity_events(synced_to_cloud);
            CREATE INDEX IF NOT EXISTS idx_sessions_sync ON sessions(synced_to_cloud);
        ";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Create a new work session record
    /// </summary>
    public void CreateSession(WorkSession session)
    {
        lock (_lock)
        {
            if (_connection == null) return;
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO sessions (id, user_name, machine_name, revit_version, project_name, file_path, started_at)
                VALUES ($id, $user, $machine, $revit, $project, $path, $started)";
            cmd.Parameters.AddWithValue("$id", session.Id);
            cmd.Parameters.AddWithValue("$user", session.UserName);
            cmd.Parameters.AddWithValue("$machine", session.MachineName);
            cmd.Parameters.AddWithValue("$revit", session.RevitVersion ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$project", session.ProjectName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$path", session.FilePath ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$started", session.StartedAt.ToString("o"));
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// End a work session
    /// </summary>
    public void EndSession(string sessionId, int activeSeconds, int idleSeconds)
    {
        lock (_lock)
        {
            if (_connection == null) return;
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE sessions 
                SET ended_at = $ended, total_active_seconds = $active, total_idle_seconds = $idle
                WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", sessionId);
            cmd.Parameters.AddWithValue("$ended", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$active", activeSeconds);
            cmd.Parameters.AddWithValue("$idle", idleSeconds);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Log a batch of activity events
    /// </summary>
    public void LogEvents(IEnumerable<ActivityEvent> events)
    {
        lock (_lock)
        {
            if (_connection == null) return;
            using var transaction = _connection.BeginTransaction();
            try
            {
                foreach (var evt in events)
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO activity_events 
                        (session_id, timestamp, category, sub_category, element_count, 
                         duration_seconds, transaction_names, active_view_name, active_view_type)
                        VALUES ($sid, $ts, $cat, $sub, $count, $dur, $txn, $view, $vtype)";
                    cmd.Parameters.AddWithValue("$sid", evt.SessionId);
                    cmd.Parameters.AddWithValue("$ts", evt.Timestamp.ToString("o"));
                    cmd.Parameters.AddWithValue("$cat", evt.Category.ToString());
                    cmd.Parameters.AddWithValue("$sub", evt.SubCategory ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$count", evt.ElementCount);
                    cmd.Parameters.AddWithValue("$dur", evt.DurationSeconds);
                    cmd.Parameters.AddWithValue("$txn", evt.TransactionNames ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$view", evt.ActiveViewName ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$vtype", evt.ActiveViewType ?? (object)DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    /// <summary>
    /// Log a workflow sequence
    /// </summary>
    public void LogWorkflow(string sessionId, string sequenceJson, DateTime startedAt, int durationSeconds)
    {
        lock (_lock)
        {
            if (_connection == null) return;
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO workflow_sequences (session_id, sequence, started_at, duration_seconds)
                VALUES ($sid, $seq, $start, $dur)";
            cmd.Parameters.AddWithValue("$sid", sessionId);
            cmd.Parameters.AddWithValue("$seq", sequenceJson);
            cmd.Parameters.AddWithValue("$start", startedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$dur", durationSeconds);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Get unsynced sessions for Supabase push
    /// </summary>
    public List<WorkSession> GetUnsyncedSessions()
    {
        lock (_lock)
        {
            var sessions = new List<WorkSession>();
            if (_connection == null) return sessions;

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM sessions WHERE synced_to_cloud = 0 AND ended_at IS NOT NULL";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                sessions.Add(new WorkSession
                {
                    Id = reader.GetString(0),
                    UserName = reader.GetString(1),
                    MachineName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    RevitVersion = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ProjectName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    FilePath = reader.IsDBNull(5) ? null : reader.GetString(5),
                    StartedAt = DateTime.Parse(reader.GetString(6)),
                    EndedAt = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7)),
                    TotalActiveSeconds = reader.GetInt32(8),
                    TotalIdleSeconds = reader.GetInt32(9),
                    SyncedToCloud = false
                });
            }
            return sessions;
        }
    }

    /// <summary>
    /// Get unsynced events for Supabase push
    /// </summary>
    public List<ActivityEvent> GetUnsyncedEvents(int limit = 500)
    {
        lock (_lock)
        {
            var events = new List<ActivityEvent>();
            if (_connection == null) return events;

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT * FROM activity_events WHERE synced_to_cloud = 0 ORDER BY id LIMIT {limit}";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                events.Add(new ActivityEvent
                {
                    Id = reader.GetInt64(0),
                    SessionId = reader.GetString(1),
                    Timestamp = DateTime.Parse(reader.GetString(2)),
                    Category = Enum.Parse<ActivityCategory>(reader.GetString(3)),
                    SubCategory = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ElementCount = reader.GetInt32(5),
                    DurationSeconds = reader.GetInt32(6),
                    TransactionNames = reader.IsDBNull(7) ? null : reader.GetString(7),
                    ActiveViewName = reader.IsDBNull(8) ? null : reader.GetString(8),
                    ActiveViewType = reader.IsDBNull(9) ? null : reader.GetString(9),
                    SyncedToCloud = false
                });
            }
            return events;
        }
    }

    /// <summary>
    /// Mark records as synced
    /// </summary>
    public void MarkSessionsSynced(IEnumerable<string> sessionIds)
    {
        lock (_lock)
        {
            if (_connection == null) return;
            foreach (var id in sessionIds)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "UPDATE sessions SET synced_to_cloud = 1 WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }
        }
    }

    public void MarkEventsSynced(IEnumerable<long> eventIds)
    {
        lock (_lock)
        {
            if (_connection == null) return;
            using var transaction = _connection.BeginTransaction();
            foreach (var id in eventIds)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "UPDATE activity_events SET synced_to_cloud = 1 WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    /// <summary>
    /// Get daily summary for dashboard display
    /// </summary>
    public DailySummary? GetDailySummary(DateTime date, string? projectName = null)
    {
        lock (_lock)
        {
            if (_connection == null) return null;

            var dateStr = date.ToString("yyyy-MM-dd");
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    category,
                    SUM(duration_seconds) / 60.0 as minutes
                FROM activity_events e
                JOIN sessions s ON e.session_id = s.id
                WHERE DATE(e.timestamp) = $date
                AND ($project IS NULL OR s.project_name = $project)
                GROUP BY category";
            cmd.Parameters.AddWithValue("$date", dateStr);
            cmd.Parameters.AddWithValue("$project", projectName ?? (object)DBNull.Value);

            var summary = new DailySummary
            {
                UserName = Environment.UserName,
                ProjectName = projectName,
                SummaryDate = date.Date
            };

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var category = reader.GetString(0);
                var minutes = reader.GetDouble(1);
                switch (category)
                {
                    case nameof(ActivityCategory.Modeling): summary.ModelingMinutes = minutes; break;
                    case nameof(ActivityCategory.Editing): summary.EditingMinutes = minutes; break;
                    case nameof(ActivityCategory.Viewing): summary.ViewingMinutes = minutes; break;
                    case nameof(ActivityCategory.Documenting): summary.DocumentingMinutes = minutes; break;
                    case nameof(ActivityCategory.FileOps): summary.FileOpsMinutes = minutes; break;
                    case nameof(ActivityCategory.Idle): summary.IdleMinutes = minutes; break;
                }
            }

            summary.TotalActiveMinutes = summary.ModelingMinutes + summary.EditingMinutes
                + summary.ViewingMinutes + summary.DocumentingMinutes + summary.FileOpsMinutes;

            // Count sessions
            using var cmd2 = _connection.CreateCommand();
            cmd2.CommandText = @"
                SELECT COUNT(*) FROM sessions 
                WHERE DATE(started_at) = $date
                AND ($project IS NULL OR project_name = $project)";
            cmd2.Parameters.AddWithValue("$date", dateStr);
            cmd2.Parameters.AddWithValue("$project", projectName ?? (object)DBNull.Value);
            summary.TotalSessions = Convert.ToInt32(cmd2.ExecuteScalar());

            return summary;
        }
    }

    /// <summary>
    /// Get weekly summaries for chart display
    /// </summary>
    public List<DailySummary> GetWeeklySummaries(DateTime weekStart)
    {
        var summaries = new List<DailySummary>();
        for (int i = 0; i < 7; i++)
        {
            var date = weekStart.AddDays(i);
            var summary = GetDailySummary(date);
            if (summary != null)
                summaries.Add(summary);
        }
        return summaries;
    }

    /// <summary>
    /// Cleanup old data (older than 90 days)
    /// </summary>
    public void CleanupOldData(int retentionDays = 90)
    {
        lock (_lock)
        {
            if (_connection == null) return;
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays).ToString("o");

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM activity_events WHERE timestamp < $cutoff AND synced_to_cloud = 1;
                DELETE FROM workflow_sequences WHERE started_at < $cutoff AND synced_to_cloud = 1;
                DELETE FROM sessions WHERE ended_at < $cutoff AND synced_to_cloud = 1;";
            cmd.Parameters.AddWithValue("$cutoff", cutoff);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Initialize SQLitePCLRaw native library for Revit add-in context.
    /// Revit doesn't probe standard .NET runtime native paths, so we need
    /// to manually set the native library search path.
    /// </summary>
    private static bool _sqliteInitialized = false;
    private static void InitializeSQLiteNative()
    {
        if (_sqliteInitialized) return;

        try
        {
            // Try to find the native e_sqlite3.dll relative to our assembly
            var assemblyDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

            // Look for native DLL in runtimes/win-x64/native/ (standard NuGet layout)
            var nativePath = Path.Combine(assemblyDir, "runtimes",
                Environment.Is64BitProcess ? "win-x64" : "win-x86",
                "native", "e_sqlite3.dll");

            if (File.Exists(nativePath))
            {
                // Copy native DLL next to our assembly so SQLitePCLRaw can find it
                var targetPath = Path.Combine(assemblyDir, "e_sqlite3.dll");
                if (!File.Exists(targetPath))
                {
                    File.Copy(nativePath, targetPath);
                }
            }

            // Initialize SQLitePCLRaw batteries
            SQLitePCL.Batteries.Init();
            _sqliteInitialized = true;
        }
        catch (Exception)
        {
            // Last resort: try init without setting path (might work if DLL is already accessible)
            try
            {
                SQLitePCL.Batteries.Init();
                _sqliteInitialized = true;
            }
            catch { throw; }
        }
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
    }
}
