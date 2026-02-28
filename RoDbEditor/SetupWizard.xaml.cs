using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoDbEditor.Config;
// Disambiguate from System.Windows.Forms
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;

namespace RoDbEditor;

public partial class SetupWizard : Window
{
    private int _currentStep;
    private const int TotalSteps = 6;

    private string? _dataPath;
    private string? _clientRootPath;
    private string? _clientPatchRoot;
    private ClientWriteMode _clientWriteMode = ClientWriteMode.PatchOnly;
    private string? _targetGrfPath;
    private string _targetGrfFileName = "custom.grf";

    public bool CompletedSuccessfully { get; private set; }

    public SetupWizard()
    {
        InitializeComponent();
        LoadFromConfig();
        _currentStep = 1;
        BuildStepContent();
        UpdateButtons();
    }

    private void LoadFromConfig()
    {
        var config = RoDbEditorConfig.Load();
        _dataPath = config.DataPath;
        _clientRootPath = config.ClientRootPath;
        _clientPatchRoot = config.ClientPatchRoot;
        _clientWriteMode = config.ClientWriteMode;
        _targetGrfPath = config.TargetGrfPath;
        _targetGrfFileName = config.TargetGrfFileName ?? "custom.grf";
    }

    private void BuildStepContent()
    {
        StepContent.Children.Clear();
        StepTitle.Text = _currentStep switch
        {
            1 => "Step 1: Server root (rAthena)",
            2 => "Step 2: Client root",
            3 => "Step 3: Patch output (optional)",
            4 => "Step 4: Write mode",
            5 => "Step 5: Target GRF (optional)",
            6 => "Step 6: Finalize",
            _ => "Setup"
        };

        switch (_currentStep)
        {
            case 1:
                BuildStep1();
                break;
            case 2:
                BuildStep2();
                break;
            case 3:
                BuildStep3();
                break;
            case 4:
                BuildStep4();
                break;
            case 5:
                BuildStep5();
                break;
            case 6:
                BuildStep6();
                break;
        }
    }

    private void BuildStep1()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Select your rAthena (or compatible) server root. RoDbEditor will read from db/re or db/pre-re and write only to db/import.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);

        var status1 = new TextBlock
        {
            Text = string.IsNullOrEmpty(_dataPath) ? "" : ValidateDataPath(_dataPath),
            Foreground = new SolidColorBrush(Colors.Gray),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };
        Grid.SetRow(status1, 2);
        grid.Children.Add(status1);

        var pathPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var dataPathBox = new TextBox
        {
            Text = _dataPath ?? "",
            IsReadOnly = true,
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 320
        };
        pathPanel.Children.Add(dataPathBox);
        var browseBtn = new Button { Content = "Browse...", Padding = new Thickness(12, 4, 12, 4) };
        browseBtn.Click += (_, _) =>
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select rAthena server root (folder containing db, npc, etc.)",
                UseDescriptionForTitle = true
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _dataPath = dlg.SelectedPath;
                dataPathBox.Text = _dataPath;
                status1.Text = ValidateDataPath(_dataPath);
            }
        };
        pathPanel.Children.Add(browseBtn);
        Grid.SetRow(pathPanel, 1);
        grid.Children.Add(pathPanel);

        StepContent.Children.Add(grid);
    }

    private static string ValidateDataPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return "Folder not found.";
        var dbRe = Path.Combine(path, "db", "re");
        var dbPreRe = Path.Combine(path, "db", "pre-re");
        var dbImport = Path.Combine(path, "db", "import");
        var hasDb = Directory.Exists(dbRe) || Directory.Exists(dbPreRe) || Directory.Exists(Path.Combine(path, "db"));
        if (!hasDb)
            return "No db folder found; is this the server root?";
        var importExists = Directory.Exists(dbImport);
        return importExists ? "OK. db/import exists (writes will go here)." : "OK. db/import will be created when saving.";
    }

    private void BuildStep2()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Select your Ragnarok client installation root (contains System, data.grf, etc.). Used to read GRFs and, in Live Client mode, to write overlays.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);

        var status2 = new TextBlock
        {
            Text = string.IsNullOrEmpty(_clientRootPath) ? "" : ValidateClientPath(_clientRootPath),
            Foreground = new SolidColorBrush(Colors.Gray),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };
        Grid.SetRow(status2, 2);
        grid.Children.Add(status2);

        var pathPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var clientBox = new TextBox
        {
            Text = _clientRootPath ?? "",
            IsReadOnly = true,
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 320
        };
        pathPanel.Children.Add(clientBox);
        var browseBtn = new Button { Content = "Browse...", Padding = new Thickness(12, 4, 12, 4) };
        browseBtn.Click += (_, _) =>
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select client root (folder with System, *.grf)",
                UseDescriptionForTitle = true
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _clientRootPath = dlg.SelectedPath;
                clientBox.Text = _clientRootPath;
                status2.Text = ValidateClientPath(_clientRootPath);
            }
        };
        pathPanel.Children.Add(browseBtn);
        Grid.SetRow(pathPanel, 1);
        grid.Children.Add(pathPanel);

        StepContent.Children.Add(grid);
    }

    private static string ValidateClientPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return "Folder not found.";
        var system = Path.Combine(path, "System");
        var hasSystem = Directory.Exists(system);
        var grfs = Directory.GetFiles(path, "*.grf");
        return hasSystem && grfs.Length > 0
            ? $"OK. System + {grfs.Length} GRF(s) found."
            : hasSystem ? "OK. System found." : "No System folder; some features may not work.";
    }

    private void BuildStep3()
    {
        var label = new TextBlock
        {
            Text = "Optional: folder where RoDbEditor will write overlay files (itemInfo_rodbeditor.lua, etc.). If left blank, Patch-only mode will still write to the configured custom GRF; you can set this later in FILE ASSIGNMENT.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        StepContent.Children.Add(label);

        var pathPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var patchBox = new TextBox
        {
            Text = _clientPatchRoot ?? "",
            IsReadOnly = true,
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 320
        };
        pathPanel.Children.Add(patchBox);
        var browseBtn = new Button { Content = "Browse...", Padding = new Thickness(12, 4, 12, 4) };
        browseBtn.Click += (_, _) =>
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select patch output root",
                UseDescriptionForTitle = true
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _clientPatchRoot = dlg.SelectedPath;
                patchBox.Text = _clientPatchRoot;
            }
        };
        pathPanel.Children.Add(browseBtn);
        StepContent.Children.Add(pathPanel);
    }

    private void BuildStep4()
    {
        var label = new TextBlock
        {
            Text = "Patch-only (recommended): RoDbEditor does not modify files directly in your client folder; it writes overlay files and GRFs. Live client: RoDbEditor may write itemInfo_C.lua and placeholder icons into the client folder (convenient for single-user testing).",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        StepContent.Children.Add(label);

        var patchRadio = new RadioButton
        {
            Content = "Patch-only (safe; overlay files and custom GRF only)",
            IsChecked = _clientWriteMode == ClientWriteMode.PatchOnly,
            Margin = new Thickness(0, 4, 0, 0)
        };
        patchRadio.Checked += (_, _) => _clientWriteMode = ClientWriteMode.PatchOnly;
        StepContent.Children.Add(patchRadio);

        var liveRadio = new RadioButton
        {
            Content = "Live client (allow direct edits to client System and icons)",
            IsChecked = _clientWriteMode == ClientWriteMode.LiveClient,
            Margin = new Thickness(0, 4, 0, 0)
        };
        liveRadio.Checked += (_, _) => _clientWriteMode = ClientWriteMode.LiveClient;
        StepContent.Children.Add(liveRadio);
    }

    private void BuildStep5()
    {
        var label = new TextBlock
        {
            Text = "Optional: full path to your custom GRF (e.g. custom.grf). If blank, RoDbEditor uses ClientRoot + custom.grf.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        StepContent.Children.Add(label);

        var pathPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var grfBox = new TextBox
        {
            Text = _targetGrfPath ?? "",
            IsReadOnly = true,
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 280
        };
        pathPanel.Children.Add(grfBox);
        var browseBtn = new Button { Content = "Browse...", Padding = new Thickness(12, 4, 12, 4) };
        browseBtn.Click += (_, _) =>
        {
            var initial = !string.IsNullOrEmpty(_targetGrfPath) && File.Exists(_targetGrfPath)
                ? Path.GetDirectoryName(_targetGrfPath)
                : _clientRootPath;
            using var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Select custom GRF file",
                Filter = "GRF files (*.grf)|*.grf|All files|*.*",
                FileName = "custom.grf",
                InitialDirectory = initial ?? Environment.GetFolderPath(Environment.SpecialFolder.MyComputer)
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _targetGrfPath = dlg.FileName;
                grfBox.Text = _targetGrfPath;
            }
        };
        pathPanel.Children.Add(browseBtn);
        StepContent.Children.Add(pathPanel);

        var testBtn = new Button
        {
            Content = "Test paths",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        testBtn.Click += (_, _) =>
        {
            var msg = TestPaths();
            System.Windows.MessageBox.Show(this, msg, "Test paths", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        StepContent.Children.Add(testBtn);
    }

    private string TestPaths()
    {
        var lines = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(_dataPath))
        {
            var importDir = Path.Combine(_dataPath, "db", "import");
            var canWrite = Directory.Exists(_dataPath) && (Directory.Exists(importDir) || Directory.Exists(Path.Combine(_dataPath, "db")));
            lines.Add($"Server: {_dataPath} — {(canWrite ? "OK (read + db/import write)" : "Missing or invalid")}");
        }
        else
            lines.Add("Server: not set");

        if (!string.IsNullOrEmpty(_clientRootPath))
            lines.Add($"Client: {_clientRootPath} — {(Directory.Exists(_clientRootPath) ? "OK" : "Not found")}");
        else
            lines.Add("Client: not set");

        if (!string.IsNullOrEmpty(_clientPatchRoot))
            lines.Add($"Patch root: {_clientPatchRoot} — {(Directory.Exists(_clientPatchRoot) ? "OK" : "Will be created on first write")}");
        else
            lines.Add("Patch root: not set (optional)");

        var grfPath = ResolveTargetGrfPath();
        if (!string.IsNullOrEmpty(grfPath))
            lines.Add($"Target GRF: {grfPath} — {(File.Exists(grfPath) ? "Exists" : "Will be created on first write")}");
        return string.Join(Environment.NewLine, lines);
    }

    private string? ResolveTargetGrfPath()
    {
        if (!string.IsNullOrEmpty(_targetGrfPath))
            return _targetGrfPath;
        if (string.IsNullOrEmpty(_clientRootPath))
            return null;
        return Path.Combine(_clientRootPath, _targetGrfFileName);
    }

    private void BuildStep6()
    {
        var summary = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        summary.Text = $"Server: {_dataPath ?? "(not set)"}\nClient: {_clientRootPath ?? "(not set)"}\nPatch output: {_clientPatchRoot ?? "(not set)"}\nMode: {_clientWriteMode}\nTarget GRF: {ResolveTargetGrfPath() ?? "(default)"}";
        StepContent.Children.Add(summary);

        var note = new TextBlock
        {
            Text = "Click Finish to save and open RoDbEditor. You can run this wizard again from Help → Setup Wizard.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Colors.Gray)
        };
        StepContent.Children.Add(note);
    }

    private void UpdateButtons()
    {
        BackButton.IsEnabled = _currentStep > 1;
        NextButton.Visibility = _currentStep < TotalSteps ? Visibility.Visible : Visibility.Collapsed;
        FinishButton.Visibility = _currentStep == TotalSteps ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep <= 1) return;
        _currentStep--;
        BuildStepContent();
        UpdateButtons();
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep >= TotalSteps) return;
        _currentStep++;
        BuildStepContent();
        UpdateButtons();
    }

    private void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        var config = App.Config ?? RoDbEditorConfig.Load();
        var profile = config.GetActiveProfile();
        if (profile == null)
        {
            config.EnsureDefaultProfileFromLegacyFields();
            profile = config.GetActiveProfile();
        }

        if (profile != null)
        {
            profile.DataPath = _dataPath;
            profile.ClientRootPath = _clientRootPath;
            profile.ClientPatchRoot = _clientPatchRoot;
            profile.ClientWriteMode = _clientWriteMode;
            profile.TargetGrfPath = _targetGrfPath;
            profile.TargetGrfFileName = _targetGrfFileName;
        }

        config.DataPath = _dataPath;
        config.ClientRootPath = _clientRootPath;
        config.ClientPatchRoot = _clientPatchRoot;
        config.ClientWriteMode = _clientWriteMode;
        config.TargetGrfPath = _targetGrfPath;
        config.TargetGrfFileName = _targetGrfFileName;
        config.HasCompletedWizard = true;

        if (config.GrfPaths.Count == 0 && !string.IsNullOrEmpty(config.ClientRootPath) && Directory.Exists(config.ClientRootPath))
        {
            foreach (var grf in new[] { "custom.grf", "en.grf", "official_data.grf", "data.grf" })
            {
                var full = Path.Combine(config.ClientRootPath, grf);
                if (File.Exists(full))
                    config.GrfPaths.Add(full);
            }
            if (profile != null)
            {
                profile.GrfPaths.Clear();
                foreach (var p in config.GrfPaths)
                    profile.GrfPaths.Add(p);
            }
        }

        config.Save();
        CompletedSuccessfully = true;
        DialogResult = true;
        Close();
    }
}
