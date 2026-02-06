using System.Linq;
using System.Text;

namespace RoDbEditor.Core;

public static class SimpleDiff
{
    static readonly string[] CrLf = { "\r\n", "\r", "\n" };

    /// <summary>Format for diff view: original lines prefixed with -, current with +.</summary>
    public static string ToUnifiedDiff(string original, string current)
    {
        var a = (original ?? "").Split(CrLf, System.StringSplitOptions.None);
        var b = (current ?? "").Split(CrLf, System.StringSplitOptions.None);
        var sb = new StringBuilder();
        int i = 0, j = 0;
        while (i < a.Length || j < b.Length)
        {
            if (i < a.Length && j < b.Length && a[i] == b[j])
            {
                sb.Append(' ').AppendLine(a[i]);
                i++;
                j++;
            }
            else if (j < b.Length && (i >= a.Length || !ContainsAt(a, b[j], i)))
            {
                sb.Append('+').AppendLine(b[j]);
                j++;
            }
            else
            {
                sb.Append('-').AppendLine(a[i]);
                i++;
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static bool ContainsAt(string[] a, string line, int start)
    {
        for (int k = start; k < a.Length; k++)
            if (a[k] == line) return true;
        return false;
    }

    public static bool HasChanges(string original, string current)
    {
        return (original ?? "") != (current ?? "");
    }
}
