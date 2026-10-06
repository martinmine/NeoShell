using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NeoShell.Interop.Com;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>An AutoPlay choice, from <c>…\Explorer\AutoplayHandlers\Handlers\&lt;Name&gt;</c>.</summary>
/// <param name="Action">What it does, "Open folder to view files".</param>
/// <param name="Provider">Who does it, "File Explorer"; none for the built-in choices such as "Take no action".</param>
/// <param name="Icon">An icon location ("file,index") or an image file.</param>
/// <param name="LastWrite">When its key was last written (UTC file time): newer ones are listed first.</param>
/// <param name="ProgId">With <paramref name="Verb"/>: the verb is run on the drive (<c>InvokeProgID</c>, <c>InvokeVerb</c>).</param>
/// <param name="Clsid">Or an <c>IHWEventHandler</c> started with <paramref name="InitCmdLine"/>.</param>
public sealed record AutoPlayHandler(
    string Name, string Action, string? Provider, string? Icon, long LastWrite,
    string? ProgId = null, string? Verb = null, Guid? Clsid = null, string? InitCmdLine = null);

/// <summary>
/// The AutoPlay choices registered for each event (<c>PlayMusicFilesOnArrival</c>, <c>StorageOnArrival</c>…), the
/// user's saved choices, and running a choice, as shell32's <c>CAutoplayHandler</c> does. Safe to call from a
/// background thread.
/// </summary>
public static unsafe class AutoPlayHandlers
{
    private const string AutoPlayKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers";

    /// <summary>Choices that only stand for a setting: they show no provider.</summary>
    private static readonly string[] s_settingChoices = ["MSTakeNoAction", "MSPromptEachTime", "MSAutoRun", "MSUseAdvancedStorageOptions"];

    /// <summary>The choices registered for an event, the user's (HKCU) before the machine's, unsorted and unique.</summary>
    public static IReadOnlyList<AutoPlayHandler> ForEvent(string eventName)
    {
        List<AutoPlayHandler> handlers = [];
        foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using RegistryKey? key = hive.OpenSubKey($@"{AutoPlayKey}\EventHandlers\{eventName}");
            foreach (string name in key?.GetValueNames() ?? [])
            {
                if (name.Length > 0 && !handlers.Any(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) && Read(name) is { } handler)
                    handlers.Add(handler);
            }
        }
        return handlers;
    }

    /// <summary>
    /// A registered choice, the user's own registration first. A user's choice needs something to run (a packaged
    /// app's verb), as Explorer requires; null if there's none.
    /// </summary>
    public static AutoPlayHandler? Read(string name)
    {
        using RegistryKey? user = Registry.CurrentUser.OpenSubKey($@"{AutoPlayKey}\Handlers\{name}");
        if (user is not null)
            return Read(name, user) is { ProgId: not null, Verb: not null } handler ? handler : null;

        using RegistryKey? machine = Registry.LocalMachine.OpenSubKey($@"{AutoPlayKey}\Handlers\{name}");
        return machine is null ? null : Read(name, machine);
    }

    private static AutoPlayHandler? Read(string name, RegistryKey key)
    {
        string? action = Text(key.GetValue("Action") as string);
        if (action is null or "-")
            return null;

        string? provider = s_settingChoices.Contains(name) ? null : Text(key.GetValue("Provider") as string);
        string? icon = key.GetValue("DefaultIcon") as string is { } iconValue and not "-" ? Text(iconValue) : null;
        Guid? clsid = Guid.TryParse(key.GetValue("CLSID") as string, out Guid parsed) ? parsed : null;
        return new AutoPlayHandler(
            name, action, provider, icon is null ? null : Environment.ExpandEnvironmentVariables(icon), LastWriteTime(key),
            (key.GetValue("InvokeProgID") ?? key.GetValue("InvokeProgid")) as string, key.GetValue("InvokeVerb") as string,
            clsid, key.GetValue("InitCmdLine") as string);
    }

    private static long LastWriteTime(RegistryKey key)
    {
        long time = 0;
        Advapi32.RegQueryInfoKey(key.Handle.DangerousGetHandle(), null, null, 0, null, null, null, null, null, null, null, &time);
        return time;
    }

    /// <summary>
    /// The user's choice for an event: the one that runs by itself (<paramref name="remembered"/>,
    /// <c>UserChosenExecuteHandlers</c>), or the one last picked, selected when AutoPlay asks
    /// (<c>EventHandlersDefaultSelection</c>). <paramref name="group"/> is a subkey some events keep theirs under
    /// (memory cards: <c>CameraAlternate</c>).
    /// </summary>
    public static string? GetChoice(bool remembered, string eventName, string? group)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ChoiceKey(remembered, eventName, group));
        return key?.GetValue(null) as string is { Length: > 0 } choice ? choice : null;
    }

    public static void SetChoice(bool remembered, string eventName, string? group, string handler)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(ChoiceKey(remembered, eventName, group));
        key.SetValue(null, handler);
    }

    private static string ChoiceKey(bool remembered, string eventName, string? group) =>
        string.Join('\\', new[] { AutoPlayKey, remembered ? "UserChosenExecuteHandlers" : "EventHandlersDefaultSelection", group, eventName }.OfType<string>());

    /// <summary>
    /// Runs a choice on the drive at <paramref name="root"/>, on a thread of its own as Explorer does: its verb on the
    /// drive through the ProgID's shell key, or its <c>IHWEventHandler</c>. Choices with neither do nothing.
    /// </summary>
    /// <param name="failed">Called on that thread if it can't be run.</param>
    public static void Invoke(AutoPlayHandler handler, string root, Action<Exception> failed) =>
        RunOnStaThread(() =>
        {
            if (handler is { ProgId: { } progId, Verb: { } verb })
                Execute(root, verb, progId, null);
            else if (handler.Clsid is { } clsid)
                HandleEvent(clsid, handler.InitCmdLine ?? "", root);
        }, failed);

    /// <summary>Runs an <c>autorun.inf</c>'s program, in the drive's root as Explorer does.</summary>
    public static void RunAutorun(AutorunInf autorun, string root, Action<Exception> failed) =>
        RunOnStaThread(() =>
        {
            (string program, string arguments) = AutoPlayVolumes.SplitCommand(autorun.Command!);
            Execute(program, null, null, arguments, root);
        }, failed);

    private static void RunOnStaThread(Action run, Action<Exception> failed)
    {
        var thread = new Thread(() =>
        {
            try
            {
                run();
            }
            catch (Exception ex) when (ex is Win32Exception or COMException or InvalidCastException)
            {
                failed(ex);
            }
        })
        { IsBackground = true, Name = "AutoPlay handler" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void Execute(string file, string? verb, string? progId, string? arguments, string? directory = null)
    {
        fixed (char* filePointer = file)
        fixed (char* verbPointer = verb)
        fixed (char* classPointer = progId)
        fixed (char* argumentsPointer = arguments)
        fixed (char* directoryPointer = directory)
        {
            var info = new Shell32.SHELLEXECUTEINFOW
            {
                cbSize = (uint)sizeof(Shell32.SHELLEXECUTEINFOW),
                fMask = Shell32.SEE_MASK_NOASYNC | (progId is null ? 0 : Shell32.SEE_MASK_CLASSNAME),
                lpVerb = verbPointer,
                lpFile = filePointer,
                lpClass = classPointer,
                lpParameters = string.IsNullOrEmpty(arguments) ? null : argumentsPointer,
                lpDirectory = directoryPointer,
                nShow = User32.SW_SHOWNORMAL,
            };
            if (!Shell32.ShellExecuteEx(&info))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    // As shell32's CAutoplayHandler::Invoke: out of process, with the drive as the device and the generic arrival event.
    private static void HandleEvent(Guid clsid, string parameters, string root)
    {
        var handler = Ole32.Create<IHWEventHandler>(clsid, Ole32.CLSCTX_LOCAL_SERVER);
        Marshal.ThrowExceptionForHR(handler.Initialize(parameters));
        Marshal.ThrowExceptionForHR(handler is IHWEventHandler2 handler2
            ? handler2.HandleEventWithHWND(root, "", "DeviceArrival", 0)
            : handler.HandleEvent(root, "", "DeviceArrival"));
    }

    /// <summary>"Published by Contoso" for a signed program, else "Publisher not specified", as Explorer's autorun choice.</summary>
    public static string PublisherText(string program)
    {
        const int length = 64;
        char* publisher = stackalloc char[length];
        var info = new Wintrust.SIGNATURE_INFO
        {
            cbSize = (uint)sizeof(Wintrust.SIGNATURE_INFO),
            pszPublisherName = publisher,
            cchPublisherName = length,
        };
        bool signed = Wintrust.WTGetSignatureInfo(program, -1, Wintrust.SIF_ANY_SIGNATURE, &info, 0, 0) >= 0
            && (info.dwSignatureInfoAvailability & Wintrust.SIA_PUBLISHERNAME) != 0;
        return signed
            ? Format(Text("@%SystemRoot%\\system32\\shell32.dll,-17428") ?? "Published by %1", new string(publisher))
            : Text("@%SystemRoot%\\system32\\shell32.dll,-17429") ?? "Publisher not specified";
    }

    /// <summary>Resolves a resource reference (<c>@shell32.dll,-17408</c>, <c>@{package?ms-resource://…}</c>); plain text is kept.</summary>
    public static string? Text(string? text)
    {
        if (text is null || !text.StartsWith('@'))
            return text;

        char* buffer = stackalloc char[1024];
        return Shlwapi.SHLoadIndirectString(Environment.ExpandEnvironmentVariables(text), buffer, 1024, 0) == 0 ? new string(buffer) : null;
    }

    /// <summary>Fills a message resource's <c>%1</c> (written <c>%1!ls!</c>).</summary>
    public static string Format(string message, string argument) =>
        message.Replace("%1!ls!", argument).Replace("%1!s!", argument).Replace("%1", argument);

    /// <summary>An icon from a location such as <c>"C:\…\vlc.exe",0</c> or <c>shell32.dll,-302</c>; null if it isn't one.</summary>
    public static IconBitmap? ExtractIcon(string location, int size)
    {
        int comma = location.LastIndexOf(',');
        int index = 0;
        if (comma > 0 && int.TryParse(location[(comma + 1)..], out index))
            location = location[..comma];
        string file = Environment.ExpandEnvironmentVariables(location.Trim().Trim('"'));
        nint icon;
        fixed (char* filePointer = file)
        {
            if (Shell32.SHDefExtractIcon(filePointer, index, 0, &icon, null, (uint)size) != 0 || icon == 0)
                return null;
        }
        try
        {
            return IconBitmap.FromIcon(icon);
        }
        finally
        {
            User32.DestroyIcon(icon);
        }
    }
}
