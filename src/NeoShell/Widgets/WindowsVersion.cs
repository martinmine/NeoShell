using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NeoShell.Widgets;

/// <summary>Which Windows this is, as winver writes it: "Windows 11 Pro", version 25H2, OS build 26200.6584.</summary>
public sealed record WindowsVersion(string Name, string Version, string Build, string Architecture, DateTime? Installed)
{
    public static WindowsVersion Read()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        string build = key?.GetValue("CurrentBuild") as string ?? Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);
        string revision = key?.GetValue("UBR") is int ubr ? "." + ubr.ToString(CultureInfo.InvariantCulture) : "";
        return new WindowsVersion(
            ProductName(key?.GetValue("ProductName") as string ?? "Windows", build),
            key?.GetValue("DisplayVersion") as string ?? "",
            build + revision,
            ArchitectureName(RuntimeInformation.OSArchitecture),
            key?.GetValue("InstallDate") is int seconds && seconds != 0
                ? DateTimeOffset.FromUnixTimeSeconds((uint)seconds).LocalDateTime
                : null);
    }

    /// <summary>
    /// Windows 11 still calls itself "Windows 10" in the registry's ProductName (as apps that only check for 10 expect);
    /// winver tells them apart by build, as does this.
    /// </summary>
    public static string ProductName(string registryName, string build) =>
        int.TryParse(build, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number >= 22000
            && registryName.StartsWith("Windows 10", StringComparison.Ordinal)
            ? "Windows 11" + registryName["Windows 10".Length..]
            : registryName;

    public static string ArchitectureName(System.Runtime.InteropServices.Architecture architecture) => architecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "64-bit (x64)",
        System.Runtime.InteropServices.Architecture.Arm64 => "64-bit (ARM64)",
        System.Runtime.InteropServices.Architecture.X86 => "32-bit (x86)",
        _ => architecture.ToString(),
    };
}
