using NeoShell.Interop;
using NeoShell.Interop.Native;
using NeoShell.Interop.Windowing;

namespace NeoShell.Tests;

// These create real message-only windows: no UI, but they check the window procedure plumbing end to end.
public sealed class WindowingTests
{
    private const uint TestMessage = 0x8000 + 1; // WM_APP + 1

    private static string UniqueClassName() => "NeoShell.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public void MessageWindow_receives_messages_and_returns_the_handler_result()
    {
        var received = new List<(uint, nint, nint)>();
        using var window = new MessageWindow(UniqueClassName(), (message, wParam, lParam) =>
        {
            if (message != TestMessage)
                return null;
            received.Add((message, wParam, lParam));
            return 42;
        });

        nint result = User32.SendMessage(window.Handle, TestMessage, 7, 8);

        Assert.Equal(42, result);
        Assert.Equal([(TestMessage, (nint)7, (nint)8)], received);
    }

    [Fact]
    public void MessageWindow_can_be_found_by_class_until_disposed()
    {
        string className = UniqueClassName();
        var window = new MessageWindow(className, (_, _, _) => null);

        Assert.Equal(window.Handle, MessageWindow.Find(className));

        window.Dispose();

        Assert.Equal(0, MessageWindow.Find(className));
    }

    [Fact]
    public void MessageWindows_can_share_a_class()
    {
        string className = UniqueClassName();
        using var first = new MessageWindow(className, (m, _, _) => m == TestMessage ? 1 : null);
        using var second = new MessageWindow(className, (m, _, _) => m == TestMessage ? 2 : null);

        Assert.Equal(1, User32.SendMessage(first.Handle, TestMessage, 0, 0));
        Assert.Equal(2, User32.SendMessage(second.Handle, TestMessage, 0, 0));
    }

    [Fact]
    public void Handler_exceptions_are_reported_instead_of_escaping()
    {
        var reported = new List<Exception>();
        Action<Exception> onException = reported.Add;
        NativeCallback.UnhandledException += onException;
        try
        {
            using var window = new MessageWindow(UniqueClassName(), (message, _, _) =>
                message == TestMessage ? throw new InvalidOperationException("boom") : null);

            nint result = User32.SendMessage(window.Handle, TestMessage, 0, 0);

            Assert.Equal(0, result); // DefWindowProc's answer
            Assert.Contains(reported, ex => ex.Message == "boom");
        }
        finally
        {
            NativeCallback.UnhandledException -= onException;
        }
    }

    [Fact]
    public void WindowSubclass_sees_messages_first_and_can_pass_them_on()
    {
        using var window = new MessageWindow(UniqueClassName(), (m, _, _) => m == TestMessage ? 1 : null);
        using (var subclass = new WindowSubclass(window.Handle, (m, wParam, _) => m == TestMessage && wParam == 5 ? 2 : null))
        {
            Assert.Equal(2, User32.SendMessage(window.Handle, TestMessage, 5, 0));
            Assert.Equal(1, User32.SendMessage(window.Handle, TestMessage, 6, 0));
        }

        Assert.Equal(1, User32.SendMessage(window.Handle, TestMessage, 5, 0));
    }

    [Fact]
    public void WindowSubclass_can_be_disposed_after_its_window_is_destroyed()
    {
        var window = new MessageWindow(UniqueClassName(), (_, _, _) => null);
        var subclass = new WindowSubclass(window.Handle, (_, _, _) => null);

        window.Dispose();
        subclass.Dispose();
    }

    [Fact]
    public void Extended_styles_can_be_added_and_removed()
    {
        using var window = new MessageWindow(UniqueClassName(), (_, _, _) => null);

        WindowStyles.AddExtended(window.Handle, ExtendedWindowStyles.ToolWindow | ExtendedWindowStyles.NoActivate);
        Assert.Equal(ExtendedWindowStyles.ToolWindow | ExtendedWindowStyles.NoActivate,
            WindowStyles.GetExtended(window.Handle) & (ExtendedWindowStyles.ToolWindow | ExtendedWindowStyles.NoActivate));

        WindowStyles.RemoveExtended(window.Handle, ExtendedWindowStyles.NoActivate);
        Assert.Equal(ExtendedWindowStyles.ToolWindow,
            WindowStyles.GetExtended(window.Handle) & (ExtendedWindowStyles.ToolWindow | ExtendedWindowStyles.NoActivate));
    }
}
