using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Wintrust
{
    // SIF_AUTHENTICODE_SIGNED | SIF_CATALOG_SIGNED | SIF_VERSION_INFO
    public const uint SIF_ANY_SIGNATURE = 0x7;
    public const uint SIA_PUBLISHERNAME = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    public struct SIGNATURE_INFO
    {
        public uint cbSize;
        public int nSignatureState;
        public int nSignatureType;
        public uint dwSignatureInfoAvailability;
        public uint dwInfoAvailability;
        public char* pszDisplayName;
        public uint cchDisplayName;
        public char* pszPublisherName;
        public uint cchPublisherName;
        public char* pszMoreInfoURL;
        public uint cchMoreInfoURL;
        public byte* prgbHash;
        public uint cbHash;
        public int fOSBinary;
    }

    [LibraryImport("wintrust.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int WTGetSignatureInfo(string file, nint fileHandle, uint flags, SIGNATURE_INFO* info, nint certificate, nint stateData);
}
