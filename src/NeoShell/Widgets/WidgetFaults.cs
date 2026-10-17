using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace NeoShell.Widgets;

/// <summary>
/// Which widget an exception came through. A widget's bug is contained (logged, and NeoShell carries on) rather than
/// ending the shell; see <see cref="UiThread"/>.
/// </summary>
public static class WidgetFaults
{
    /// <summary>The widget whose code the exception went through, if any.</summary>
    public static Type? Thrower(Exception exception) => FirstThrough(exception, typeof(WidgetView));

    /// <summary>
    /// The first type on the exception's stack that derives from <paramref name="baseType"/>. Async methods' frames
    /// count, through their state machines, and so do the frames from before an await rethrew it.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification =
        "Frames are of methods that ran, which IL trimming keeps with their metadata. A frame without a method is " +
        "skipped, and the exception then ends NeoShell as one from elsewhere does.")]
    public static Type? FirstThrough(Exception exception, Type baseType)
    {
        foreach (StackFrame frame in new StackTrace(exception).GetFrames())
        {
            // Lambdas and async state machines are types nested in the widget.
            for (Type? type = frame.GetMethod()?.DeclaringType; type is not null; type = type.DeclaringType)
            {
                if (type.IsSubclassOf(baseType))
                    return type;
            }
        }
        return null;
    }
}
