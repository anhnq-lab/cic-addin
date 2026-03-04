using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CIC.BIM.Addin.Bridge;

/// <summary>
/// Handles Revit API calls on the main thread via ExternalEvent mechanism.
/// HTTP server threads queue requests here, and this handler executes them
/// when Revit's Idling event fires.
/// </summary>
public class RevitApiHandler : IExternalEventHandler
{
    private readonly object _lock = new();
    private Func<UIApplication, object?>? _pendingAction;
    private TaskCompletionSource<object?>? _tcs;

    public string GetName() => "CIC.BIM.Bridge.ApiHandler";

    /// <summary>
    /// Called by Revit on the main thread when ExternalEvent is raised.
    /// </summary>
    public void Execute(UIApplication app)
    {
        Func<UIApplication, object?>? action;
        TaskCompletionSource<object?>? tcs;

        lock (_lock)
        {
            action = _pendingAction;
            tcs = _tcs;
            _pendingAction = null;
            _tcs = null;
        }

        if (action == null || tcs == null) return;

        try
        {
            var result = action(app);
            tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    }

    /// <summary>
    /// Queue a Revit API action to be executed on the main thread.
    /// Returns a task that completes when the action has been executed.
    /// </summary>
    public Task<object?> ExecuteAsync(ExternalEvent externalEvent, Func<UIApplication, object?> action, int timeoutMs = 30000)
    {
        var tcs = new TaskCompletionSource<object?>();

        lock (_lock)
        {
            _pendingAction = action;
            _tcs = tcs;
        }

        var result = externalEvent.Raise();
        if (result != ExternalEventRequest.Accepted)
        {
            lock (_lock)
            {
                _pendingAction = null;
                _tcs = null;
            }
            tcs.TrySetException(new InvalidOperationException(
                $"ExternalEvent.Raise() returned {result}. Revit may be busy."));
        }

        // Timeout guard
        var cts = new CancellationTokenSource(timeoutMs);
        cts.Token.Register(() =>
        {
            tcs.TrySetException(new TimeoutException(
                $"Revit API call timed out after {timeoutMs}ms. Revit may be in a modal dialog."));
        });

        return tcs.Task;
    }
}
