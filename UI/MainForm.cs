using SilverpineSilcEditor.Core;

namespace SilverpineSilcEditor.UI;

public sealed class MainForm : Form
{
    private readonly TextBox _folderPath;
    private readonly TextBox _search;
    private readonly ComboBox _typeFilter;
    private readonly Label _libraryCount;
    private readonly ListView _definitionList;
    private readonly Panel _editorHost;
    private readonly Label _documentTitle;
    private readonly Label _documentMeta;
    private readonly Label _status;
    private readonly Button _save;
    private readonly Button _revert;
    private readonly Button _backupAll;
    private readonly CheckBox _autoBackup;
    private readonly List<DefinitionListItem> _allDefinitions = [];
    private string? _definitionsDirectory;
    private SilcDocument? _document;
    private Action _applyEditor = () => { };
    private bool _dirty;
    private bool _changingSelection;
    private int _loadGeneration;

    public MainForm(string? initialPath)
    {
        Text = "Silverpine SILC Editor";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 680);
        ClientSize = new Size(1480, 900);
        Font = Theme.BodyFont;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Icon = SystemIcons.Application;

        var top = new Panel { Dock = DockStyle.Top, Height = 100, BackColor = Theme.Surface, Padding = new Padding(20, 14, 20, 12) };
        var title = new Label
        {
            Text = "SILVERPINE  /  SILC STUDIO",
            Font = new Font("Segoe UI Semibold", 17f),
            ForeColor = Theme.Accent,
            AutoSize = true,
            Location = new Point(16, 10)
        };
        var folderLabel = new Label { Text = "Folder", AutoSize = true, ForeColor = Theme.Muted, Location = new Point(20, 67) };
        _folderPath = new TextBox { ReadOnly = true, Location = new Point(82, 62), Height = 28, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        _folderPath.Width = top.Width - 315;
        var browse = Theme.Button("Choose folder", (_, _) => ChooseFolder());
        browse.Location = new Point(top.Width - 194, 55);
        browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        var refresh = Theme.Button("Refresh", (_, _) => RefreshDefinitions());
        refresh.Location = new Point(top.Width - 94, 55);
        refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _backupAll = Theme.Button("Back up all now", (_, _) => BackupAllDefinitions());
        _backupAll.Enabled = false;
        _backupAll.Location = new Point(top.Width - 140, 8);
        _autoBackup = new CheckBox
        {
            Text = "Back up before every save",
            Checked = true,
            AutoSize = true,
            ForeColor = Theme.Text,
            Location = new Point(top.Width - 355, 13)
        };
        top.Controls.AddRange([title, folderLabel, _folderPath, browse, refresh, _backupAll, _autoBackup]);
        top.Resize += (_, _) =>
        {
            refresh.Left = top.ClientSize.Width - refresh.Width - 16;
            browse.Left = refresh.Left - browse.Width - 8;
            _folderPath.Width = Math.Max(160, browse.Left - _folderPath.Left - 10);
            _backupAll.Left = top.ClientSize.Width - _backupAll.Width - 16;
            _autoBackup.Left = _backupAll.Left - _autoBackup.Width - 14;
        };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 350,
            FixedPanel = FixedPanel.Panel1,
            BackColor = Theme.Border,
            SplitterWidth = 1
        };
        Theme.InitializeSplitter(split, 350);

        var left = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(16) };
        var definitionsLabel = new Label
        {
            Text = "DEFINITION LIBRARY",
            Font = new Font("Segoe UI Semibold", 10.5f),
            ForeColor = Theme.Muted,
            Dock = DockStyle.Top,
            Height = 30
        };
        _search = new TextBox { PlaceholderText = "Search definitions…", Dock = DockStyle.Top, Height = 32 };
        _search.TextChanged += (_, _) => ApplyFilter();
        _typeFilter = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        _typeFilter.Items.AddRange(["All definitions", "Characters", "NPCs", "Models"]);
        _typeFilter.SelectedIndex = 0;
        _typeFilter.SelectedIndexChanged += (_, _) => ApplyFilter();
        _libraryCount = new Label { Dock = DockStyle.Bottom, Height = 44, ForeColor = Theme.Muted, Padding = new Padding(0, 12, 0, 0) };
        var spacer = new Panel { Dock = DockStyle.Top, Height = 9 };
        _definitionList = new DefinitionLibraryView();
        _definitionList.SelectedIndexChanged += DefinitionSelectionChanged;
        left.Controls.Add(_definitionList);
        left.Controls.Add(_libraryCount);
        left.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12 });
        left.Controls.Add(_typeFilter);
        left.Controls.Add(spacer);
        left.Controls.Add(_search);
        left.Controls.Add(definitionsLabel);
        split.Panel1.Controls.Add(left);

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        var documentHeader = new Panel { Dock = DockStyle.Top, Height = 90, BackColor = Theme.Background, Padding = new Padding(22, 14, 20, 12) };
        _documentTitle = new Label
        {
            Text = "No definition selected",
            Font = Theme.HeadingFont,
            ForeColor = Theme.Text,
            AutoEllipsis = true,
            Location = new Point(18, 10),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Width = documentHeader.Width - 260,
            Height = 30
        };
        _documentMeta = new Label
        {
            Text = "Choose a Definitions folder to begin.",
            ForeColor = Theme.Muted,
            Location = new Point(20, 44),
            AutoEllipsis = true,
            Height = 25,
            Width = 500
        };
        _save = Theme.Button("Save changes", (_, _) => SaveCurrent());
        _save.Tag = "primary";
        _save.Enabled = false;
        _save.Location = new Point(documentHeader.Width - 178, 21);
        _save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _revert = Theme.Button("Revert", (_, _) => RevertCurrent());
        _revert.Enabled = false;
        _revert.Location = new Point(documentHeader.Width - 94, 21);
        _revert.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        documentHeader.Controls.AddRange([_documentTitle, _documentMeta, _save, _revert]);
        documentHeader.Resize += (_, _) =>
        {
            _revert.Left = documentHeader.ClientSize.Width - _revert.Width - 14;
            _save.Left = _revert.Left - _save.Width - 8;
            _documentTitle.Width = Math.Max(180, _save.Left - _documentTitle.Left - 12);
            _documentMeta.Width = Math.Max(180, _save.Left - _documentMeta.Left - 12);
        };

        _editorHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        right.Controls.Add(_editorHost);
        right.Controls.Add(documentHeader);
        split.Panel2.Controls.Add(right);

        _status = new Label
        {
            Text = "Ready",
            Dock = DockStyle.Bottom,
            Height = 30,
            BackColor = Theme.Surface,
            ForeColor = Theme.Muted,
            Padding = new Padding(14, 6, 8, 4)
        };

        Controls.Add(split);
        Controls.Add(_status);
        Controls.Add(top);
        Theme.Style(this);
        Shown += (_, _) => InitializeDirectory(initialPath);
        FormClosing += OnFormClosing;
        ShowWelcome();
    }

    private void InitializeDirectory(string? initialPath)
    {
        string? directory = DefinitionDirectoryLocator.Find(initialPath);
        if (directory is not null)
            SetDirectory(directory);
        else
            SetStatus("Definitions folder not found automatically. Select it with Choose folder.", false);
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose Silverpine's Definitions folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            InitialDirectory = _definitionsDirectory ?? ""
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            SetDirectory(dialog.SelectedPath);
    }

    private void SetDirectory(string directory)
    {
        if (!CanAbandonCurrent())
            return;
        _definitionsDirectory = Path.GetFullPath(directory);
        _folderPath.Text = _definitionsDirectory;
        _backupAll.Enabled = true;
        DefinitionDirectoryLocator.Save(_definitionsDirectory);
        ClearDocument();
        RefreshDefinitions();
    }

    private void RefreshDefinitions()
    {
        if (_definitionsDirectory is null || !Directory.Exists(_definitionsDirectory))
        {
            SetStatus("Choose an existing Definitions folder first.", true);
            return;
        }

        string? selectedPath = (_definitionList.SelectedItems.Count > 0
            ? _definitionList.SelectedItems[0].Tag as DefinitionListItem
            : null)?.Path;
        _allDefinitions.Clear();
        try
        {
            foreach (string path in Directory.EnumerateFiles(_definitionsDirectory, "*.silc", SearchOption.TopDirectoryOnly))
                _allDefinitions.Add(new DefinitionListItem(path));
            _allDefinitions.Sort(DefinitionListItem.Compare);
            ApplyFilter(selectedPath);
            SetStatus($"Found {_allDefinitions.Count:N0} SILC definition{(_allDefinitions.Count == 1 ? "" : "s")}.", false);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not read the folder: {exception.Message}", true);
        }
    }

    private void ApplyFilter(string? restorePath = null)
    {
        string query = _search.Text.Trim();
        string? currentPath = restorePath ?? (_definitionList.SelectedItems.Count > 0
            ? (_definitionList.SelectedItems[0].Tag as DefinitionListItem)?.Path
            : null);

        _changingSelection = true;
        _definitionList.BeginUpdate();
        _definitionList.Items.Clear();
        string? prefix = _typeFilter.SelectedIndex switch { 1 => "PlayerCharacter", 2 => "NPC", 3 => "ModelDefinition", _ => null };
        foreach (DefinitionListItem item in _allDefinitions.Where(item => item.Matches(query) && (prefix is null || item.Prefix == prefix)))
        {
            var row = new ListViewItem(item.Name) { Tag = item };
            if (string.Equals(item.Path, currentPath, StringComparison.OrdinalIgnoreCase))
                row.Selected = true;
            _definitionList.Items.Add(row);
        }
        _definitionList.EndUpdate();
        _libraryCount.Text = $"{_definitionList.Items.Count} of {_allDefinitions.Count} definitions  ·  Ctrl+F to search";
        _changingSelection = false;
    }

    private async void DefinitionSelectionChanged(object? sender, EventArgs eventArgs)
    {
        if (_changingSelection || _definitionList.SelectedItems.Count == 0)
            return;
        var item = (DefinitionListItem)_definitionList.SelectedItems[0].Tag!;
        if (_document is not null && string.Equals(_document.FilePath, item.Path, StringComparison.OrdinalIgnoreCase))
            return;
        if (!CanAbandonCurrent())
        {
            RestoreCurrentSelection();
            return;
        }
        await LoadDefinitionAsync(item.Path);
    }

    private async Task LoadDefinitionAsync(string path)
    {
        int generation = ++_loadGeneration;
        _document = null;
        _applyEditor = () => { };
        _dirty = false;
        _save.Enabled = false;
        _revert.Enabled = false;
        _definitionList.Enabled = false;
        Theme.DisposeChildren(_editorHost);
        _editorHost.Controls.Add(BusyPanel("Reading and validating SILC…"));
        SetStatus($"Loading {Path.GetFileName(path)}…", false);
        try
        {
            SilcDocument document = await Task.Run(() => SilcDocument.Load(path));
            if (IsDisposed || Disposing || generation != _loadGeneration)
                return;
            _document = document;
            _dirty = false;
            string[] availableNpcNames = _allDefinitions
                .Where(item => string.Equals(item.Prefix, "NPC", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Name)
                .Concat(NpcDefinitionModel.BaseGameNpcNames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] availablePlayerNames = _allDefinitions
                .Where(item => string.Equals(item.Prefix, "PlayerCharacter", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            EditorSurface editor = DefinitionEditors.Build(document, MarkDirty, availableNpcNames, availablePlayerNames);
            _applyEditor = editor.Apply;
            Theme.DisposeChildren(_editorHost);
            editor.Control.Dock = DockStyle.Fill;
            _editorHost.Controls.Add(editor.Control);
            Theme.Style(editor.Control);
            UpdateDocumentHeader();
            _save.Enabled = true;
            _revert.Enabled = true;
            string note = document.UsedCompressionFallback ? " (layout detected by fallback)" : "";
            SetStatus($"Loaded {Path.GetFileName(path)}{note}.", false);
        }
        catch (Exception exception)
        {
            if (IsDisposed || Disposing || generation != _loadGeneration)
                return;
            ClearDocument();
            ShowErrorPanel($"Could not load {Path.GetFileName(path)}", exception);
            SetStatus(exception.Message, true);
        }
        finally
        {
            if (!IsDisposed && !Disposing && generation == _loadGeneration)
                _definitionList.Enabled = true;
        }
    }

    private bool SaveCurrent()
    {
        if (_document is null)
            return true;
        try
        {
            _applyEditor();
            string? backup = _document.Save(_autoBackup.Checked);
            _dirty = false;
            UpdateDocumentHeader();
            SetStatus(backup is null
                ? "Saved and validated. Automatic backup was disabled."
                : $"Saved and validated. Backup: {backup}", false);
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "SILC could not be saved", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus($"Save failed: {exception.Message}", true);
            return false;
        }
    }

    private async void RevertCurrent()
    {
        if (_document is null)
            return;
        if (_dirty && MessageBox.Show(this, "Discard all unsaved edits to this definition?", "Revert definition",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        await LoadDefinitionAsync(_document.FilePath);
    }

    private bool CanAbandonCurrent()
    {
        if (!_dirty)
            return true;
        DialogResult result = MessageBox.Show(this,
            "Save changes to the current SILC before continuing?",
            "Unsaved SILC changes",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Warning);
        return result switch
        {
            DialogResult.Yes => SaveCurrent(),
            DialogResult.No => true,
            _ => false
        };
    }

    private void RestoreCurrentSelection()
    {
        if (_document is null)
            return;
        _changingSelection = true;
        foreach (ListViewItem row in _definitionList.Items)
            row.Selected = row.Tag is DefinitionListItem item && string.Equals(item.Path, _document.FilePath, StringComparison.OrdinalIgnoreCase);
        _changingSelection = false;
    }

    private void MarkDirty()
    {
        if (_document is null)
            return;
        _dirty = true;
        UpdateDocumentHeader();
    }

    private void UpdateDocumentHeader()
    {
        if (_document is null)
            return;
        _documentTitle.Text = (_dirty ? "● " : "") + _document.Name;
        _documentTitle.ForeColor = _dirty ? Theme.Accent : Theme.Text;
        string layout = _document.IsLegacy ? "legacy raw" : "Brotli";
        _documentMeta.Text = $"{(_dirty ? "Unsaved changes" : "Saved on disk")}  ·  {KindLabel(_document.Kind)}  ·  {_document.Version}  ·  {layout}";
    }

    private void ClearDocument()
    {
        _loadGeneration++;
        _definitionList.Enabled = true;
        _document = null;
        _dirty = false;
        _applyEditor = () => { };
        _save.Enabled = false;
        _revert.Enabled = false;
        _documentTitle.Text = "No definition selected";
        _documentTitle.ForeColor = Theme.Text;
        _documentMeta.Text = "Select a SILC from the list.";
        ShowWelcome();
    }

    private void ShowWelcome()
    {
        Theme.DisposeChildren(_editorHost);
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(55) };
        panel.Controls.Add(new Label
        {
            Text = "Choose a definition from the searchable list.\r\n\r\nThe editor creates a timestamped backup before every write and validates the complete SILC before replacing the original.",
            Dock = DockStyle.Top,
            Height = 150,
            ForeColor = Theme.Muted,
            Font = new Font("Segoe UI", 11f)
        });
        _editorHost.Controls.Add(panel);
    }

    private void ShowErrorPanel(string title, Exception exception)
    {
        Theme.DisposeChildren(_editorHost);
        _editorHost.Controls.Add(new Label
        {
            Text = $"{title}\r\n\r\n{exception.Message}",
            Dock = DockStyle.Fill,
            Padding = new Padding(35),
            ForeColor = Theme.Danger,
            Font = new Font("Segoe UI", 11f)
        });
    }

    private static Control BusyPanel(string text) => new Label
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Theme.Muted,
        BackColor = Theme.Background,
        Font = new Font("Segoe UI", 11f)
    };

    private void SetStatus(string text, bool error)
    {
        _status.Text = text;
        _status.ForeColor = error ? Theme.Danger : Theme.Muted;
    }

    private void BackupAllDefinitions()
    {
        if (_definitionsDirectory is null || !Directory.Exists(_definitionsDirectory))
        {
            SetStatus("Choose an existing Definitions folder first.", true);
            return;
        }
        try
        {
            string[] sources = Directory.GetFiles(_definitionsDirectory, "*.silc", SearchOption.TopDirectoryOnly);
            string destination = Path.Combine(
                _definitionsDirectory,
                "SILC Editor Backups",
                "Full backups",
                DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(destination);
            foreach (string source in sources)
                File.Copy(source, Path.Combine(destination, Path.GetFileName(source)), overwrite: false);
            SetStatus($"Backed up {sources.Length:N0} SILCs to {destination}", false);
            MessageBox.Show(this,
                $"Backed up {sources.Length:N0} SILC files.\r\n\r\n{destination}",
                "Full SILC backup complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            SetStatus($"Backup failed: {exception.Message}", true);
            MessageBox.Show(this, exception.Message, "Full SILC backup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!CanAbandonCurrent())
            eventArgs.Cancel = true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S)) { if (_save.Enabled) SaveCurrent(); return true; }
        if (keyData == (Keys.Control | Keys.F)) { _search.Focus(); _search.SelectAll(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _loadGeneration++;
            _document = null;
            _applyEditor = () => { };
        }
        base.Dispose(disposing);
    }

    private static string KindLabel(SilcDefinitionKind kind) => kind switch
    {
        SilcDefinitionKind.Npc => "NPC",
        SilcDefinitionKind.PlayerCharacter => "Player character",
        SilcDefinitionKind.ModelDefinition => "Model definition",
        _ => "Unknown definition"
    };
}

internal sealed class DefinitionListItem
{
    internal DefinitionListItem(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        string stem = System.IO.Path.GetFileNameWithoutExtension(path);
        int separator = stem.IndexOf('_');
        Prefix = separator >= 0 ? stem[..separator] : "Unknown";
        Name = separator >= 0 && separator + 1 < stem.Length ? stem[(separator + 1)..] : stem;
        TypeLabel = Prefix switch
        {
            "NPC" => "NPC",
            "PlayerCharacter" => "Player",
            "ModelDefinition" => "Model",
            _ => Prefix
        };
    }

    internal string Path { get; }
    internal string Prefix { get; }
    internal string Name { get; }
    internal string TypeLabel { get; }

    internal bool Matches(string query) => query.Length == 0
        || Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || TypeLabel.Contains(query, StringComparison.OrdinalIgnoreCase)
        || System.IO.Path.GetFileName(Path).Contains(query, StringComparison.OrdinalIgnoreCase);

    internal static int Compare(DefinitionListItem left, DefinitionListItem right)
    {
        int type = string.Compare(left.TypeLabel, right.TypeLabel, StringComparison.OrdinalIgnoreCase);
        return type != 0 ? type : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class DefinitionDirectoryLocator
{
    private static readonly string SettingsDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Silverpine SILC Editor");
    private static readonly string SettingsPath = System.IO.Path.Combine(SettingsDirectory, "definitions-folder.txt");

    internal static string? Find(string? initialPath)
    {
        foreach (string? candidate in new[] { Normalize(initialPath), ReadSaved(), Normalize(Environment.CurrentDirectory) })
        {
            string? definitions = AsDefinitionsDirectory(candidate);
            if (definitions is not null)
                return definitions;
        }

        string localLow = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "..", "LocalLow"));
        try
        {
            foreach (string company in Directory.EnumerateDirectories(localLow))
            {
                foreach (string product in Directory.EnumerateDirectories(company))
                {
                    if (!System.IO.Path.GetFileName(product).Contains("Silverpine", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string definitions = System.IO.Path.Combine(product, "Definitions");
                    if (Directory.Exists(definitions))
                        return definitions;
                }
            }
        }
        catch
        {
            // Folder selection remains available when automatic discovery is blocked.
        }
        return null;
    }

    internal static void Save(string directory)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, directory);
        }
        catch
        {
            // Remembering the folder is convenient but not required for editing.
        }
    }

    private static string? ReadSaved()
    {
        try
        {
            return File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        try
        {
            string full = System.IO.Path.GetFullPath(value);
            return File.Exists(full) ? System.IO.Path.GetDirectoryName(full) : full;
        }
        catch
        {
            return null;
        }
    }

    private static string? AsDefinitionsDirectory(string? candidate)
    {
        if (candidate is null || !Directory.Exists(candidate))
            return null;
        if (string.Equals(System.IO.Path.GetFileName(candidate), "Definitions", StringComparison.OrdinalIgnoreCase))
            return candidate;
        try
        {
            if (Directory.EnumerateFiles(candidate, "*.silc", SearchOption.TopDirectoryOnly).Any())
                return candidate;
        }
        catch
        {
            return null;
        }
        string child = System.IO.Path.Combine(candidate, "Definitions");
        return Directory.Exists(child) ? child : null;
    }
}
