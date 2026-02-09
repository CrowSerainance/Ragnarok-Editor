namespace RoDbEditor.ViewModels;

public sealed class ModeToggleRow : ObservableObject
{
    public ModeToggleRow(string name, bool enabled)
    {
        Name = name;
        _isEnabled = enabled;
    }

    public string Name { get; }

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set => Set(ref _isEnabled, value);
    }
}
