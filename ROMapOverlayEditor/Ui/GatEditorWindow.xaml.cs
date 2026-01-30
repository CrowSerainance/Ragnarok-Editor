using System;
using System.Windows;
using ROMapOverlayEditor.Patching;
using ROMapOverlayEditor.Vfs;

namespace ROMapOverlayEditor.Ui
{
    public partial class GatEditorWindow : Window
    {
        public GatEditorWindow()
        {
            InitializeComponent();
        }

        public void Initialize(CompositeVfs vfs, EditStaging staging, Func<string?> browseGrf)
        {
            GatView.Initialize(vfs, staging, browseGrf);
        }

        public void LoadMap(string mapName)
        {
            GatView.LoadMap(mapName);
        }
    }
}
