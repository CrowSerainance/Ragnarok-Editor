using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ROMapOverlayEditor; // Needed for MobEntry

namespace ROMapOverlayEditor.UserControls
{
    public partial class DatabaseView : UserControl
    {
        public event RoutedEventHandler? SpawnRequested;

        private List<MobEntry> _mobs;

        public DatabaseView()
        {
            InitializeComponent();
            LoadStubData();
        }

        private void LoadStubData()
        {
            _mobs = new List<MobEntry>
            {
                new MobEntry { Id = 1002, Name = "Poring", Level = 1, Hp = 50, Race = "Plant", Element = "Water 1" },
                new MobEntry { Id = 1007, Name = "Fabre", Level = 2, Hp = 63, Race = "Insect", Element = "Earth 1" },
                new MobEntry { Id = 1115, Name = "Eddga", Level = 65, Hp = 152000, Race = "Brute", Element = "Fire 1", Size = "Large", BaseExp=50000 },
                new MobEntry { Id = 1511, Name = "Amon Ra", Level = 99, Hp = 1200000, Race = "Demi-Human", Element = "Earth 3", Size = "Large", IsMvp=true }
            };
            
            ResultsList.ItemsSource = _mobs;
        }

        private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultsList.SelectedItem is MobEntry mob)
            {
                DetailsPanel.DataContext = mob;
                DetailsPanel.Visibility = Visibility.Visible;
            }
            else
            {
                DetailsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void PlaceButton_Click(object sender, RoutedEventArgs e)
        {
             SpawnRequested?.Invoke(this, e);
        }
    }
}
