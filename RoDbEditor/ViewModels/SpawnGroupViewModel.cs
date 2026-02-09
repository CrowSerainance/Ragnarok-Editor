namespace RoDbEditor.ViewModels;

public class SpawnGroupViewModel : ObservableObject
{
    private string _mapName = "";
    private string _areaName = "";
    private int _count;
    private int? _delayMs;
    private int? _varianceMs;

    public string MapName { get => _mapName; set { if (Set(ref _mapName, value ?? "")) Raise(nameof(DisplayText)); } }
    public string AreaName { get => _areaName; set { if (Set(ref _areaName, value ?? "")) Raise(nameof(DisplayText)); } }
    public int Count { get => _count; set { if (Set(ref _count, value)) Raise(nameof(DisplayText)); } }
    public int? DelayMs { get => _delayMs; set { Set(ref _delayMs, value); Raise(nameof(DisplayText)); } }
    public int? VarianceMs { get => _varianceMs; set { Set(ref _varianceMs, value); Raise(nameof(DisplayText)); } }

    public string DisplayText
    {
        get
        {
            var baseStr = string.IsNullOrEmpty(AreaName)
                ? $"{MapName} + {Count}x"
                : $"{MapName} - {AreaName} + {Count}x";
            return baseStr + FormatDelay();
        }
    }

    private string FormatDelay()
    {
        if (!DelayMs.HasValue || DelayMs.Value <= 0) return "";
        var sec = DelayMs.Value / 1000;
        var result = $" / {sec}s";
        if (VarianceMs.HasValue && VarianceMs.Value > 0)
        {
            var vSec = VarianceMs.Value / 1000;
            result += $" ± {vSec}s";
        }
        return result;
    }
}
