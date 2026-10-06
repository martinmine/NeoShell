using Microsoft.Win32.SafeHandles;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>
/// What an optical drive is called ("DVD Drive", "BD-RE Drive"). Explorer's process learns the drive's abilities
/// from the Shell Hardware Detection service, which asks the drive for its MMC features; every other process,
/// NeoShell's too, falls back to "CD Drive". The same features are asked here, as shsvcs' <c>_UpdateMMC2CDInfo</c>.
/// </summary>
public static unsafe class OpticalDrives
{
    private const uint IOCTL_CDROM_GET_CONFIGURATION = 0x24058;
    private const uint SCSI_GET_CONFIGURATION_REQUEST_TYPE_ONE = 2;

    // shsvcs' abilities: each needs all of its MMC features.
    internal const uint Mmc2 = 0x8000_0000, CdRead = 0x1, CdRecordable = 0x2, CdRewritable = 0x4, DvdRead = 0x10,
        DvdRecordable = 0x20, DvdRewritable = 0x40, DvdRam = 0x80, DvdPlusRecordable = 0x100, DvdPlusRewritable = 0x200,
        BluRayRead = 0x10_0000, BluRayRecordable = 0x20_0000, BluRayRewritable = 0x40_0000;

    private static readonly (uint Ability, ushort[] Features)[] s_abilities =
    [
        (Mmc2, [0x0]),
        (CdRead, [0x1E]),
        (CdRecordable, [0x21, 0x2D, 0x2E]),
        (CdRewritable, [0x23]),
        (DvdRead, [0x1F]),
        (DvdRecordable, [0x2F]),
        (DvdRewritable, [0x23]),
        (DvdRam, [0x20, 0x24]),
        (DvdPlusRecordable, [0x2B]),
        (DvdPlusRewritable, [0x2A]),
        (BluRayRead, [0x40]),
    ];

    // windows.storage's CMtPtLocal::_GetCDROMName: the first that matches names the drive.
    private static readonly (uint Mask, int Name)[] s_names =
    [
        (Mmc2 | BluRayRewritable, 9375), (Mmc2 | BluRayRecordable, 9374), (Mmc2 | BluRayRead, 9373),
        (Mmc2 | DvdRewritable, 9347), (Mmc2 | DvdPlusRewritable, 9347), (Mmc2 | DvdRecordable, 9346),
        (Mmc2 | DvdPlusRecordable, 9346), (Mmc2 | DvdRead | CdRewritable, 9348), (Mmc2 | DvdRead | CdRecordable, 9349),
        (Mmc2 | DvdRam, 9345), (Mmc2 | DvdRead, 9316), (Mmc2 | CdRewritable, 9350), (Mmc2 | CdRecordable, 9351),
    ];

    /// <summary>The <c>windows.storage.dll</c> string naming a drive with these abilities ("CD Drive" if none fits).</summary>
    internal static int NameFor(uint abilities)
    {
        foreach ((uint mask, int name) in s_names)
        {
            if ((abilities & mask) == mask)
                return name;
        }
        return CdDrive;
    }

    /// <summary>"CD Drive", what drives are called outside Explorer's process.</summary>
    internal const int CdDrive = 9317;

    /// <summary>The drive's type name as Explorer shows it, or null when the drive can't be asked.</summary>
    public static string? TypeName(string root)
    {
        using SafeFileHandle drive = Kernel32.CreateFile(
            $@"\\.\{root[..2]}", Kernel32.GENERIC_READ, Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE, 0, Kernel32.OPEN_EXISTING, 0, 0);
        if (drive.IsInvalid)
            return null;

        uint abilities = 0;
        foreach ((uint ability, ushort[] features) in s_abilities)
        {
            if (features.All(f => Feature(drive, f) is { } data && (f is not (0x2A or 0x2B) || (data.Length > 4 && (data[4] & 1) != 0))))
                abilities |= ability;
        }
        // BD writing: which versions of BD-RE and BD-R it writes.
        if (Feature(drive, 0x41) is { Length: >= 24 } bluRay)
        {
            if (bluRay[8..16].Any(b => b != 0))
                abilities |= BluRayRewritable;
            if (bluRay[16..24].Any(b => b != 0))
                abilities |= BluRayRecordable;
        }
        return AutoPlayHandlers.Text($@"@%SystemRoot%\system32\windows.storage.dll,-{NameFor(abilities)}");
    }

    // A feature descriptor (from its code on), or null when the drive doesn't have it.
    private static byte[]? Feature(SafeFileHandle drive, ushort feature)
    {
        // GET_CONFIGURATION_IOCTL_INPUT: the feature, the request type and two reserved pointers.
        uint* input = stackalloc uint[6];
        input[0] = feature;
        input[1] = SCSI_GET_CONFIGURATION_REQUEST_TYPE_ONE;
        input[2] = input[3] = input[4] = input[5] = 0;
        byte* output = stackalloc byte[64];
        if (!Kernel32.DeviceIoControl(drive, IOCTL_CDROM_GET_CONFIGURATION, input, 24, output, 64, out uint returned, 0) || returned < 12)
            return null;
        // An 8-byte header, then the descriptor: its code, flags and length.
        if ((output[8] << 8 | output[9]) != feature)
            return null;
        return new ReadOnlySpan<byte>(output + 8, (int)Math.Min(returned - 8, 4u + output[11])).ToArray();
    }
}
