using System.Windows;
using System.Windows.Controls;
using ROMapOverlayEditor;

namespace ROMapOverlayEditor.UserControls
{
    public partial class ProjectView : UserControl
    {
        public event RoutedEventHandler? ExportNpcsRequested;
        public event RoutedEventHandler? ExportWarpsRequested;
        public event RoutedEventHandler? ExportAllRequested;
        public event RoutedEventHandler? ValidateRequested;

        public ProjectView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) => UpdateSnippet();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e) => UpdateSnippet();

        private void UpdateSnippet()
        {
            if (IncludeSnippetText == null) return;
            var p = DataContext as ProjectData;
            var dir = (p != null && !string.IsNullOrEmpty(p.ExportScriptsPath)) ? p.ExportScriptsPath.TrimEnd('\\', '/') : "scripts";
            IncludeSnippetText.Text = "import \"" + dir + "/npcs_custom.txt\";\nimport \"" + dir + "/warps_custom.txt\";";
        }

        private void CopySnippet_Click(object sender, RoutedEventArgs e)
        {
            UpdateSnippet();
            try { Clipboard.SetText(IncludeSnippetText?.Text ?? ""); } catch { }
        }

        private void ExportNpcs_Click(object sender, RoutedEventArgs e) => ExportNpcsRequested?.Invoke(this, e);
        private void ExportWarps_Click(object sender, RoutedEventArgs e) => ExportWarpsRequested?.Invoke(this, e);
        private void ExportAll_Click(object sender, RoutedEventArgs e) => ExportAllRequested?.Invoke(this, e);
        private void Validate_Click(object sender, RoutedEventArgs e) => ValidateRequested?.Invoke(this, e);

        public void SetValidationMessage(string msg)
        {
            ValidationOutput.Text = msg;
        }
    }
}
