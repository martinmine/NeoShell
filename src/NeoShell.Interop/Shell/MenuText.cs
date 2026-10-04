using System.Text;

namespace NeoShell.Interop.Shell;

internal static class MenuText
{
    /// <summary>
    /// A Win32 menu label as plain text: "&amp;" access-key markers removed ("&amp;&amp;" is a literal ampersand) and
    /// any shortcut after a tab dropped.
    /// </summary>
    public static string Clean(string label)
    {
        int tab = label.IndexOf('\t');
        if (tab >= 0)
            label = label[..tab];

        var text = new StringBuilder(label.Length);
        for (int i = 0; i < label.Length; i++)
        {
            if (label[i] == '&' && i + 1 < label.Length && label[i + 1] != '&')
                continue;
            if (label[i] == '&' && i + 1 < label.Length)
                i++;
            text.Append(label[i]);
        }
        return text.ToString().Trim();
    }
}
