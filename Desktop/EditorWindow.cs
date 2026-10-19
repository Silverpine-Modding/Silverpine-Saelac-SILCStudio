using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using SilverpineSilcEditor.Core;
using System.Security.Cryptography;

namespace SilverpineSilcEditor.Desktop;

internal sealed class EditorWindow : Window
{
    protected override Type StyleKeyOverride => typeof(Window);
    private readonly ListBox _library = new();
    private readonly TextBox _search = new() { Watermark = "Search definitions…" };
    private readonly ComboBox _filter = new() { ItemsSource = new[] { "All definitions", "Characters", "NPCs", "Models" }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly TextBlock _count = Ui.Note("Choose a Definitions folder"), _title = Ui.Text("Your character workshop", 25), _meta = Ui.Note("Select a SILC to begin."), _status = Ui.Note("Nothing is changed on disk until you save."), _folderLabel = Ui.Note("No Definitions folder selected");
    private readonly CheckBox _backup = new() { Content = "Back up before saving", IsChecked = true };
    private readonly ContentControl _host = new();
    private readonly Grid _workspace = new();
    private readonly Control _editorBody;
    private RoutineMapEditor? _mapEditor;
    private TaskCompletionSource<bool>? _mapCompletion;
    private readonly Button _save;
    private List<LibraryItem> _items = [];
    private string? _folder;
    private SilcDocument? _document;
    private DefinitionView? _view;
    private byte[]? _diskHash;
    private bool _dirty, _busy, _restoring, _closeApproved;
    internal EditorWindow(string? initialPath = null)
    {
        Title = "Silverpine · SILC Studio"; Width = 1450; Height = 920; MinWidth = 1060; MinHeight = 660;
        Background = Ui.Background; FontSize = 14; Ui.CurrentWindow = this;
        _save = Ui.AsyncButton("Save changes", SaveClick, true); _save.IsEnabled = false;
        _backup.IsChecked = EditorPaths.Read("automatic-backup") != "false";
        _backup.IsCheckedChanged += (_, _) => { try { EditorPaths.Write("automatic-backup", _backup.IsChecked == true ? "true" : "false"); } catch (Exception e) { _status.Text = e.Message; } };
        var top = Ui.Card(Ui.Dock(Ui.Row(Ui.Text("SILVERPINE / SILC STUDIO", 20, Ui.Accent), Ui.Note("Native desktop · 0.2.1")),
            Ui.Row(Ui.AsyncButton("Definitions folder…", PickDefinitions), Ui.AsyncButton("Game folder…", PickGame), Ui.AsyncButton("Back up all SILCs", BackupAll), _backup)), 16);
        _folderLabel.TextTrimming = TextTrimming.CharacterEllipsis; _folderLabel.MaxLines = 1;
        var sidebarHead = Ui.Stack(Ui.Text("LIBRARY", 13, Ui.Accent), _search, _filter, _count); sidebarHead.Margin = new Thickness(12, 18);
        _library.ItemTemplate = new FuncDataTemplate<LibraryItem>((item, _) => Ui.Stack(Ui.Text(item?.Name ?? "", 15), Ui.Note(item?.Kind ?? "")));
        _library.SelectionChanged += async (_, _) =>
        {
            if (_restoring || _busy || _library.SelectedItem is not LibraryItem item || item.Path == _document?.FilePath) return;
            if (await CanLeave()) await LoadFile(item.Path); else RestoreSelection();
        };
        _search.TextChanged += (_, _) => Filter(); _filter.SelectionChanged += (_, _) => Filter();
        var sidebar = new Border { Background = Ui.Surface, Child = Ui.Dock(sidebarHead, _library) };
        var header = Ui.Dock(Ui.Row(_title), Ui.Row(_meta, _save, Ui.AsyncButton("Reload from disk", Reload))); header.Margin = new Thickness(24, 22, 24, 10);
        _host.Content = Ui.Card(Ui.Stack(Ui.Text("Make a character feel at home.", 28, Ui.Accent), Ui.Note("Choose your Definitions folder, then select a character, NPC, or model from the library."), Ui.Note("Backups are enabled by default. Game and save files are only read for the routine map."))); _host.Margin = new Thickness(12, 0, 12, 12);
        var main = Ui.Split(sidebar, Ui.Dock(header, _host), 265);
        _status.Margin = new Thickness(16, 8); _status.MaxLines = 2;
        _editorBody = Ui.Dock(Ui.Stack(top, _folderLabel), main, _status); _workspace.Children.Add(_editorBody); Content = _workspace;
        KeyDown += async (_, e) => { if (_mapEditor is not null) return; var modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control; if ((e.KeyModifiers & modifier) == 0) return; if (e.Key == Key.S) { e.Handled = true; await SaveClick(); } if (e.Key == Key.F) { e.Handled = true; _search.Focus(); } };
        Closing += async (_, e) => { if (_mapEditor is not null) { e.Cancel = true; FinishRoutineMap(false); return; } if (_busy) { e.Cancel = true; return; } if (_closeApproved || !_dirty) return; e.Cancel = true; if (await CanLeave()) { _closeApproved = true; Close(); } };
        Closed += (_, _) => { FinishRoutineMap(false); _view?.Dispose(); };
        Opened += async (_, _) =>
        {
            try
            {
                string? folder = Directory.Exists(initialPath) ? initialPath : File.Exists(initialPath) ? Path.GetDirectoryName(Path.GetFullPath(initialPath)) : EditorPaths.FindDefinitions();
                if (folder is not null) await SetFolder(folder);
                if (File.Exists(initialPath)) await LoadFile(initialPath);
            }
            catch (Exception e) { _status.Text = "Could not open the Definitions folder: " + e.Message; }
        };
    }
    internal Task<bool> EditRoutineMap(RoutineRecord routine, IReadOnlyList<RoutineRecord> routines)
    {
        if (_mapCompletion is not null) return _mapCompletion.Task;
        _mapCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _mapEditor = new RoutineMapEditor(routine, routines, FinishRoutineMap);
        _editorBody.IsVisible = false; _workspace.Children.Add(_mapEditor); _mapEditor.Focus();
        return _mapCompletion.Task;
    }
    private void FinishRoutineMap(bool apply)
    {
        if (_mapEditor is null) return;
        _mapEditor.Dispose(); _workspace.Children.Remove(_mapEditor); _mapEditor = null; _editorBody.IsVisible = true;
        var completion = _mapCompletion; _mapCompletion = null; completion?.TrySetResult(apply);
    }
    private async Task PickDefinitions()
    {
        if (_busy || !await CanLeave()) return;
        string? folder = await Ui.PickFolder(this, "Select Silverpine's Definitions folder"); if (folder is not null) { await SetFolder(folder); EditorPaths.Write("definitions-folder", folder); }
    }
    private async Task PickGame()
    {
        string? folder = await Ui.PickFolder(this, "Select the folder containing Silverpine.exe / Silverpine_Data (or Silverpine.app)");
        if (folder is null) return;
        if (EditorPaths.GameData(folder) is null) throw new InvalidDataException("That folder doesn't contain Silverpine_Data or a Silverpine.app game bundle.");
        EditorPaths.Write("game-folder", folder); _status.Text = "Game folder remembered for this user. Artwork is loaded from the routine map.";
    }
    private Task SetFolder(string folder)
    {
        // Enumerate only this folder, never backups or unrelated subdirectories.
        var files = Directory.EnumerateFiles(folder).Where(p => Path.GetExtension(p).Equals(".silc", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (_folder != folder && _document is not null)
        {
            _view?.Dispose(); _view = null; _document = null; _diskHash = null; _dirty = false; _save.IsEnabled = false;
            _title.Text = "Your character workshop"; _meta.Text = "Select a SILC to begin."; _host.Content = Ui.Note("Choose a definition from the library.");
        }
        _items = files.Select(p => new LibraryItem(Path.GetFullPath(p))).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _folder = folder; _folderLabel.Text = folder; ToolTip.SetTip(_folderLabel, folder); Filter(); return Task.CompletedTask;
    }
    private void Filter()
    {
        string text = _search.Text ?? "";
        string? kind = _filter.SelectedIndex switch { 1 => "Character", 2 => "NPC", 3 => "Model", _ => null };
        var rows = _items.Where(i => (kind is null || i.Kind == kind) && (i.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(i.Path).Contains(text, StringComparison.OrdinalIgnoreCase))).ToArray();
        _restoring = true; _library.ItemsSource = rows; _library.SelectedItem = rows.FirstOrDefault(i => i.Path == _document?.FilePath); _restoring = false;
        _count.Text = $"{rows.Length} of {_items.Count} definitions";
    }
    private void RestoreSelection() { _restoring = true; _library.SelectedItem = _items.FirstOrDefault(i => i.Path == _document?.FilePath); _restoring = false; }
    internal async Task LoadFile(string path)
    {
        if (_busy) return; _busy = true; _library.IsEnabled = false; _save.IsEnabled = false;
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path); SilcDocument document = await Task.Run(() => SilcDocument.FromBytes(path, bytes));
            var view = new DefinitionView(document, MarkDirty, _items.Where(i => i.Kind == "NPC").Select(i => i.Name).Concat(NpcDefinitionModel.BaseGameNpcNames).ToArray(), _items.Where(i => i.Kind == "Character").Select(i => i.Name).ToArray());
            _view?.Dispose(); _view = view; _document = document; _diskHash = SHA256.HashData(bytes); _dirty = false;
            _host.Content = view; _save.IsEnabled = document.Definition is not null; UpdateHeader(); RestoreSelection(); _status.Text = $"Loaded {Path.GetFileName(path)}. Unknown data is preserved.";
        }
        catch (Exception e) { await Ui.Alert(this, $"Could not load {Path.GetFileName(path)}\n\n{e.Message}"); RestoreSelection(); _save.IsEnabled = _document is not null; }
        finally { _busy = false; _library.IsEnabled = true; }
    }
    private void MarkDirty() { _dirty = true; UpdateHeader(); }
    private void UpdateHeader() { _title.Text = (_dirty ? "● " : "") + _document?.Name; _meta.Text = $"{(_dirty ? "Unsaved changes" : "Saved on disk")} · {_document?.Kind} · {_document?.Version}"; }
    private async Task<bool> Save()
    {
        if (_busy || _document is null || _view is null) return false;
        _busy = true; _host.IsEnabled = false; _library.IsEnabled = false; _save.IsEnabled = false;
        try
        {
            byte[] current = await File.ReadAllBytesAsync(_document.FilePath);
            if (_diskHash is null || !SHA256.HashData(current).AsSpan().SequenceEqual(_diskHash)) throw new IOException("This SILC changed on disk after you opened it. Reload before saving to avoid overwriting changes made by the game or another editor.");
            _view.Apply(); string? backup = _document.Save(_backup.IsChecked == true);
            _diskHash = SHA256.HashData(await File.ReadAllBytesAsync(_document.FilePath)); _dirty = false; UpdateHeader();
            _status.Text = backup is null ? "Saved and validated. Automatic backup is disabled." : "Saved and validated. Backup: " + backup; return true;
        }
        catch (Exception e) { await Ui.Alert(this, "Could not save the SILC.\n\n" + e.Message); return false; }
        finally { _busy = false; _host.IsEnabled = true; _library.IsEnabled = true; _save.IsEnabled = _document?.Definition is not null; }
    }
    private async Task SaveClick() => _ = await Save();
    private async Task Reload() { if (_document is not null && (!_dirty || await Ui.Choose(this, "Discard unsaved edits and reload this SILC?", "Cancel", "Discard edits") == "Discard edits")) await LoadFile(_document.FilePath); }
    private async Task<bool> CanLeave()
    {
        if (!_dirty) return true;
        return await Ui.Choose(this, "Save changes before continuing?", "Save", "Discard", "Cancel") switch { "Save" => await Save(), "Discard" => true, _ => false };
    }
    private async Task BackupAll()
    {
        if (_folder is null || _items.Count == 0) return;
        string directory = Path.Combine(_folder, "SILC Editor Backups", "Full backups", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(directory);
        foreach (var item in _items) File.Copy(item.Path, Path.Combine(directory, Path.GetFileName(item.Path)), false);
        _status.Text = $"Backed up {_items.Count} SILCs to {directory}";
        await Ui.Alert(this, _status.Text);
    }
    private sealed record LibraryItem(string Path)
    {
        internal string Name { get { string stem = System.IO.Path.GetFileNameWithoutExtension(Path); int i = stem.IndexOf('_'); return i >= 0 ? stem[(i + 1)..] : stem; } }
        internal string Kind => System.IO.Path.GetFileName(Path).Split('_')[0] switch { "PlayerCharacter" => "Character", "NPC" => "NPC", "ModelDefinition" => "Model", _ => "Other" };
    }
}
