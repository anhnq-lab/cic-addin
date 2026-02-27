using SysProcess = System.Diagnostics.Process;
using System.Runtime.InteropServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using CIC.BIM.Addin.Analytics.Models;

namespace CIC.BIM.Addin.Analytics.Services;

/// <summary>
/// Core tracking service. Hooks into Revit events and silently records user activity.
/// Tracking is invisible to users — only admin can view dashboard.
/// 
/// TIME CALCULATION:
/// Uses a "current activity slot" model. When a new event arrives:
/// 1. Calculate elapsed time since the PREVIOUS event
/// 2. Assign that elapsed time to the PREVIOUS activity's category
/// 3. Start a new activity slot for the current event
/// Cap single slot at MaxSlotSeconds to avoid counting breaks as work.
/// </summary>
public class ActivityTracker : IDisposable
{
    // ═══ P/Invoke for foreground detection ═══
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private readonly LocalStore _store;
    private readonly WorkflowAnalyzer _workflowAnalyzer;
    private UIControlledApplication? _uiApp;
    private UserProfile _userProfile;

    // Current session
    private WorkSession? _currentSession;
    private string? _activeViewName;
    private string? _activeViewType;
    private bool _revitInForeground = true;

    // ═══ TIME SLOT MODEL ═══
    // Instead of fixed 30s per event, we track the "current activity slot"
    // When a new event comes, the previous slot's duration = now - slotStart
    private ActivityCategory _currentSlotCategory = ActivityCategory.Idle;
    private string? _currentSlotSubCategory;
    private int _currentSlotElementCount;
    private string? _currentSlotTransactions;
    private DateTime _currentSlotStart = DateTime.UtcNow;

    // Completed time slots waiting to be flushed
    private readonly List<ActivityEvent> _completedSlots = new();
    private readonly object _bufferLock = new();

    // Timing
    private DateTime _lastActivityTime = DateTime.UtcNow;
    private DateTime _lastFlushTime = DateTime.UtcNow;
    private DateTime _lastSyncTime = DateTime.UtcNow;
    private DateTime _lastDocChangedTime = DateTime.MinValue;
    private bool _isIdle;

    // Session accumulators
    private int _sessionActiveSeconds;
    private int _sessionIdleSeconds;
    private DateTime _lastTickTime = DateTime.UtcNow;

    // Configuration
    private const int IdleThresholdSeconds = 120;     // 2 minutes → mark as idle
    private const int MaxSlotSeconds = 300;           // Cap single slot at 5 min
    private const int FlushIntervalSeconds = 15;      // Flush to SQLite every 15s
    private const int SyncIntervalMinutes = 5;        // Sync to Supabase every 5 min

    private SupabaseSyncService? _syncService;

    public ActivityTracker()
    {
        _store = new LocalStore();
        _workflowAnalyzer = new WorkflowAnalyzer();
        _userProfile = UserProfile.Load();
    }

    /// <summary>
    /// Start tracking — called from App.OnStartup()
    /// </summary>
    public void StartTracking(UIControlledApplication application)
    {
        _uiApp = application;
        _store.Initialize();

        // Initialize sync service
        _syncService = new SupabaseSyncService(_store);

        // Hook Revit events
        application.ControlledApplication.DocumentChanged += OnDocumentChanged;
        application.ControlledApplication.DocumentOpened += OnDocumentOpened;
        application.ControlledApplication.DocumentClosing += OnDocumentClosing;
        application.ControlledApplication.DocumentSaved += OnDocumentSaved;
        application.ControlledApplication.DocumentSavedAs += OnDocumentSavedAs;
        application.ControlledApplication.DocumentSynchronizedWithCentral += OnSynchronized;
        application.ViewActivated += OnViewActivated;
        application.Idling += OnIdling;
        application.DialogBoxShowing += OnDialogShowing;

        // Cleanup old data on startup
        Task.Run(() => _store.CleanupOldData());
    }

    /// <summary>
    /// Stop tracking — called from App.OnShutdown()
    /// </summary>
    public void StopTracking()
    {
        if (_uiApp != null)
        {
            _uiApp.ControlledApplication.DocumentChanged -= OnDocumentChanged;
            _uiApp.ControlledApplication.DocumentOpened -= OnDocumentOpened;
            _uiApp.ControlledApplication.DocumentClosing -= OnDocumentClosing;
            _uiApp.ControlledApplication.DocumentSaved -= OnDocumentSaved;
            _uiApp.ControlledApplication.DocumentSavedAs -= OnDocumentSavedAs;
            _uiApp.ControlledApplication.DocumentSynchronizedWithCentral -= OnSynchronized;
            _uiApp.ViewActivated -= OnViewActivated;
            _uiApp.Idling -= OnIdling;
            _uiApp.DialogBoxShowing -= OnDialogShowing;
        }

        // Close current slot
        CloseCurrentSlot();

        // End current session
        EndCurrentSession();

        // Final flush
        FlushBuffer();

        // Final sync attempt
        _syncService?.TrySyncAsync().Wait(TimeSpan.FromSeconds(5));
    }

    // ═══════════════════════════════════════════════════════
    //  CORE: TIME SLOT MANAGEMENT
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Transition to a new activity. Closes the previous time slot and
    /// starts a new one for the given category.
    /// 
    /// This is the CORE of accurate time tracking:
    /// - Previous slot duration = elapsed time since it started
    /// - Capped at MaxSlotSeconds to avoid counting breaks
    /// </summary>
    private void TransitionToActivity(
        ActivityCategory newCategory,
        string? subCategory = null,
        int elementCount = 0,
        string? transactionNames = null)
    {
        if (_currentSession == null) return;

        var now = DateTime.UtcNow;
        var elapsed = (int)(now - _currentSlotStart).TotalSeconds;

        // Cap at MaxSlotSeconds — if someone left Revit open during lunch,
        // we don't want to count that as 2 hours of "Modeling"
        elapsed = Math.Min(elapsed, MaxSlotSeconds);

        // Only record slots that are at least 1 second
        if (elapsed >= 1)
        {
            var completedEvent = new ActivityEvent
            {
                SessionId = _currentSession.Id,
                Timestamp = _currentSlotStart,
                Category = _currentSlotCategory,
                SubCategory = _currentSlotSubCategory,
                ElementCount = _currentSlotElementCount,
                DurationSeconds = elapsed,
                TransactionNames = _currentSlotTransactions,
                ActiveViewName = _activeViewName,
                ActiveViewType = _activeViewType
            };

            lock (_bufferLock)
            {
                _completedSlots.Add(completedEvent);
            }

            _workflowAnalyzer.RecordActivity(_currentSlotCategory);
        }

        // Start new slot
        _currentSlotCategory = newCategory;
        _currentSlotSubCategory = subCategory;
        _currentSlotElementCount = elementCount;
        _currentSlotTransactions = transactionNames;
        _currentSlotStart = now;
    }

    /// <summary>
    /// Update the current slot with additional data (same category, more elements)
    /// without transitioning. Used when DocumentChanged fires multiple times
    /// for the same type of activity.
    /// </summary>
    private void EnrichCurrentSlot(
        int additionalElements,
        string? transactionNames)
    {
        _currentSlotElementCount += additionalElements;
        if (!string.IsNullOrEmpty(transactionNames))
        {
            _currentSlotTransactions = string.IsNullOrEmpty(_currentSlotTransactions)
                ? transactionNames
                : _currentSlotTransactions + ", " + transactionNames;
        }
    }

    /// <summary>
    /// Close the current slot without starting a new one (used on shutdown/doc close)
    /// </summary>
    private void CloseCurrentSlot()
    {
        if (_currentSession == null) return;

        var elapsed = (int)(DateTime.UtcNow - _currentSlotStart).TotalSeconds;
        elapsed = Math.Min(elapsed, MaxSlotSeconds);

        if (elapsed >= 1)
        {
            var evt = new ActivityEvent
            {
                SessionId = _currentSession.Id,
                Timestamp = _currentSlotStart,
                Category = _currentSlotCategory,
                SubCategory = _currentSlotSubCategory,
                ElementCount = _currentSlotElementCount,
                DurationSeconds = elapsed,
                TransactionNames = _currentSlotTransactions,
                ActiveViewName = _activeViewName,
                ActiveViewType = _activeViewType
            };

            lock (_bufferLock)
            {
                _completedSlots.Add(evt);
            }
        }
    }

    // ═══════════════════════════════════════════════════════
    //  EVENT HANDLERS
    // ═══════════════════════════════════════════════════════

    private void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
    {
        try
        {
            var doc = e.Document;
            if (doc == null || doc.IsFamilyDocument) return;

            // End previous session if any
            CloseCurrentSlot();
            EndCurrentSession();

            // Start new session
            _currentSession = new WorkSession
            {
                ProjectName = doc.Title,
                FilePath = doc.PathName,
                RevitVersion = doc.Application.VersionNumber,
                DepartmentName = _userProfile.Department
            };
            _store.CreateSession(_currentSession);
            _sessionActiveSeconds = 0;
            _sessionIdleSeconds = 0;
            _lastTickTime = DateTime.UtcNow;
            _isIdle = false;

            // First slot: FileOps (opening file)
            _currentSlotCategory = ActivityCategory.FileOps;
            _currentSlotSubCategory = "Open";
            _currentSlotElementCount = 0;
            _currentSlotTransactions = null;
            _currentSlotStart = DateTime.UtcNow;
            _lastActivityTime = DateTime.UtcNow;
        }
        catch { /* Silent — tracking must never crash Revit */ }
    }

    private void OnDocumentClosing(object sender, DocumentClosingEventArgs e)
    {
        try
        {
            TransitionToActivity(ActivityCategory.FileOps, "Close", 0);
            CloseCurrentSlot();
            EndCurrentSession();
        }
        catch { }
    }

    private void OnDocumentSaved(object sender, DocumentSavedEventArgs e)
    {
        try
        {
            TransitionToActivity(ActivityCategory.FileOps, "Save", 0);
            _lastActivityTime = DateTime.UtcNow;
            _isIdle = false;
        }
        catch { }
    }

    private void OnDocumentSavedAs(object sender, DocumentSavedAsEventArgs e)
    {
        try
        {
            TransitionToActivity(ActivityCategory.FileOps, "SaveAs", 0);
            _lastActivityTime = DateTime.UtcNow;
            _isIdle = false;
        }
        catch { }
    }

    private void OnSynchronized(object sender, DocumentSynchronizedWithCentralEventArgs e)
    {
        try
        {
            TransitionToActivity(ActivityCategory.FileOps, "SyncCentral", 0);
            _lastActivityTime = DateTime.UtcNow;
            _isIdle = false;
        }
        catch { }
    }

    private void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
    {
        try
        {
            if (_currentSession == null) return;

            var now = DateTime.UtcNow;

            // Throttle burst events: if same-category event fires within 500ms,
            // just enrich the current slot without running full classification
            var msSinceLast = (now - _lastDocChangedTime).TotalMilliseconds;
            _lastDocChangedTime = now;

            if (msSinceLast < 500)
            {
                // Quick path: count elements and merge transaction names only
                var addedIds = e.GetAddedElementIds();
                var modifiedIds = e.GetModifiedElementIds();
                var deletedIds = e.GetDeletedElementIds();
                var totalElements = addedIds.Count + modifiedIds.Count + deletedIds.Count;
                var txnString = string.Join(", ", e.GetTransactionNames());
                EnrichCurrentSlot(totalElements, txnString);
                // Accumulate element counts on session
                AccumulateElementCounts(addedIds.Count, modifiedIds.Count, deletedIds.Count);
                _lastActivityTime = now;
                _isIdle = false;
                return;
            }

            var addedCount = e.GetAddedElementIds().Count;
            var modifiedCount = e.GetModifiedElementIds().Count;
            var deletedCount = e.GetDeletedElementIds().Count;

            var evt = EventClassifier.ClassifyDocumentChanged(
                e, _currentSession.Id, _activeViewName, _activeViewType);

            if (evt.Category == _currentSlotCategory)
            {
                EnrichCurrentSlot(evt.ElementCount, evt.TransactionNames);
            }
            else
            {
                TransitionToActivity(evt.Category, evt.SubCategory,
                    evt.ElementCount, evt.TransactionNames);
            }

            // Accumulate element counts on session
            AccumulateElementCounts(addedCount, modifiedCount, deletedCount);

            _lastActivityTime = now;
            _isIdle = false;
        }
        catch { }
    }

    private void OnViewActivated(object sender, ViewActivatedEventArgs e)
    {
        try
        {
            var view = e.CurrentActiveView;
            if (view == null) return;

            _activeViewName = view.Name;
            _activeViewType = EventClassifier.GetViewTypeString(view.ViewType);

            // Only transition if not already in Viewing mode
            if (_currentSlotCategory != ActivityCategory.Viewing)
            {
                TransitionToActivity(ActivityCategory.Viewing, _activeViewType, 0);
            }

            _lastActivityTime = DateTime.UtcNow;
            _isIdle = false;
        }
        catch { }
    }

    private void OnDialogShowing(object sender, DialogBoxShowingEventArgs e)
    {
        try
        {
            TransitionToActivity(ActivityCategory.DialogWait, null, 0);
            _lastActivityTime = DateTime.UtcNow;
        }
        catch { }
    }

    /// <summary>
    /// Idling event — fires ~every second when Revit is idle.
    /// Used for: idle detection, buffer flushing, periodic sync.
    /// </summary>
    private void OnIdling(object sender, IdlingEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;

            // Check if Revit is in foreground
            _revitInForeground = IsRevitForeground();

            // Accumulate session time
            var tickDuration = (int)(now - _lastTickTime).TotalSeconds;
            _lastTickTime = now;

            if (_currentSession != null && tickDuration > 0 && tickDuration < 60)
            {
                if (_isIdle || !_revitInForeground)
                    _sessionIdleSeconds += tickDuration;
                else
                    _sessionActiveSeconds += tickDuration;
            }

            // Check idle threshold (also consider background as idle)
            var idleSeconds = (now - _lastActivityTime).TotalSeconds;
            var effectivelyIdle = idleSeconds > IdleThresholdSeconds || (!_revitInForeground && idleSeconds > 30);
            if (!_isIdle && effectivelyIdle)
            {
                _isIdle = true;
                // Transition to idle — this closes the previous active slot
                // with accurate duration up to the idle threshold
                TransitionToActivity(ActivityCategory.Idle, null, 0);
            }

            // Flush completed slots + snapshot current slot to SQLite
            if ((now - _lastFlushTime).TotalSeconds > FlushIntervalSeconds)
            {
                _lastFlushTime = now;
                // Take a snapshot of the current active slot so dashboard stays up-to-date
                // even when the user stays in the same activity for a long time
                SnapshotCurrentSlot(now);
                Task.Run(FlushBuffer);
            }

            // Periodic sync to Supabase
            if ((now - _lastSyncTime).TotalMinutes > SyncIntervalMinutes)
            {
                _lastSyncTime = now;
                Task.Run(async () => await (_syncService?.TrySyncAsync() ?? Task.CompletedTask));
            }
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Take a snapshot of the current active slot and add it to flush queue.
    /// This cuts the current slot at this point, writes the elapsed time,
    /// and starts a new slot for the same activity — ensuring the dashboard
    /// stays updated even during long continuous operations.
    /// </summary>
    private void SnapshotCurrentSlot(DateTime now)
    {
        if (_currentSession == null) return;

        var elapsed = (int)(now - _currentSlotStart).TotalSeconds;
        if (elapsed < 5) return; // Don't snapshot until at least 5 seconds

        elapsed = Math.Min(elapsed, MaxSlotSeconds);

        var snapshot = new ActivityEvent
        {
            SessionId = _currentSession.Id,
            Timestamp = _currentSlotStart,
            Category = _currentSlotCategory,
            SubCategory = _currentSlotSubCategory,
            ElementCount = _currentSlotElementCount,
            DurationSeconds = elapsed,
            TransactionNames = _currentSlotTransactions,
            ActiveViewName = _activeViewName,
            ActiveViewType = _activeViewType
        };

        lock (_bufferLock)
        {
            _completedSlots.Add(snapshot);
        }

        // Reset the current slot start time (keep same category)
        // so the next snapshot doesn't double-count
        _currentSlotStart = now;
        _currentSlotElementCount = 0;
        _currentSlotTransactions = null;
    }

    private void FlushBuffer()
    {
        List<ActivityEvent> slotsToFlush;
        lock (_bufferLock)
        {
            if (_completedSlots.Count == 0) return;
            slotsToFlush = new List<ActivityEvent>(_completedSlots);
            _completedSlots.Clear();
        }

        try
        {
            _store.LogEvents(slotsToFlush);

            // Check for workflow patterns
            if (_currentSession != null)
            {
                var workflow = _workflowAnalyzer.TryExtractWorkflow();
                if (workflow.HasValue)
                {
                    _store.LogWorkflow(
                        _currentSession.Id,
                        workflow.Value.SequenceJson,
                        workflow.Value.StartedAt,
                        workflow.Value.DurationSeconds);
                }
            }
        }
        catch { /* Silent */ }
    }

    private void EndCurrentSession()
    {
        if (_currentSession == null) return;

        // Flush remaining workflow
        var remaining = _workflowAnalyzer.FlushRemaining();
        if (remaining.HasValue)
        {
            _store.LogWorkflow(
                _currentSession.Id,
                remaining.Value.SequenceJson,
                remaining.Value.StartedAt,
                remaining.Value.DurationSeconds);
        }

        _store.EndSession(_currentSession.Id, _sessionActiveSeconds, _sessionIdleSeconds,
            _currentSession.TotalElementsCreated, _currentSession.TotalElementsModified,
            _currentSession.TotalElementsDeleted);
        _currentSession = null;
    }

    /// <summary>
    /// Accumulate element counts on the current session for productivity tracking.
    /// </summary>
    private void AccumulateElementCounts(int added, int modified, int deleted)
    {
        if (_currentSession == null) return;
        _currentSession.TotalElementsCreated += added;
        _currentSession.TotalElementsModified += modified;
        _currentSession.TotalElementsDeleted += deleted;
    }

    /// <summary>
    /// Check if Revit is the foreground window.
    /// If Revit is minimized or user switched to another app → not foreground → count as idle.
    /// </summary>
    private static bool IsRevitForeground()
    {
        try
        {
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero) return false;

            GetWindowThreadProcessId(foregroundWindow, out var foregroundPid);
            return foregroundPid == (uint)SysProcess.GetCurrentProcess().Id;
        }
        catch { return true; } // Default to foreground on error
    }

    /// <summary>
    /// Get LocalStore for dashboard queries
    /// </summary>
    public LocalStore GetStore() => _store;

    public void Dispose()
    {
        StopTracking();
        _store.Dispose();
    }
}
