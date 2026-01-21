using System.Windows;
using System.Windows.Controls;
using ROMapOverlayEditor; // Needed for Placable types

namespace ROMapOverlayEditor.UserControls
{
    public partial class InspectorView : UserControl
    {
        public event RoutedEventHandler? FocusRequested;
        public event RoutedEventHandler? DeleteRequested;

        public string[] CommonSprites { get; } = new[]
        {
            "4_M_01", "4_F_01", "4_M_02", "4_F_02", // Kafra / Guides
            "4_M_KAFRA", "4_F_KAFRA",
            "4_M_SOLDIER", "4_F_SOLDIER",
            "1_M_01", "1_F_01", // Novice
            "4_M_MERCHANT", "4_F_MERCHANT",
            "2_M_SWORDMAN", "2_F_SWORDMAN",
            "111", "112", "113", "114" // Classic IDs
        };

        public InspectorView()
        {
            InitializeComponent();
            DataContextChanged += InspectorView_DataContextChanged;
            UpdateVisibility();
            SpriteCombo.ItemsSource = CommonSprites;
        }

        private void InspectorView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            if (NpcPanel == null || WarpPanel == null || SpawnPanel == null) return;
            
            NpcPanel.Visibility = Visibility.Collapsed;
            WarpPanel.Visibility = Visibility.Collapsed;
            SpawnPanel.Visibility = Visibility.Collapsed;

            if (DataContext is NpcPlacable)
            {
                NpcPanel.Visibility = Visibility.Visible;
            }
            else if (DataContext is WarpPlacable)
            {
                WarpPanel.Visibility = Visibility.Visible;
            }
            else if (DataContext is SpawnPlacable)
            {
                SpawnPanel.Visibility = Visibility.Visible;
            }
        }

        private void Focus_Click(object sender, RoutedEventArgs e)
        {
            FocusRequested?.Invoke(this, e);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            DeleteRequested?.Invoke(this, e);
        }
    }
}
