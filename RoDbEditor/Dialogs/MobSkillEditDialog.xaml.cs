using System;
using System.Globalization;
using System.Windows;
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
        "friendstatuson", "friendstatusoff", "attackpcgt", "attackpcge",
        "slavelt", "slavele", "closedattacked", "longrangeattacked",
        "skillused", "afterskill", "casttargeted", "rudeattacked",
        "mobnearbygt", "groundattacked", "damagedgt", "alchemist", "trickcasting"
    };

    /// <summary>
    /// The resulting MobSkillDbRow after user clicks OK.
    /// </summary>
    public MobSkillDbRow? Result { get; private set; }

    /// <summary>
    /// Mob ID for this entry (set before showing).
    /// </summary>
    public int MobId { get; set; }

    /// <summary>
    /// Mob name for display.
    /// </summary>
    public string MobName { get; set; } = "";

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
    }

    /// <summary>
    /// Pre-fill the dialog with an existing row for editing.
    /// </summary>
    public void LoadFromRow(MobSkillDbRow row)
    {
        MobId = row.MobId;
        TxtMobId.Text = row.MobId.ToString(CultureInfo.InvariantCulture);
        TxtSkillId.Text = row.SkillId.ToString(CultureInfo.InvariantCulture);
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

    /// <summary>
    /// Set the mob context (ID + name) for a new entry.
    /// </summary>
    public void SetMobContext(int mobId, string mobName)
    {
        MobId = mobId;
        MobName = mobName;
        TxtMobId.Text = mobId.ToString(CultureInfo.InvariantCulture);
        LblMobName.Text = mobName;
    }

    private void TxtSkillId_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (int.TryParse(TxtSkillId.Text.Trim(), out var skillId) && App.SkillDbMiniService != null)
        {
            LblSkillName.Text = App.SkillDbMiniService.ResolveDisplayName(skillId);
        }
        else
        {
            LblSkillName.Text = "";
        }
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtSkillId.Text.Trim(), out var skillId) || skillId <= 0)
        {
            System.Windows.MessageBox.Show("Please enter a valid Skill ID.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
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
