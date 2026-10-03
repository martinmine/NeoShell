namespace NeoShell.Interop.Windowing;

/// <summary>
/// Handles a window message. Returns the message result, or <c>null</c> to pass the message on to the default handling.
/// </summary>
public delegate nint? MessageHandler(uint message, nint wParam, nint lParam);
