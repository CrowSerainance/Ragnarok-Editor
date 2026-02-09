namespace RoDbEditor.ViewModels;

public sealed class MobSlaveRow
{
    public int Amount { get; }
    public int SlaveMobId { get; }
    public string SlaveMobName { get; }

    public int Rate { get; }
    public string ConditionType { get; }
    public string ConditionValue { get; }

    public string WhenText =>
        string.IsNullOrWhiteSpace(ConditionType) ? "" :
        string.IsNullOrWhiteSpace(ConditionValue) ? ConditionType :
        $"{ConditionType} {ConditionValue}";

    public string DisplayText => $"{Amount} {SlaveMobName}";

    public MobSlaveRow(int amount, int slaveMobId, string slaveMobName, int rate, string conditionType, string conditionValue)
    {
        Amount = amount;
        SlaveMobId = slaveMobId;
        SlaveMobName = slaveMobName;
        Rate = rate;
        ConditionType = conditionType ?? "";
        ConditionValue = conditionValue ?? "";
    }
}
