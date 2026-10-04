using System.Buffers.Binary;
using System.Text;

namespace NeoShell.Interop.Shell;

internal enum DestinationCategoryKind { Custom = 0, Known = 1, Tasks = 2 }

/// <summary>One category of a custom jump list: the app's own (with a title), a known one, or its tasks.</summary>
/// <param name="Known">For a known category: 1 Frequent, 2 Recent.</param>
/// <param name="Links">Each entry as a shell link's persisted data (what <c>IPersistStream::Load</c> reads).</param>
internal sealed record DestinationCategory(DestinationCategoryKind Kind, string Title, int Known, IReadOnlyList<byte[]> Links);

/// <summary>
/// Reads the jump list an app set with <c>ICustomDestinationList</c>, which Windows keeps in
/// <c>Recent\CustomDestinations\&lt;hash of the AppID&gt;.customDestinations-ms</c>. There's no API to read it back.
/// </summary>
/// <remarks>
/// The file: version (2), category count, a reserved zero; then each category: its kind; for a custom one the title
/// (a UTF-16 count and characters), for a known one its ID; the entry count and the entries (custom and tasks); and
/// a 0xBABFFBAB footer. An entry is a CLSID followed by the object's persisted data, as <c>OleLoadFromStream</c>
/// reads it; in practice always a shell link, whose data ([MS-SHLLINK]) carries no length, so it's measured here.
/// </remarks>
internal static class CustomDestinations
{
    private const uint Footer = 0xBABFFBAB;
    private static readonly Guid s_shellLink = new("00021401-0000-0000-c000-000000000046");

    /// <summary>The categories in the file's order; parsing stops at anything it doesn't understand.</summary>
    public static IReadOnlyList<DestinationCategory> Parse(ReadOnlySpan<byte> file)
    {
        var categories = new List<DestinationCategory>();
        try
        {
            if (file.Length < 12 || ReadUInt32(file, 0) != 2)
                return categories;

            uint count = ReadUInt32(file, 4);
            int offset = 12;
            for (uint i = 0; i < count; i++)
            {
                var kind = (DestinationCategoryKind)ReadUInt32(file, offset);
                offset += 4;
                string title = "";
                int known = 0;
                var links = new List<byte[]>();
                switch (kind)
                {
                    case DestinationCategoryKind.Custom:
                        int characters = BinaryPrimitives.ReadUInt16LittleEndian(file[offset..]);
                        title = Encoding.Unicode.GetString(file.Slice(offset + 2, 2 * characters));
                        offset += 2 + 2 * characters;
                        offset = ReadLinks(file, offset, links);
                        break;
                    case DestinationCategoryKind.Known:
                        known = (int)ReadUInt32(file, offset);
                        offset += 4;
                        break;
                    case DestinationCategoryKind.Tasks:
                        offset = ReadLinks(file, offset, links);
                        break;
                    default:
                        return categories;
                }
                if (ReadUInt32(file, offset) != Footer)
                    return categories;
                offset += 4;
                categories.Add(new DestinationCategory(kind, title, known, links));
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Cut short; what was read before stands.
        }
        return categories;
    }

    private static int ReadLinks(ReadOnlySpan<byte> file, int offset, List<byte[]> links)
    {
        uint count = ReadUInt32(file, offset);
        offset += 4;
        for (uint i = 0; i < count; i++)
        {
            if (new Guid(file.Slice(offset, 16)) != s_shellLink)
                throw new ArgumentOutOfRangeException(nameof(file), "Not a shell link");
            offset += 16;
            int length = LinkLength(file[offset..]);
            links.Add(file.Slice(offset, length).ToArray());
            offset += length;
        }
        return offset;
    }

    /// <summary>The length of a shell link's data ([MS-SHLLINK]): header, ID list, link info, strings, extra data.</summary>
    internal static int LinkLength(ReadOnlySpan<byte> link)
    {
        const int HeaderSize = 0x4C;
        if (ReadUInt32(link, 0) != HeaderSize)
            throw new ArgumentOutOfRangeException(nameof(link), "Not a shell link header");

        uint flags = ReadUInt32(link, 20);
        int offset = HeaderSize;
        if ((flags & 0x1) != 0) // HasLinkTargetIDList
            offset += 2 + BinaryPrimitives.ReadUInt16LittleEndian(link[offset..]);
        if ((flags & 0x2) != 0) // HasLinkInfo
            offset += (int)ReadUInt32(link, offset);
        // Name, relative path, working directory, arguments, icon location: each a count and UTF-16 characters.
        foreach (uint flag in (uint[])[0x4, 0x8, 0x10, 0x20, 0x40])
        {
            if ((flags & flag) != 0)
                offset += 2 + 2 * BinaryPrimitives.ReadUInt16LittleEndian(link[offset..]);
        }
        // Extra data blocks, each starting with its size, up to a terminal block smaller than 4.
        while (true)
        {
            uint size = ReadUInt32(link, offset);
            if (size < 4)
                return offset + 4;
            offset += (int)size;
        }
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
}
