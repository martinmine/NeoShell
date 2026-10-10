using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using NeoShell.Logging;

namespace NeoShell;

/// <summary>
/// Work queued to the UI thread. WinUI ends the process at once (fail-fast) when a queued callback throws, without
/// raising any unhandled-exception event, so the exception is logged here first; the shell watchdog then starts
/// Explorer (see <see cref="Program"/>).
/// </summary>
internal static class UiThread
{
    public static bool Post(this DispatcherQueue dispatcher, Action action) =>
        dispatcher.Post(DispatcherQueuePriority.Normal, action);

    public static bool Post(this DispatcherQueue dispatcher, DispatcherQueuePriority priority, Action action) =>
        dispatcher.TryEnqueue(priority, () => RunLogged(action));

    /// <summary>Runs an action once the next two frames have been drawn: then what's in the tree now shows.</summary>
    public static void AfterFramesDrawn(Action action)
    {
        int frames = 0;
        EventHandler<object>? rendered = null;
        rendered = (_, _) =>
        {
            if (++frames < 2)
                return;
            CompositionTarget.Rendered -= rendered;
            RunLogged(action);
        };
        CompositionTarget.Rendered += rendered;
    }

    private static void RunLogged(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error("Unhandled exception on the UI thread", ex);
            throw;
        }
    }

    /// <summary>The synchronization context for await and async void on the UI thread, logging the same way.</summary>
    public sealed class LoggingSynchronizationContext(DispatcherQueue dispatcher) : DispatcherQueueSynchronizationContext(dispatcher)
    {
        private readonly DispatcherQueue _dispatcher = dispatcher;

        public override void Post(SendOrPostCallback callback, object? state) => _dispatcher.Post(() => callback(state));

        public override SynchronizationContext CreateCopy() => new LoggingSynchronizationContext(_dispatcher);
    }
}
