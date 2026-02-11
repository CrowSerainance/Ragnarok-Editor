using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using RoDbEditor.Models;

namespace RoDbEditor.Dialogs;

public partial class MobSlaveEditDialog : Window
{
    private static readonly string[] States =
    {
        "any", "idle", "walk", "dead", "loot", "attack",
        "angry", "chase", "follow", "anytarget"
    };

    private sealed class ConditionOption
    {
        public string Key { get; }
        public string Label { get; }

        public ConditionOption(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public override string ToString() => $"{Label} ({Key})";
    }

    private sealed class MobOption
    {
        public int Id { get; }
        public string Name { get; }

        public MobOption(int id, string name)
        {
            Id = id;
            Name = name;
        }
        public override string ToString() => $"{Id} - {Name}";
    }

    private static readonly ConditionOption[] ConditionTypes =
    {
        new("slavele", "When slave count is less than or equal"),
        new("slavelt", "When slave count is less than"),
        new("always", "Unconditional"),
        new("onspawn", "On spawn/respawn"),
        new("myhpltmaxrate", "When self HP drops below percent"),
        new("myhpinrate", "When self HP is within percent range"),
        new("attackpcgt", "When number of attacking players is greater than"),
        new("attackpcge", "When number of attacking players is greater than or equal"),
        new("masterhpltmaxrate", "When master HP drops below percent"),
        new("masterattacked", "When master is attacked")
    };

    private readonly List<MobOption> _mobOptions = new();

    public MobSkillDbRow? Result { get; private set; }
    public int MobId { get; set; }
    public string MobName { get; set; } = "";

    public MobSlaveEditDialog()
    {
        InitializeComponent();

        CmbState.ItemsSource = States;
        CmbConditionType.ItemsSource = ConditionTypes;

        LoadMobOptions();
        BindMobSources();

        CmbState.SelectedItem = "idle";
        CmbConditionType.SelectedItem = ConditionTypes.FirstOrDefault(c => c.Key == "slavele") 
                                        ?? ConditionTypes.First();
    }

    private void LoadMobOptions()
    {
        _mobOptions.Clear();

        var mobs = App.MobDbService?.Mobs;
        if (mobs == null) return;
        foreach (var m in mobs.OrderBy(x => x.Id))
        {
            var name = string.IsNullOrWhiteSpace(m.Name) ? $"Mob#{m.Id}" : m.Name;
            _mobOptions.Add(new MobOption(m.Id, name));
        }
    }

    private void BindMobSources()
    {
        ApplyMobFilter("");
    }

    private void CmbSlave_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox combo)
            ApplyMobFilterForCombo(combo);
    }

    private void ApplyMobFilterForCombo(System.Windows.Controls.ComboBox combo)
    {
        var text = (combo.Text ?? "").Trim();
        combo.ItemsSource = FilterMobOptions(text);
    }

    private void ApplyMobFilter(string filter)
    {
        var filtered = FilterMobOptions(filter);
        CmbSlave1.ItemsSource = filtered;
        CmbSlave2.ItemsSource = filtered;
        CmbSlave3.ItemsSource = filtered;
        CmbSlave4.ItemsSource = filtered;
        CmbSlave5.ItemsSource = filtered;
    }

    private IList<MobOption> FilterMobOptions(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return _mobOptions;

        var f = filter.Trim();
        return _mobOptions
            .Where(m => m.Id.ToString().Contains(f, StringComparison.OrdinalIgnoreCase) ||
                        (m.Name?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    public void SetMobContext(int mobId, string mobName)
    {
        MobId = mobId;
        MobName = mobName;
        TxtMobId.Text = mobId.ToString(CultureInfo.InvariantCulture);
        LblMobName.Text = mobName;
    }

    public void LoadFromRow(MobSkillDbRow row)
    {
        MobId = row.MobId;
        TxtMobId.Text = row.MobId.ToString(CultureInfo.InvariantCulture);

        SetSlaveComboById(CmbSlave1, row.Val1);
        SetSlaveComboById(CmbSlave2, row.Val2);
        SetSlaveComboById(CmbSlave3, row.Val3);
        SetSlaveComboById(CmbSlave4, row.Val4);
        SetSlaveComboById(CmbSlave5, row.Val5);

        TxtTotalCount.Text = row.SkillLv.ToString(CultureInfo.InvariantCulture);
        CmbState.Text = row.State;
        TxtRate.Text = row.Rate.ToString(CultureInfo.InvariantCulture);

        var cond = ConditionTypes.FirstOrDefault(c =>
            c.Key.Equals(row.ConditionType, StringComparison.OrdinalIgnoreCase));
        if (cond != null)
            CmbConditionType.SelectedItem = cond;
        else
            CmbConditionType.Text = row.ConditionType;

        TxtConditionValue.Text = row.ConditionValue.ToString(CultureInfo.InvariantCulture);
    }

    private void SetSlaveComboById(System.Windows.Controls.ComboBox combo, int id)
    {
        if (id <= 0)
        {
            combo.SelectedItem = null;
            combo.Text = "";
            return;
        }
        var found = _mobOptions.FirstOrDefault(m => m.Id == id);
        if (found != null)
            combo.SelectedItem = found;
        else
            combo.Text = id.ToString(CultureInfo.InvariantCulture);
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        var val1 = GetSlaveId(CmbSlave1);
        var val2 = GetSlaveId(CmbSlave2);
        var val3 = GetSlaveId(CmbSlave3);
        var val4 = GetSlaveId(CmbSlave4);
        var val5 = GetSlaveId(CmbSlave5);

        if (val1 <= 0 && val2 <= 0 && val3 <= 0 && val4 <= 0 && val5 <= 0)
        {
            System.Windows.MessageBox.Show("Select at least one Slave Mob ID.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var mobNameForDummy = !string.IsNullOrEmpty(MobName) ? MobName : $"Mob{MobId}";

        Result = new MobSkillDbRow
        {
            MobId = MobId,
            Dummy = $"{mobNameForDummy}@NPC_SUMMONSLAVE",
            SkillId = 196,
            SkillLv = ParseInt(TxtTotalCount.Text, 1),
            State = (CmbState.Text ?? "idle").Trim(),
            Rate = ParseInt(TxtRate.Text, 10000),
            CastTimeMs = 0,
            DelayMs = 0,
            Cancelable = 0,
            Target = "self",
            ConditionType = GetConditionTypeKey(),
            ConditionValue = ParseInt(TxtConditionValue.Text, 0),
            Val1 = val1,
            Val2 = val2,
            Val3 = val3,
            Val4 = val4,
            Val5 = val5,
            Emotion = 0,
            Chat = ""
        };

        DialogResult = true;
        Close();
    }

    private int GetSlaveId(System.Windows.Controls.ComboBox combo)
    {
        if (combo.SelectedItem is MobOption selected)
            return selected.Id;

        var text = (combo.Text ?? "").Trim();
        if (text.Length == 0) return 0;

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
            return id;

        // Try extracting ID from "123 - Name" format if user typed it manually
        var dash = text.IndexOf('-');
        if (dash > 0 &&
            int.TryParse(text.Substring(0, dash).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) &&
            id > 0)
        {
            return id;
        }

        // Try binding by name lookup
        var byName = _mobOptions.FirstOrDefault(m => m.Name.Equals(text, StringComparison.OrdinalIgnoreCase));
        return byName?.Id ?? 0;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static int ParseInt(string? text, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(text)) return defaultValue;
        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : defaultValue;
    }

    private string GetConditionTypeKey()
    {
        if (CmbConditionType.SelectedItem is ConditionOption selected)
            return selected.Key;

        var text = (CmbConditionType.Text ?? "slavele").Trim();
        if (text.Length == 0) return "slavele";

        var open = text.LastIndexOf('(');
        var close = text.LastIndexOf(')');
        if (open >= 0 && close > open + 1)
            return text.Substring(open + 1, close - open - 1).Trim();

        return text;
    }
}
