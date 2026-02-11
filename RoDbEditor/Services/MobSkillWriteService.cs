using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

public sealed class MobSkillWriteService
{
    private readonly string _dataPath;
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

    public MobSkillWriteService(string dataPath)
    {
        _dataPath = dataPath ?? throw new ArgumentNullException(nameof(dataPath));
    }

    public string ImportFilePath => Path.Combine(_dataPath, "db", "import", "mob_skill_db.txt");

    public void EnsureImportFileExists()
    {
        var path = ImportFilePath;
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        if (!File.Exists(path))
        {
            // Minimal commented header to avoid parser treating a text header as a data row.
            File.WriteAllText(path,
                "// mob_skill_db.txt (import overlay)\r\n" +
                "// MobID,Dummy,State,SkillID,SkillLv,Rate,CastTime,Delay,Cancelable,Target,CondType,CondVal,Val1,Val2,Val3,Val4,Val5,Emotion,Chat\r\n",
                Utf8NoBom);
        }
        else
        {
            // Normalize existing file to UTF-8 without BOM to avoid rAthena line-1 parse issues.
            var text = File.ReadAllText(path, Encoding.UTF8);
            text = NormalizeImportFileText(text);
            File.WriteAllText(path, text, Utf8NoBom);
        }
    }

    public void AppendSkillRow(MobSkillDbRow row, string? provenance = null)
    {
        EnsureImportFileExists();

        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var prov = provenance ?? $"Added by RoDbEditor {stamp}";
        var line = ToCsvLine(row);

        File.AppendAllText(ImportFilePath, $"// {prov}\r\n{line}\r\n", Utf8NoBom);
    }

    public bool UpdateSkillRow(MobSkillDbRow original, MobSkillDbRow updated)
    {
        if (updated == null) throw new ArgumentNullException(nameof(updated));

        EnsureImportFileExists();
        if (IsImportSource(original.SourceFile))
            return ReplaceImportLine(original.SourceLine, ToCsvLine(updated));

        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        AppendSkillRow(updated, provenance: $"Override (was {original.SourceFile}:{original.SourceLine}) {stamp}");
        return true;
    }

    public bool DeleteSkillRow(MobSkillDbRow row)
    {
        if (row == null) throw new ArgumentNullException(nameof(row));
        EnsureImportFileExists();

        if (IsImportSource(row.SourceFile))
        {
            return CommentOutImportLine(row.SourceLine, $"Disabled by RoDbEditor {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        File.AppendAllText(ImportFilePath,
            $"// NOTE: Requested delete of official row {row.SourceFile}:{row.SourceLine} at {stamp}. " +
            $"Server will still use official entry unless overridden.\r\n",
            Utf8NoBom);
        return true;
    }

    /// <summary>
    /// Delete multiple skill rows. Rows in the import file are commented out.
    /// Deletes in reverse line order so indices stay valid.
    /// </summary>
    public int DeleteSkillRows(IEnumerable<MobSkillDbRow> rows)
    {
        if (rows == null) return 0;

        var inImport = rows.Where(r => r != null && IsImportSource(r.SourceFile))
            .OrderByDescending(r => r.SourceLine)
            .ToList();

        if (inImport.Count == 0) return 0;

        EnsureImportFileExists();
        var reason = $"Disabled by RoDbEditor {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        int deleted = 0;

        foreach (var row in inImport)
        {
            if (CommentOutImportLine(row.SourceLine, reason))
                deleted++;
            // Process highest line first; lower line numbers unchanged after each comment-out
        }

        return deleted;
    }

    private bool ReplaceImportLine(int sourceLine1Based, string replacementLine)
    {
        if (sourceLine1Based <= 0) return false;

        var lines = ReadAllLinesPreserveNewlineStyle(ImportFilePath, out var newline);
        var idx = sourceLine1Based - 1;
        if (idx < 0 || idx >= lines.Count) return false;
        lines[idx] = replacementLine;
        WriteAllLines(ImportFilePath, lines, newline);
        return true;
    }

    private bool CommentOutImportLine(int sourceLine1Based, string reason)
    {
        if (sourceLine1Based <= 0) return false;

        var lines = ReadAllLinesPreserveNewlineStyle(ImportFilePath, out var newline);
        var idx = sourceLine1Based - 1;
        if (idx < 0 || idx >= lines.Count) return false;

        var cur = lines[idx];
        if (!cur.TrimStart().StartsWith("//", StringComparison.Ordinal))
        {
            lines.Insert(idx, $"// {reason}");
            idx++;
            lines[idx] = "// " + cur;
        }
        else
        {
            lines.Insert(idx, $"// {reason}");
        }
        WriteAllLines(ImportFilePath, lines, newline);
        return true;
    }

    private bool IsImportSource(string? sourceFile)
    {
        if (string.IsNullOrWhiteSpace(sourceFile)) return false;

        var absImport = Path.GetFullPath(ImportFilePath);
        try
        {
            var absSource = Path.GetFullPath(sourceFile);
            return string.Equals(absSource, absImport, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return sourceFile.Replace('\\', '/')
                .EndsWith("/db/import/mob_skill_db.txt", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string ToCsvLine(MobSkillDbRow r)
    {
        var fields = new[]
        {
            r.MobId.ToString(CultureInfo.InvariantCulture),
            r.Dummy ?? "",
            r.State ?? "",
            r.SkillId.ToString(CultureInfo.InvariantCulture),
            r.SkillLv.ToString(CultureInfo.InvariantCulture),
            r.Rate.ToString(CultureInfo.InvariantCulture),
            r.CastTimeMs.ToString(CultureInfo.InvariantCulture),
            r.DelayMs.ToString(CultureInfo.InvariantCulture),
            r.Cancelable != 0 ? "yes" : "no",
            r.Target ?? "",
            r.ConditionType ?? "",
            r.ConditionValue.ToString(CultureInfo.InvariantCulture),
            r.Val1.ToString(CultureInfo.InvariantCulture),
            r.Val2.ToString(CultureInfo.InvariantCulture),
            r.Val3.ToString(CultureInfo.InvariantCulture),
            r.Val4.ToString(CultureInfo.InvariantCulture),
            r.Val5.ToString(CultureInfo.InvariantCulture),
            r.Emotion.ToString(CultureInfo.InvariantCulture),
            r.Chat ?? ""
        };

        return string.Join(",", fields.Select(SanitizeField).Select(EscapeCsv));
    }

    private static string EscapeCsv(string s)
    {
        if (s == null) return "";
        var needs = s.Contains(',') || s.Contains('"');
        if (!needs) return s;

        var escaped = s.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private static string SanitizeField(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";

        // rAthena parser is line-based; multiline CSV fields can break row parsing.
        s = s.Replace("\r", " ").Replace("\n", " ");

        // Strip potential BOM marker if it leaks into user-entered text.
        if (s.Length > 0 && s[0] == '\uFEFF')
            s = s.Substring(1);

        return s;
    }

    private static string NormalizeImportFileText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Strip UTF-8 BOM marker at the beginning if present.
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text.Substring(1);

        // If legacy un-commented header exists, comment it out so rAthena won't parse it as data.
        const string legacyHeaderPrefix = "MobID,Dummy,State,SkillID,SkillLv,Rate,CastTime,Delay,Cancelable,Target,CondType,CondVal,Val1,Val2,Val3,Val4,Val5,Emotion,Chat";
        if (text.StartsWith(legacyHeaderPrefix, StringComparison.Ordinal))
            text = "// " + text;

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            // Comment out known non-data header lines even if they appear later in the file.
            if (string.Equals(lines[i].Trim(), legacyHeaderPrefix, StringComparison.Ordinal))
                lines[i] = "// " + lines[i];
        }

        return string.Join(newline, lines);
    }

    private static List<string> ReadAllLinesPreserveNewlineStyle(string path, out string newline)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return text.Replace("\r\n", "\n").Split(new[] { '\n' }, StringSplitOptions.None).ToList();
    }

    private static void WriteAllLines(string path, List<string> lines, string newline)
    {
        var text = string.Join(newline, lines);
        File.WriteAllText(path, text, Utf8NoBom);
    }
}
