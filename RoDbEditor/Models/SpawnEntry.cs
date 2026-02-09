namespace RoDbEditor.Models;

public class SpawnEntry
{
    public string Map { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int MobId { get; set; }
    public int Amount { get; set; } = 1;
    public int? DelayMs { get; set; }
    public int? VarianceMs { get; set; }

    public string DisplayText
    {
        get
        {
            var baseStr = Amount > 1 ? $"{Map} ({X}, {Y}) {Amount}x" : $"{Map} ({X}, {Y})";
            if (DelayMs.HasValue && DelayMs.Value > 0)
            {
                var sec = DelayMs.Value / 1000;
                baseStr += $" / {sec}s";
                if (VarianceMs.HasValue && VarianceMs.Value > 0)
                {
                    var vSec = VarianceMs.Value / 1000;
                    baseStr += $" ± {vSec}s";
                }
            }
            return baseStr;
        }
    }
}
