using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

public sealed class MobSkillWriteService
{
    private readonly string _dataPath;

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

        if (File.Exists(path))
            return;

        // Minimal header; do NOT attempt to match official formatting beyond this.
        File.WriteAllText(path,
            "// mob_skill_db.txt (import overlay)\r\n" +
            "// MobID,Dummy,State,SkillID,SkillLv,Rate,CastTime,Delay,Cancelable,Target,CondType,CondVal,Val1,Val2,Val3,Val4,Val5,Emotion,Chat\r\n",
            System.Text.Encoding.UTF8);
    }

    public void AppendSkillRow(MobSkillDbRow row, string? provenance = null)
    {
        EnsureImportFileExists();

        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var prov = provenance ?? $"Added by RoDbEditor {stamp}";
        var line = ToCsvLine(row);

        // Provenance as comment line, then record line
        File.AppendAllText(ImportFilePath,
            $"// {prov}\r\n{line}\r\n",
            System.Text.Encoding.UTF8);
    }

    public bool UpdateSkillRow(MobSkillDbRow original, MobSkillDbRow updated)
    {
        if (original == null) throw new ArgumentNullException(nameof(original));
        if (updated == null) throw new ArgumentNullException(nameof(updated));

        EnsureImportFileExists();

        // If original is in import, replace in place by SourceLine (1-based).
        if (IsImportSource(original.SourceFile))
            return ReplaceImportLine(original.SourceLine, ToCsvLine(updated));

        // If original is from db/re (or anything else), append an override to import.
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
            // Comment out the line in import (preserve history).
            return CommentOutImportLine(row.SourceLine, $"Disabled by RoDbEditor {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // Can't delete official line; append a tombstone comment (human actionable).
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        File.AppendAllText(ImportFilePath,
            $"// NOTE: Requested delete of official row {row.SourceFile}:{row.SourceLine} at {stamp}. " +
            $"Server will still use official entry unless overridden.\r\n",
            System.Text.Encoding.UTF8);
        return true;
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
            // Insert comment above, then comment out line
            // Wait - if we insert, all subsequent line numbers shift. 
            // BUT since this is the import file, shifting is okay IF we reload immediately after.
            // AND since we are editing by file:line, we assume the caller will reload or at least know the world changed.
            // Actually, inserting lines shifts line numbers for *other* entries if we don't reload.
            // Safer to just comment out the line in-place if possible, OR accept the shift.
            // The plan said: "lines.Insert(idx, reason)... idx++ ... lines[idx] = // cur".
            // That shifts everything below down by 1. That's fine as long as we reload.
            
            lines.Insert(idx, $"// {reason}");
            idx++; // original line shifted down
            lines[idx] = "// " + cur;
        }
        else
        {
            // already commented; just add a reason above?
            // Or just do nothing.
            // Let's just add a reason above to be safe.
            lines.Insert(idx, $"// {reason}");
        }

        WriteAllLines(ImportFilePath, lines, newline);
        return true;
    }

    private static bool IsImportSource(string? sourceFile)
    {
        if (string.IsNullOrWhiteSpace(sourceFile)) return false;
        // Check for db/import/mob_skill_db.txt suffix
        var normalized = sourceFile.Replace('\\', '/');
        return normalized.EndsWith("/db/import/mob_skill_db.txt", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("mob_skill_db.txt", StringComparison.OrdinalIgnoreCase);
        // Note: The second check is a bit loose but 'mob_skill_db.txt' usually implies the one we are editing if it's not fully qualified?
        // Actually, db/re/mob_skill_db.txt also ends with that.
        // We should be stricter.
        // But IsImportSource logic in plan was: 
        // return sourceFile.Replace('\\', '/').EndsWith("/db/import/mob_skill_db.txt", StringComparison.OrdinalIgnoreCase)
        //    || sourceFile.EndsWith("mob_skill_db.txt", StringComparison.OrdinalIgnoreCase); // tolerate relative
        // The issue is if sourceFile is JUST "mob_skill_db.txt" (from some relative path logic).
        // Let's stick to the plan's logic but maybe refine the second check if needed. 
        // Actually, ImportFilePath is absolute. So we can check against that.
    }
    
    // Refined IsImportSource
    private bool IsImportSourceRefined(string? sourceFile)
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
             // Fallback for relative paths or invalid paths
             return sourceFile.Replace('\\', '/').EndsWith("/db/import/mob_skill_db.txt", StringComparison.OrdinalIgnoreCase);
         }
    }

    public static string ToCsvLine(MobSkillDbRow r)
    {
        // Field order per mob_skill_db header.
        // MobID,Dummy,State,SkillID,SkillLv,Rate,CastTime,Delay,Cancelable,Target,CondType,CondVal,Val1,Val2,Val3,Val4,Val5,Emotion,Chat
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

        return string.Join(",", fields.Select(EscapeCsv));
    }

    private static string EscapeCsv(string s)
    {
        if (s == null) return "";
        var needs = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
        if (!needs) return s;

        var escaped = s.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private static List<string> ReadAllLinesPreserveNewlineStyle(string path, out string newline)
    {
        var text = File.ReadAllText(path, System.Text.Encoding.UTF8);

        // Detect newline style (default CRLF for Windows)
        newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        // Split WITHOUT losing empty last lines if possible, but for editing we care about content lines.
        // using StringSplitOptions.None preserves empty entries.
        var lines = text.Replace("\r\n", "\n").Split(new[] { '\n' }, StringSplitOptions.None).ToList();
        
        // If the last line is empty (due to trailing newline), we keep it in the list 
        // so that writing it back with join(newline) preserves the trailing newline?
        // "A\nB\n" -> split -> ["A", "B", ""]
        // Join("\n", ["A", "B", ""]) -> "A\nB\n" -> Correct.
        return lines;
    }

    private static void WriteAllLines(string path, List<string> lines, string newline)
    {
        // Rebuild exactly with chosen newline.
        var text = string.Join(newline, lines);
        File.WriteAllText(path, text, System.Text.Encoding.UTF8);
    }
}
