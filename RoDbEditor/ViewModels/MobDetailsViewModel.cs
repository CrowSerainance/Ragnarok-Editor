using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RoDbEditor.Models;
using RoDbEditor.Services;

namespace RoDbEditor.ViewModels;

public class MobDetailsViewModel : ObservableObject
{
    private readonly AttrFixTableService? _attrFix;
    private readonly MapIndexService? _mapIndex;
    private readonly MobSkillDbService? _mobSkillDb;
    private readonly SkillDbService? _skillDb;
    private readonly MobDbService? _mobDb;
    private string _name = "";
    private string _aegisName = "";
    private int _level = 1;
    private int _hp = 1;
    private int _baseExp;
    private int _jobExp;
    private int _attack;
    private int _attack2;
    private int _defense;
    private int _magicDefense;
    private int _str = 1;
    private int _agi = 1;
    private int _vit = 1;
    private int _int = 1;
    private int _dex = 1;
    private int _luk = 1;
    private string _race = "Formless";
    private string _element = "Neutral";
    private int _elementLevel = 1;
    private string _size = "Medium";
    private int _walkSpeed = 200;
    private int _attackDelay;
    private int _attackRange = 1;
    private int _skillRange = 1;
    private int _chaseRange = 1;

    public int Id { get; }
    public ObservableCollection<MobDropEntry> Drops { get; } = new();
    public ObservableCollection<MobDropEntry> MvpDrops { get; } = new();
    public ObservableCollection<ElementModifierRow> ElementRows { get; } = new();
    public ObservableCollection<SpawnGroupViewModel> SpawnGroups { get; } = new();
    public ObservableCollection<ModeToggleRow> Modes { get; } = new();
    public ObservableCollection<MobSkillRow> MonsterSkills { get; } = new();
    public ObservableCollection<MobSlaveRow> Slaves { get; } = new();

    public IReadOnlyList<string> RaceOptions { get; } = new[] { "Formless", "Undead", "Brute", "Plant", "Insect", "Fish", "Demon", "Demihuman", "Angel", "Dragon", "Boss", "Non-Boss" };
    public IReadOnlyList<string> ElementOptions { get; } = AttrFixTableService.Elements;
    public IReadOnlyList<int> ElementLevelOptions { get; } = new[] { 1, 2, 3, 4 };
    public IReadOnlyList<string> SizeOptions { get; } = new[] { "Small", "Medium", "Large" };

    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public string AegisName { get => _aegisName; set => Set(ref _aegisName, value ?? ""); }
    public int Level { get => _level; set { Set(ref _level, value); Raise(nameof(Hit100)); Raise(nameof(Flee95)); Raise(nameof(BaseExpPerHp)); Raise(nameof(JobExpPerHp)); } }
    public int Hp { get => _hp; set { Set(ref _hp, value); Raise(nameof(BaseExpPerHp)); Raise(nameof(JobExpPerHp)); } }
    public int BaseExp { get => _baseExp; set { Set(ref _baseExp, value); Raise(nameof(BaseExpPerHp)); } }
    public int JobExp { get => _jobExp; set { Set(ref _jobExp, value); Raise(nameof(JobExpPerHp)); } }
    public int Attack { get => _attack; set => Set(ref _attack, value); }
    public int Attack2 { get => _attack2; set => Set(ref _attack2, value); }
    public int Defense { get => _defense; set => Set(ref _defense, value); }
    public int MagicDefense { get => _magicDefense; set => Set(ref _magicDefense, value); }
    public int Str { get => _str; set { Set(ref _str, value); Raise(nameof(Hit100)); Raise(nameof(Flee95)); } }
    public int Agi { get => _agi; set { Set(ref _agi, value); Raise(nameof(Flee95)); } }
    public int Vit { get => _vit; set => Set(ref _vit, value); }
    public int Int { get => _int; set => Set(ref _int, value); }
    public int Dex { get => _dex; set { Set(ref _dex, value); Raise(nameof(Hit100)); } }
    public int Luk { get => _luk; set => Set(ref _luk, value); }
    public string Race { get => _race; set => Set(ref _race, value ?? "Formless"); }
    public string Element { get => _element; set { Set(ref _element, value ?? "Neutral"); RefreshElementRows(); } }
    public int ElementLevel { get => _elementLevel; set { Set(ref _elementLevel, value); RefreshElementRows(); } }
    public string Size { get => _size; set => Set(ref _size, value ?? "Medium"); }
    public int WalkSpeed { get => _walkSpeed; set => Set(ref _walkSpeed, value); }
    public int AttackDelay { get => _attackDelay; set => Set(ref _attackDelay, value); }
    public int AttackRange { get => _attackRange; set => Set(ref _attackRange, value); }
    public int SkillRange { get => _skillRange; set => Set(ref _skillRange, value); }
    public int ChaseRange { get => _chaseRange; set => Set(ref _chaseRange, value); }

    public int Hit100 => Level + Dex + 175;
    public int Flee95 => Level + Agi + 100;
    public int AttackMin => Attack;
    public int AttackMax => Attack2 > 0 ? Attack2 : Attack;
    public string AttackRangeText => Attack2 > 0 ? $"{Attack}-{Attack2}" : Attack.ToString();
    public double BaseExpPerHp => Hp > 0 ? (double)BaseExp / Hp : 0;
    public double JobExpPerHp => Hp > 0 ? (double)JobExp / Hp : 0;

    public MobDetailsViewModel(MobEntry mob, AttrFixTableService? attrFix, SpawnParser? spawnParser, MapIndexService? mapIndex, MobSkillDbService? mobSkillDb, SkillDbService? skillDb, MobDbService? mobDb)
    {
        _attrFix = attrFix;
        _mapIndex = mapIndex;
        _mobSkillDb = mobSkillDb;
        _skillDb = skillDb;
        _mobDb = mobDb;
        Id = mob.Id;
        _name = mob.Name ?? "";
        _aegisName = mob.AegisName ?? "";
        _level = mob.Level;
        _hp = mob.Hp;
        _baseExp = mob.BaseExp;
        _jobExp = mob.JobExp;
        _attack = mob.Attack;
        _attack2 = mob.Attack2;
        _defense = mob.Defense;
        _magicDefense = mob.MagicDefense;
        _str = mob.Str;
        _agi = mob.Agi;
        _vit = mob.Vit;
        _int = mob.Int;
        _dex = mob.Dex;
        _luk = mob.Luk;
        _race = mob.Race ?? "Formless";
        _element = mob.Element ?? "Neutral";
        _elementLevel = mob.ElementLevel;
        _size = mob.Size ?? "Medium";
        _walkSpeed = mob.WalkSpeed;
        _attackDelay = mob.AttackDelay;
        _attackRange = mob.AttackRange;
        _skillRange = mob.SkillRange;
        _chaseRange = mob.ChaseRange;

        foreach (var d in mob.Drops)
            Drops.Add(new MobDropEntry { Item = d.Item, Rate = d.Rate, StealProtected = d.StealProtected, Index = d.Index });
        foreach (var d in mob.MvpDrops)
            MvpDrops.Add(new MobDropEntry { Item = d.Item, Rate = d.Rate, StealProtected = d.StealProtected, Index = d.Index });

        BuildModes(mob);
        RefreshElementRows();
        RebuildSpawnGroups(spawnParser, mob.Id);
        RebuildMonsterSkillsAndSlaves();
    }

    private void RebuildMonsterSkillsAndSlaves()
    {
        // Monster Skills and Slaves are now populated by MainWindow.PopulateMonsterSkillsAndSlaves
        // via MobSkillPanelService and the DataGrids (MonsterSkillsGrid, MonsterSlavesGrid).
        MonsterSkills.Clear();
        Slaves.Clear();
    }

    private void BuildModes(MobEntry mob)
    {
        var knownModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Boss", "CanMove", "Looter", "Aggressive", "Assist", "CastSensorIdle", "CastSensorChase",
            "ChangeChase", "ChangeTargetMelee", "ChangeTargetChase", "NoCast", "NoRandomWalk",
            "Detector", "StatusImmune", "SkillImmune", "Mvp"
        };
        if (mob.Modes != null)
        {
            foreach (var k in mob.Modes.Keys)
                knownModes.Add(k);
        }
        foreach (var m in knownModes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            Modes.Add(new ModeToggleRow(m, mob.Modes != null && mob.Modes.TryGetValue(m, out var v) && v));
    }

    private void RefreshElementRows()
    {
        ElementRows.Clear();
        if (_attrFix != null)
        {
            foreach (var elem in AttrFixTableService.Elements)
            {
                var pct = _attrFix.GetDamagePercent(elem, _element, _elementLevel);
                ElementRows.Add(new ElementModifierRow { ElementName = elem, Percent = pct });
            }
        }
    }

    private void RebuildSpawnGroups(SpawnParser? spawnParser, int mobId)
    {
        SpawnGroups.Clear();
        var spawns = spawnParser?.GetSpawnsForMob(mobId).ToList() ?? new List<SpawnEntry>();
        foreach (var g in spawns.GroupBy(s => s.Map))
        {
            var total = g.Sum(s => s.Amount);
            var firstWithDelay = g.FirstOrDefault(s => s.DelayMs.HasValue && s.DelayMs.Value > 0);
            var delayMs = firstWithDelay?.DelayMs;
            var varianceMs = firstWithDelay?.VarianceMs;
            var displayMap = _mapIndex?.GetDisplayName(g.Key) ?? g.Key;
            SpawnGroups.Add(new SpawnGroupViewModel
            {
                MapName = displayMap,
                AreaName = "",
                Count = total,
                DelayMs = delayMs,
                VarianceMs = varianceMs
            });
        }
    }

    public void PushTo(MobEntry mob)
    {
        mob.Name = _name;
        mob.AegisName = _aegisName;
        mob.Level = _level;
        mob.Hp = _hp;
        mob.BaseExp = _baseExp;
        mob.JobExp = _jobExp;
        mob.Attack = _attack;
        mob.Attack2 = _attack2;
        mob.Defense = _defense;
        mob.MagicDefense = _magicDefense;
        mob.Str = _str;
        mob.Agi = _agi;
        mob.Vit = _vit;
        mob.Int = _int;
        mob.Dex = _dex;
        mob.Luk = _luk;
        mob.Race = _race;
        mob.Element = _element;
        mob.ElementLevel = _elementLevel;
        mob.Size = _size;
        mob.WalkSpeed = _walkSpeed;
        mob.AttackDelay = _attackDelay;
        mob.AttackRange = _attackRange;
        mob.SkillRange = _skillRange;
        mob.ChaseRange = _chaseRange;

        mob.Drops.Clear();
        foreach (var d in Drops)
            mob.Drops.Add(new MobDropEntry { Item = d.Item, Rate = d.Rate, StealProtected = d.StealProtected, Index = d.Index });
        mob.MvpDrops.Clear();
        foreach (var d in MvpDrops)
            mob.MvpDrops.Add(new MobDropEntry { Item = d.Item, Rate = d.Rate, StealProtected = d.StealProtected, Index = d.Index });

        mob.Modes.Clear();
        foreach (var row in Modes.Where(r => r.IsEnabled))
            mob.Modes[row.Name] = true;
    }
}
