using System;
using System.Linq;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

public class EntityDesignationService
{
    private readonly NpcIndexService _npcIndexService;
    private readonly MobDbService _mobDbService;

    public EntityDesignationService(NpcIndexService npcIndexService, MobDbService mobDbService)
    {
        _npcIndexService = npcIndexService;
        _mobDbService = mobDbService;
    }

    public string ApplyNpcSprite(NpcScriptEntry npc, string spriteKey)
    {
        if (npc == null) throw new ArgumentNullException(nameof(npc));
        npc.SpriteId = spriteKey;
        _npcIndexService.SaveNpc(npc);
        return $"NPC sprite set to '{spriteKey}' in {npc.FilePath}";
    }

    public MobEntry CreateOrUpdateCustomMonsterFrom(MobEntry baseMob, string spriteKey)
    {
        if (baseMob == null) throw new ArgumentNullException(nameof(baseMob));

        var existing = _mobDbService.Mobs.FirstOrDefault(m =>
            string.Equals(m.AegisName, spriteKey, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return existing;

        var custom = new MobEntry
        {
            Id = _mobDbService.GetNextCustomMobId(),
            AegisName = spriteKey,
            Name = string.IsNullOrWhiteSpace(baseMob.Name) ? $"{baseMob.AegisName}_Custom" : $"{baseMob.Name} (Custom)",
            Level = baseMob.Level,
            Hp = baseMob.Hp,
            Sp = baseMob.Sp,
            BaseExp = baseMob.BaseExp,
            JobExp = baseMob.JobExp,
            MvpExp = baseMob.MvpExp,
            Attack = baseMob.Attack,
            Attack2 = baseMob.Attack2,
            Defense = baseMob.Defense,
            MagicDefense = baseMob.MagicDefense,
            Str = baseMob.Str,
            Agi = baseMob.Agi,
            Vit = baseMob.Vit,
            Int = baseMob.Int,
            Dex = baseMob.Dex,
            Luk = baseMob.Luk,
            AttackRange = baseMob.AttackRange,
            SkillRange = baseMob.SkillRange,
            ChaseRange = baseMob.ChaseRange,
            Size = baseMob.Size,
            Race = baseMob.Race,
            Element = baseMob.Element,
            ElementLevel = baseMob.ElementLevel,
            WalkSpeed = baseMob.WalkSpeed,
            AttackDelay = baseMob.AttackDelay,
            AttackMotion = baseMob.AttackMotion,
            DamageMotion = baseMob.DamageMotion,
            Ai = baseMob.Ai,
            Class = baseMob.Class,
            Modes = baseMob.Modes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            Drops = baseMob.Drops.Select(d => new MobDropEntry
            {
                Item = d.Item,
                Rate = d.Rate,
                StealProtected = d.StealProtected,
                Index = d.Index
            }).ToList(),
            MvpDrops = baseMob.MvpDrops.Select(d => new MobDropEntry
            {
                Item = d.Item,
                Rate = d.Rate,
                StealProtected = d.StealProtected,
                Index = d.Index
            }).ToList()
        };

        _mobDbService.AddMob(custom);
        _mobDbService.SaveMob(custom);
        return custom;
    }
}

