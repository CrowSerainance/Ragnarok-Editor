using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace ROMapOverlayEditor;

/// <summary>Dialog to pick a map from Towninfo.lub to import NPCs.</summary>
public partial class MapSelectionDialog : Window
{
    private readonly List<string> _allMaps;
    public string? SelectedMap { get; private set; }

    public MapSelectionDialog(List<string> availableMaps, string? currentMap = null)
    {
        InitializeComponent();
        _allMaps = availableMaps.OrderBy(m => m).ToList();
        MapListBox.ItemsSource = _allMaps;

        if (!string.IsNullOrEmpty(currentMap))
        {
            var match = _allMaps.FirstOrDefault(m => m.Equals(currentMap, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                MapListBox.SelectedItem = match;
                MapListBox.ScrollIntoView(match);
            }
        }

        Loaded += (_, _) => SearchBox.Focus();
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        string filter = SearchBox.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(filter))
            MapListBox.ItemsSource = _allMaps;
        else
        {
            var filtered = _allMaps.Where(m => m.ToLowerInvariant().Contains(filter)).ToList();
            MapListBox.ItemsSource = filtered;
            if (filtered.Count > 0) MapListBox.SelectedIndex = 0;
        }
    }

    private void MapListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MapListBox.SelectedItem != null) ConfirmSelection();
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (MapListBox.SelectedItem == null)
        {
            MessageBox.Show("Please select a map to import.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ConfirmSelection();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ConfirmSelection()
    {
        SelectedMap = MapListBox.SelectedItem as string;
        DialogResult = true;
        Close();
    }
}
