using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RoDbEditor.Models;
using RoDbEditor.Services;

namespace RoDbEditor.Services.Export;

public sealed class MobBundleExporter
{
    private readonly Func<int, IReadOnlyList<MobSkillDbRow>>? _mobSkillProvider;

    public MobBundleExporter(Func<int, IReadOnlyList<MobSkillDbRow>>? mobSkillProvider = null)
    {
        _mobSkillProvider = mobSkillProvider;
    }

    public void AddMob(ExportBundle bundle, MobEntry mob, bool includeMobAvail, bool includeMobSkills, bool includeAssetNotes)
    {
        bundle.Files.Add(new ExportFile(
            "db/import/mob_db.yml",
            RenderMobDbYamlBodyEntry(mob),
            ExportWriteMode.Append));

        if (includeMobAvail && !string.IsNullOrWhiteSpace(mob.AegisName))
        {
            bundle.Files.Add(new ExportFile(
                "db/import/mob_avail.yml",
                RenderMobAvailYamlEntry(mob),
                ExportWriteMode.Append));
        }

        if (includeMobSkills && _mobSkillProvider != null)
        {
            var rows = _mobSkillProvider(mob.Id);
            if (rows.Count > 0)
            {
                bundle.Files.Add(new ExportFile(
                    "db/import/mob_skill_db.txt",
                    RenderMobSkillLines(mob, rows),
                    ExportWriteMode.Append));
            }
        }

        bundle.Commands.Add("@reloadmobdb");
        bundle.Notes.Add("Use import overlay only: db/import/mob_db.yml.");
        if (includeAssetNotes)
            bundle.Notes.Add("Install mob sprite assets through your client overlay/GRF pipeline.");
    }

    private static string RenderMobDbYamlBodyEntry(MobEntry mob)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  - Id: {mob.Id}");
        sb.AppendLine($"    AegisName: {mob.AegisName}");
        sb.AppendLine($"    Name: {EscapeYaml(mob.Name)}");
        if (mob.Level > 0) sb.AppendLine($"    Level: {mob.Level}");
        if (mob.Hp > 0) sb.AppendLine($"    Hp: {mob.Hp}");
        if (mob.Sp > 0) sb.AppendLine($"    Sp: {mob.Sp}");
        if (mob.BaseExp > 0) sb.AppendLine($"    BaseExp: {mob.BaseExp}");
        if (mob.JobExp > 0) sb.AppendLine($"    JobExp: {mob.JobExp}");
        if (mob.MvpExp > 0) sb.AppendLine($"    MvpExp: {mob.MvpExp}");
        if (mob.Attack > 0) sb.AppendLine($"    Attack: {mob.Attack}");
        if (mob.Attack2 > 0) sb.AppendLine($"    Attack2: {mob.Attack2}");
        if (mob.Defense > 0) sb.AppendLine($"    Defense: {mob.Defense}");
        if (mob.MagicDefense > 0) sb.AppendLine($"    MagicDefense: {mob.MagicDefense}");
        if (mob.Str > 0) sb.AppendLine($"    Str: {mob.Str}");
        if (mob.Agi > 0) sb.AppendLine($"    Agi: {mob.Agi}");
        if (mob.Vit > 0) sb.AppendLine($"    Vit: {mob.Vit}");
        if (mob.Int > 0) sb.AppendLine($"    Int: {mob.Int}");
        if (mob.Dex > 0) sb.AppendLine($"    Dex: {mob.Dex}");
        if (mob.Luk > 0) sb.AppendLine($"    Luk: {mob.Luk}");
        if (mob.AttackRange > 0) sb.AppendLine($"    AttackRange: {mob.AttackRange}");
        if (mob.SkillRange > 0) sb.AppendLine($"    SkillRange: {mob.SkillRange}");
        if (mob.ChaseRange > 0) sb.AppendLine($"    ChaseRange: {mob.ChaseRange}");
        if (!string.IsNullOrWhiteSpace(mob.Size)) sb.AppendLine($"    Size: {mob.Size}");
        if (!string.IsNullOrWhiteSpace(mob.Race)) sb.AppendLine($"    Race: {mob.Race}");
        if (!string.IsNullOrWhiteSpace(mob.Element)) sb.AppendLine($"    Element: {mob.Element}");
        if (mob.ElementLevel > 0) sb.AppendLine($"    ElementLevel: {mob.ElementLevel}");
        if (mob.WalkSpeed > 0) sb.AppendLine($"    WalkSpeed: {mob.WalkSpeed}");
        if (mob.AttackDelay > 0) sb.AppendLine($"    AttackDelay: {mob.AttackDelay}");
        if (mob.AttackMotion > 0) sb.AppendLine($"    AttackMotion: {mob.AttackMotion}");
        if (mob.DamageMotion > 0) sb.AppendLine($"    DamageMotion: {mob.DamageMotion}");
        if (!string.IsNullOrWhiteSpace(mob.Ai)) sb.AppendLine($"    Ai: {mob.Ai}");
        if (!string.IsNullOrWhiteSpace(mob.Class)) sb.AppendLine($"    Class: {mob.Class}");
        if (mob.Modes.Count > 0)
        {
            var enabled = mob.Modes.Where(kv => kv.Value).ToList();
            if (enabled.Count > 0)
            {
                sb.AppendLine("    Modes:");
                foreach (var kv in enabled)
                    sb.AppendLine($"      {kv.Key}: true");
            }
        }
        if (mob.Drops.Count > 0)
        {
            sb.AppendLine("    Drops:");
            foreach (var drop in mob.Drops)
                sb.AppendLine($"      - Item: {drop.Item}\n        Rate: {drop.Rate}");
        }
        if (mob.MvpDrops.Count > 0)
        {
            sb.AppendLine("    MvpDrops:");
            foreach (var drop in mob.MvpDrops)
                sb.AppendLine($"      - Item: {drop.Item}\n        Rate: {drop.Rate}");
        }
        sb.AppendLine($"    # RoDbEditor: {DateTime.Now:yyyy-MM-dd HH:mm:ss} MOB {mob.Id} {mob.AegisName}");
        return sb.ToString();
    }

    private static string RenderMobAvailYamlEntry(MobEntry mob)
    {
        var aegis = (mob.AegisName ?? "").ToUpperInvariant();
        var sb = new StringBuilder();
        sb.AppendLine($"  - Mob: {aegis}");
        sb.AppendLine($"    Sprite: {aegis}");
        sb.AppendLine($"    # RoDbEditor: {DateTime.Now:yyyy-MM-dd HH:mm:ss} MOB_AVAIL {mob.Id}");
        return sb.ToString();
    }

    private static string RenderMobSkillLines(MobEntry mob, IReadOnlyList<MobSkillDbRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// RoDbEditor: {DateTime.Now:yyyy-MM-dd HH:mm:ss} MOB_SKILLS {mob.Id} {mob.AegisName}");
        foreach (var row in rows)
            sb.AppendLine(MobSkillWriteService.ToCsvLine(row));
        return sb.ToString();
    }

    private static string EscapeYaml(string value)
        => value?.Replace("\"", "''") ?? "";
}
