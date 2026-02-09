namespace RoDbEditor.ViewModels;

public class ElementModifierRow : ObservableObject
{
    private int _percent = 100;

    public string ElementName { get; set; } = "";
    public int Percent { get => _percent; set => Set(ref _percent, value); }
    public bool IsResistant => Percent < 100;
    public bool IsWeak => Percent > 100;
}
