using System.Diagnostics;
using System.Windows;

namespace FxTradeConfirmation.Helpers;

/// <summary>
/// Extension methods for fire-and-forget Task execution with structured error handling.
/// Ensures exceptions are never silently swallowed.
/// </summary>
internal static class TaskExtensions
{
    /// <summary>
    /// Runs a task as fire-and-forget. Any unhandled exception is logged via
    /// <paramref name="onError"/> (dispatched to the UI thread) and written to Debug output.
    /// </summary>
    /// <param name="task">The task to observe.</param>
    /// <param name="onError">Optional UI-thread callback receiving the exception message.</param>
    /// <param name="callerName">Injected automatically — used in the debug log prefix.</param>
    internal static void FireAndForget(
        this Task task,
        Action<string>? onError = null,
        [System.Runtime.CompilerServices.CallerMemberName] string callerName = "")
    {
        task.ContinueWith(t =>
        {
            if (t.Exception is null) return;

            var inner = t.Exception.Flatten().InnerExceptions[0];
            var message = $"[{callerName}] Unhandled exception: {inner.Message}";
            Debug.WriteLine(message);

            if (onError is null) return;

            Application.Current?.Dispatcher.Invoke(() => onError(message));
        },
        TaskContinuationOptions.OnlyOnFaulted);
    }
}
