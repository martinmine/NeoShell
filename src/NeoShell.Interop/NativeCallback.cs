namespace NeoShell.Interop;

public static class NativeCallback
{
    /// <summary>
    /// Raised for an exception thrown by .NET code that Windows called back into (window procedures, hooks).
    /// </summary>
    /// <remarks>
    /// An exception escaping an <c>[UnmanagedCallersOnly]</c> method ends the process without running any
    /// unhandled-exception handler, which would leave the user without a shell. Callbacks catch everything
    /// and report it here instead.
    /// </remarks>
    public static event Action<Exception>? UnhandledException;

    internal static void Report(Exception exception) => UnhandledException?.Invoke(exception);
}
