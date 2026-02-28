using System.ComponentModel;

namespace RoDbEditor.Models;

/// <summary>
/// One row in the Batch Import table. Maps to ItemEntry for persistence (server YAML, client Lua, accessory, assets).
/// </summary>
public class BatchItemRow : INotifyPropertyChanged
{
    private string _spriteName = "";
    private string _iconName = "";
    private string _displayName = "";
    private string _type = "Equip";
    private int _slots = 0;
    private string _position = "Head_Top";
    private int _viewId;
    private int _itemId;
    private string _script = "";
    private string _description = "";
    private bool _costume;
    private string _importFileName = "";

    /// <summary>Sprite asset name (e.g. .spr/.act base name).</summary>
    public string SpriteName { get => _spriteName; set { _spriteName = value ?? ""; OnPropertyChanged(nameof(SpriteName)); } }
    /// <summary>Icon asset name (e.g. .bmp filename).</summary>
    public string IconName { get => _iconName; set { _iconName = value ?? ""; OnPropertyChanged(nameof(IconName)); } }
    /// <summary>Display name (item Name).</summary>
    public string DisplayName { get => _displayName; set { _displayName = value ?? ""; OnPropertyChanged(nameof(DisplayName)); } }
    /// <summary>Item type (Equip, Etc, Usable, etc.).</summary>
    public string Type { get => _type; set { _type = value ?? "Etc"; OnPropertyChanged(nameof(Type)); } }
    public int Slots { get => _slots; set { _slots = value; OnPropertyChanged(nameof(Slots)); } }
    /// <summary>Equip position (Head_Top, etc.).</summary>
    public string Position { get => _position; set { _position = value ?? ""; OnPropertyChanged(nameof(Position)); } }
    /// <summary>Headgear view ID (0 = assign on Generate).</summary>
    public int ViewId { get => _viewId; set { _viewId = value; OnPropertyChanged(nameof(ViewId)); } }
    /// <summary>Item ID (0 = assign on Generate).</summary>
    public int ItemId { get => _itemId; set { _itemId = value; OnPropertyChanged(nameof(ItemId)); } }
    public string Script { get => _script; set { _script = value ?? ""; OnPropertyChanged(nameof(Script)); } }
    public string Description { get => _description; set { _description = value ?? ""; OnPropertyChanged(nameof(Description)); } }
    public bool Costume { get => _costume; set { _costume = value; OnPropertyChanged(nameof(Costume)); } }
    /// <summary>Original filename from import (for duplicate skip).</summary>
    public string ImportFileName { get => _importFileName; set { _importFileName = value ?? ""; OnPropertyChanged(nameof(ImportFileName)); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Build an ItemEntry for saving (caller sets Id/View if 0).</summary>
    public ItemEntry ToItemEntry(int assignedItemId, int assignedViewId)
    {
        var id = ItemId > 0 ? ItemId : assignedItemId;
        var view = ViewId > 0 ? ViewId : assignedViewId;
        var name = string.IsNullOrWhiteSpace(DisplayName) ? (ImportFileName ?? "Custom_" + id) : DisplayName.Trim();
        var aegis = "Custom_" + name.Replace(" ", "_");
        return new ItemEntry
        {
            Id = id,
            AegisName = aegis,
            Name = name,
            Type = Type,
            Slots = Slots > 0 ? Slots : null,
            View = view > 0 ? view : null,
            ResourceName = SpriteName ?? aegis,
            Description = Description,
            Script = string.IsNullOrWhiteSpace(Script) ? null : Script,
        };
    }

    /// <summary>Create from existing ItemEntry.</summary>
    public static BatchItemRow FromItemEntry(ItemEntry e)
    {
        return new BatchItemRow
        {
            ItemId = e.Id,
            ViewId = e.View ?? 0,
            DisplayName = e.Name,
            Type = e.Type ?? "Etc",
            Slots = e.Slots ?? 0,
            SpriteName = e.ResourceName ?? e.AegisName ?? "",
            IconName = "",
            Script = e.Script ?? "",
            Description = e.Description ?? "",
            ImportFileName = ""
        };
    }
}
