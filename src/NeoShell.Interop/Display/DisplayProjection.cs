using NeoShell.Interop.Native;

namespace NeoShell.Interop.Display;

/// <summary>Win+P's choices; the values are the DISPLAYCONFIG_TOPOLOGY and SDC_TOPOLOGY flags.</summary>
public enum ProjectionMode { PcScreenOnly = 0x1, Duplicate = 0x2, Extend = 0x4, SecondScreenOnly = 0x8 }

/// <summary>How the PC's screens are used together, as Win+P sets it (<c>QueryDisplayConfig</c>, <c>SetDisplayConfig</c>).</summary>
public static unsafe class DisplayProjection
{
    /// <summary>The mode in use; null when Windows doesn't say (a topology it doesn't track, such as a remote session).</summary>
    public static ProjectionMode? Current()
    {
        if (User32.GetDisplayConfigBufferSizes(User32.QDC_DATABASE_CURRENT, out uint pathCount, out uint modeCount) != 0)
            return null;

        byte[] paths = new byte[Math.Max(1, pathCount) * User32.DisplayConfigPathInfoSize];
        byte[] modes = new byte[Math.Max(1, modeCount) * User32.DisplayConfigModeInfoSize];
        fixed (byte* pathBuffer = paths, modeBuffer = modes)
        {
            if (User32.QueryDisplayConfig(User32.QDC_DATABASE_CURRENT, ref pathCount, pathBuffer, ref modeCount, modeBuffer, out uint topology) != 0)
                return null;
            return Enum.IsDefined((ProjectionMode)topology) ? (ProjectionMode)topology : null;
        }
    }

    /// <summary>Switches to the mode, with the screens' saved layout for it. Throws on failure (e.g. no second screen).</summary>
    public static void Set(ProjectionMode mode)
    {
        int error = User32.SetDisplayConfig(0, null, 0, null, User32.SDC_APPLY | (uint)mode);
        if (error != 0)
            throw new System.ComponentModel.Win32Exception(error);
    }
}
