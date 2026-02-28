using System.ComponentModel;

namespace RoDbEditor.Models;

/// <summary>One entry in an item package (random box): ItemId, Amount, Weight. Weights typically sum to 10000 (100%).</summary>
public class PackageEntry : INotifyPropertyChanged
{
    private int _itemId;
    private int _amount = 1;
    private int _weight;

    public int ItemId { get => _itemId; set { _itemId = value; OnPropertyChanged(nameof(ItemId)); } }
    public int Amount { get => _amount; set { _amount = value; OnPropertyChanged(nameof(Amount)); } }
    public int Weight { get => _weight; set { _weight = value; OnPropertyChanged(nameof(Weight)); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
