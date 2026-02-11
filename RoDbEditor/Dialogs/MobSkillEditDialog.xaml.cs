using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RoDbEditor.Models;

namespace RoDbEditor.Dialogs;

/// <summary>
/// Modal dialog for adding or editing a monster skill entry.
/// Returns a MobSkillDbRow via the Result property when DialogResult is true.
/// </summary>
public partial class MobSkillEditDialog : Window
{
    private static readonly string[] States =
    {
        "any", "idle", "walk", "dead", "loot", "attack",
        "angry", "chase", "follow", "anytarget"
    };

    private static readonly string[] Targets =
    {
        "target", "self", "friend", "master", "randomtarget",
        "around1", "around2", "around3", "around4",
        "around5", "around6", "around7", "around8", "around"
    };

    private static readonly string[] ConditionTypes =
    {
        "always", "onspawn", "myhpltmaxrate", "myhpinrate",
        "mystatuson", "mystatusoff", "friendhpltmaxrate", "friendhpinrate",
        "slavelt", "slavele", "closedattacked", "longrangeattacked",
        "skillused", "afterskill", "casttargeted", "rudeattacked", "masterhpltmaxrate", "masterattacked",
        "mobnearbygt", "groundattacked", "damagedgt", "alchemist", "trickcasting"
    };

    /// <summary>The resulting MobSkillDbRow after user clicks OK.</summary>
    public MobSkillDbRow? Result { get; private set; }

    /// <summary>Mob ID for this entry (set before showing).</summary>
    public int MobId { get; set; }

    /// <summary>Mob name for display.</summary>
    public string MobName { get; set; } = "";

    // Skill dropdown data
    private sealed class SkillDropdownItem
    {
        public int Id { get; set; }
        public string Display { get; set; } = "";
        public override string ToString() => Display;
    }

    private List<SkillDropdownItem> _allSkillItems = new();
    private bool _isUpdatingSkillCombo;

    public MobSkillEditDialog()
    {
        InitializeComponent();

        CmbState.ItemsSource = States;
        CmbTarget.ItemsSource = Targets;
        CmbConditionType.ItemsSource = ConditionTypes;

        // Defaults
        CmbState.SelectedItem = "attack";
        CmbTarget.SelectedItem = "target";
        CmbConditionType.SelectedItem = "always";

        // Populate skill dropdown
        PopulateSkillDropdown();
    }

    private void PopulateSkillDropdown()
    {
        if (App.SkillDbMiniService == null) return;

        var all = App.SkillDbMiniService.GetAll();
        _allSkillItems = all
            .OrderBy(kv => kv.Key)
            .Select(kv => new SkillDropdownItem
            {
                Id = kv.Key,
                Display = $"{kv.Key} - {kv.Value.Name}" +
                          (!string.IsNullOrWhiteSpace(kv.Value.Description) ? $" ({kv.Value.Description})" : "")
            })
            .ToList();

        CmbSkillId.ItemsSource = _allSkillItems;
    }

    /// <summary>Pre-fill the dialog with an existing row for editing.</summary>
    public void LoadFromRow(MobSkillDbRow row)
    {
        MobId = row.MobId;
        TxtMobId.Text = row.MobId.ToString(CultureInfo.InvariantCulture);

        // Set skill via text (works for both known and unknown skill IDs)
        _isUpdatingSkillCombo = true;
        CmbSkillId.Text = row.SkillId.ToString(CultureInfo.InvariantCulture);
        _isUpdatingSkillCombo = false;
        UpdateSkillDisplay(row.SkillId);

        TxtSkillLv.Text = row.SkillLv.ToString(CultureInfo.InvariantCulture);
        CmbState.Text = row.State;
        TxtRate.Text = row.Rate.ToString(CultureInfo.InvariantCulture);
        TxtCastTime.Text = row.CastTimeMs.ToString(CultureInfo.InvariantCulture);
        TxtDelay.Text = row.DelayMs.ToString(CultureInfo.InvariantCulture);
        ChkCancelable.IsChecked = row.Cancelable != 0;
        CmbTarget.Text = row.Target;
        CmbConditionType.Text = row.ConditionType;
        TxtConditionValue.Text = row.ConditionValue.ToString(CultureInfo.InvariantCulture);
        TxtVal1.Text = row.Val1.ToString(CultureInfo.InvariantCulture);
        TxtVal2.Text = row.Val2.ToString(CultureInfo.InvariantCulture);
        TxtVal3.Text = row.Val3.ToString(CultureInfo.InvariantCulture);
        TxtVal4.Text = row.Val4.ToString(CultureInfo.InvariantCulture);
        TxtVal5.Text = row.Val5.ToString(CultureInfo.InvariantCulture);
        TxtEmotion.Text = row.Emotion.ToString(CultureInfo.InvariantCulture);
        TxtChat.Text = row.Chat;
    }

    /// <summary>Set the mob context (ID + name) for a new entry.</summary>
    public void SetMobContext(int mobId, string mobName)
    {
        MobId = mobId;
        MobName = mobName;
        TxtMobId.Text = mobId.ToString(CultureInfo.InvariantCulture);
        LblMobName.Text = mobName;
    }

    // ── Skill ComboBox events ──────────────────────────────────────

    private void CmbSkillId_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSkillCombo) return;

        if (CmbSkillId.SelectedItem is SkillDropdownItem item)
        {
            _isUpdatingSkillCombo = true;
            CmbSkillId.Text = item.Id.ToString(CultureInfo.InvariantCulture);
            _isUpdatingSkillCombo = false;
            UpdateSkillDisplay(item.Id);
        }
    }

    /// <summary>
    /// Called on PreviewKeyUp so we can filter the dropdown as the user types,
    /// without fighting WPF's built-in text search.
    /// </summary>
    protected override void OnPreviewKeyUp(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);

        // Only react when the ComboBox edit textbox has focus
        if (!CmbSkillId.IsKeyboardFocusWithin) return;
        if (e.Key == Key.Enter || e.Key == Key.Escape || e.Key == Key.Tab) return;
        if (e.Key == Key.Up || e.Key == Key.Down) return;

        var text = CmbSkillId.Text?.Trim() ?? "";

        // If purely numeric, try to resolve skill and filter by ID prefix
        if (int.TryParse(text, out var numId))
        {
            UpdateSkillDisplay(numId);
            _isUpdatingSkillCombo = true;
            CmbSkillId.ItemsSource = _allSkillItems
                .Where(s => s.Id.ToString().StartsWith(text, StringComparison.Ordinal))
                .Take(50)
                .ToList();
            CmbSkillId.IsDropDownOpen = true;
            _isUpdatingSkillCombo = false;
        }
        else if (text.Length >= 2)
        {
            // Text search by name
            _isUpdatingSkillCombo = true;
            CmbSkillId.ItemsSource = _allSkillItems
                .Where(s => s.Display.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Take(50)
                .ToList();
            CmbSkillId.IsDropDownOpen = true;
            _isUpdatingSkillCombo = false;

            LblSkillName.Text = "";
            LblValHint.Text = "";
        }
        else if (text.Length == 0)
        {
            _isUpdatingSkillCombo = true;
            CmbSkillId.ItemsSource = _allSkillItems;
            _isUpdatingSkillCombo = false;
            LblSkillName.Text = "";
            LblValHint.Text = "";
        }
    }

    private void UpdateSkillDisplay(int skillId)
    {
        if (App.SkillDbMiniService != null)
            LblSkillName.Text = App.SkillDbMiniService.ResolveDisplayName(skillId);
        else
            LblSkillName.Text = "";

        LblValHint.Text = GetValHint(skillId);
    }

    private static string GetValHint(int skillId) => skillId switch
    {
        196 => "Val1-Val5 = Slave Mob IDs to summon  (use 'Edit Slave' dialog for easier editing)",
        197 => "Val1 = Emotion ID (e.g. 0=Normal, 1=!, 2=?, 28=Lv.99 Aura, 29=Casting)",
        198 or 681 => "Val1 = Transform into Mob ID",
        352 => "NPC_CALLSLAVE: recalls existing slaves (no Val needed)",
        _ => "Skill-specific parameters (usually 0)"
    };

    // ── OK / Cancel ────────────────────────────────────────────────

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        // Resolve skill ID from ComboBox: either from selection or from typed text
        int skillId;
        if (CmbSkillId.SelectedItem is SkillDropdownItem selected)
        {
            skillId = selected.Id;
        }
        else if (!int.TryParse(CmbSkillId.Text?.Trim(), out skillId) || skillId <= 0)
        {
            System.Windows.MessageBox.Show("Please enter a valid Skill ID.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Build the Dummy field: MobName@SkillAegisName
        var skillAegis = App.SkillDbMiniService?.ResolveName(skillId) ?? $"Skill#{skillId}";
        var mobNameForDummy = !string.IsNullOrEmpty(MobName) ? MobName : $"Mob{MobId}";
        var dummy = $"{mobNameForDummy}@{skillAegis}";

        Result = new MobSkillDbRow
        {
            MobId = MobId,
            Dummy = dummy,
            SkillId = skillId,
            SkillLv = ParseInt(TxtSkillLv.Text, 1),
            State = (CmbState.Text ?? "attack").Trim(),
            Rate = ParseInt(TxtRate.Text, 10000),
            CastTimeMs = ParseInt(TxtCastTime.Text, 0),
            DelayMs = ParseInt(TxtDelay.Text, 0),
            Cancelable = ChkCancelable.IsChecked == true ? 1 : 0,
            Target = (CmbTarget.Text ?? "target").Trim(),
            ConditionType = (CmbConditionType.Text ?? "always").Trim(),
            ConditionValue = ParseInt(TxtConditionValue.Text, 0),
            Val1 = ParseInt(TxtVal1.Text, 0),
            Val2 = ParseInt(TxtVal2.Text, 0),
            Val3 = ParseInt(TxtVal3.Text, 0),
            Val4 = ParseInt(TxtVal4.Text, 0),
            Val5 = ParseInt(TxtVal5.Text, 0),
            Emotion = ParseInt(TxtEmotion.Text, 0),
            Chat = TxtChat.Text?.Trim() ?? ""
        };

        DialogResult = true;
        Close();
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
}
