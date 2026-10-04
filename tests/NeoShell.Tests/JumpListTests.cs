using System.Text;
using NeoShell.Interop.Shell;

namespace NeoShell.Tests;

public sealed class JumpListTests
{
    private const uint Footer = 0xBABFFBAB;
    private static readonly Guid ShellLink = new("00021401-0000-0000-c000-000000000046");

    // A shell link: the 0x4C-byte header, a name if given, an extra data block of 12 bytes, the terminal block.
    private static byte[] Link(string? name = null)
    {
        var data = new List<byte>();
        byte[] header = new byte[0x4C];
        BitConverter.GetBytes(0x4C).CopyTo(header, 0);
        BitConverter.GetBytes(name is null ? 0 : 0x4).CopyTo(header, 20);
        data.AddRange(header);
        if (name is not null)
        {
            data.AddRange(BitConverter.GetBytes((ushort)name.Length));
            data.AddRange(Encoding.Unicode.GetBytes(name));
        }
        data.AddRange(BitConverter.GetBytes(12));
        data.AddRange(new byte[8]);
        data.AddRange(BitConverter.GetBytes(0));
        return [.. data];
    }

    private static byte[] File(params byte[][] categories)
    {
        var data = new List<byte>();
        data.AddRange(BitConverter.GetBytes(2));
        data.AddRange(BitConverter.GetBytes(categories.Length));
        data.AddRange(BitConverter.GetBytes(0));
        foreach (byte[] category in categories)
            data.AddRange(category);
        return [.. data];
    }

    private static byte[] Category(int kind, string? title, int known, params byte[][] links)
    {
        var data = new List<byte>(BitConverter.GetBytes(kind));
        if (kind == 0)
        {
            data.AddRange(BitConverter.GetBytes((ushort)title!.Length));
            data.AddRange(Encoding.Unicode.GetBytes(title));
        }
        if (kind == 1)
        {
            data.AddRange(BitConverter.GetBytes(known));
        }
        else
        {
            data.AddRange(BitConverter.GetBytes(links.Length));
            foreach (byte[] link in links)
            {
                data.AddRange(ShellLink.ToByteArray());
                data.AddRange(link);
            }
        }
        data.AddRange(BitConverter.GetBytes(Footer));
        return [.. data];
    }

    [Fact]
    public void Link_length_covers_header_strings_and_extra_data()
    {
        Assert.Equal(0x4C + 16, CustomDestinations.LinkLength(Link()));
        Assert.Equal(0x4C + 2 + 10 + 16, CustomDestinations.LinkLength(Link("hello")));
    }

    [Fact]
    public void Categories_are_read_in_order()
    {
        byte[] newWindow = Link("New window");
        byte[] folder = Link("C:\\source");
        IReadOnlyList<DestinationCategory> categories = CustomDestinations.Parse(File(
            Category(2, null, 0, newWindow, Link()),
            Category(0, "Recent Folders", 0, folder),
            Category(1, null, 2)));

        Assert.Equal(3, categories.Count);
        Assert.Equal(DestinationCategoryKind.Tasks, categories[0].Kind);
        Assert.Equal(newWindow, categories[0].Links[0]);
        Assert.Equal(2, categories[0].Links.Count);
        Assert.Equal(("Recent Folders", DestinationCategoryKind.Custom), (categories[1].Title, categories[1].Kind));
        Assert.Equal(folder, categories[1].Links[0]);
        Assert.Equal((DestinationCategoryKind.Known, 2), (categories[2].Kind, categories[2].Known));
    }

    [Fact]
    public void A_cut_short_file_keeps_the_categories_before_the_break()
    {
        byte[] file = File(Category(1, null, 1), Category(2, null, 0, Link("Task")));
        IReadOnlyList<DestinationCategory> categories = CustomDestinations.Parse(file.AsSpan(0, file.Length - 10));

        Assert.Equal((DestinationCategoryKind.Known, 1), (Assert.Single(categories).Kind, categories[0].Known));
    }

    [Fact]
    public void Unknown_versions_and_empty_files_give_nothing()
    {
        Assert.Empty(CustomDestinations.Parse([]));
        byte[] file = File(Category(1, null, 2));
        file[0] = 3;
        Assert.Empty(CustomDestinations.Parse(file));
    }

    [Theory]
    [InlineData("MSEdge", "ccba5a5986c77e43")]
    [InlineData("Microsoft.ScreenSketch_8wekyb3d8bbwe!App", "1c7a9be1b15a03ba")]
    [InlineData("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", "a61657a5e5dfbdc")] // no leading zero
    [InlineData(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\notepad.exe", "9b9cdc69c1c24e2b")]
    [InlineData(@"{1ac14e77-02e7-4e5d-b744-2eb1ae5198b7}\NOTEPAD.EXE", "9b9cdc69c1c24e2b")]
    public void File_name_is_the_crc64_of_the_app_id_as_windows_names_it(string appId, string expected)
    {
        Assert.Equal(expected, JumpLists.FileName(appId));
    }

    [Fact]
    public void Implicit_app_id_starts_with_the_longest_known_folder()
    {
        Guid windows = new("F38BF404-1D43-42F2-9305-67DE0B28FC23");
        Guid system = new("1AC14E77-02E7-4E5D-B744-2EB1AE5198B7");
        (Guid, string?)[] folders = [(windows, @"C:\Windows"), (system, @"C:\Windows\System32"), (Guid.Empty, null)];

        Assert.Equal(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe",
            JumpLists.ImplicitAppId(@"C:\WINDOWS\system32\WindowsPowerShell\v1.0\powershell.exe", folders));
        Assert.Equal(@"{F38BF404-1D43-42F2-9305-67DE0B28FC23}\regedit.exe", JumpLists.ImplicitAppId(@"C:\Windows\regedit.exe", folders));
        Assert.Equal(@"D:\Tools\app.exe", JumpLists.ImplicitAppId(@"D:\Tools\app.exe", folders));
    }
}
