using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SilverpineSilcEditor.Core;
using System.Globalization;

namespace SilverpineSilcEditor.Desktop;

internal static class Ui
{
    internal static readonly IBrush Background = Brush.Parse("#121A1F"), Surface = Brush.Parse("#1A262C"), Accent = Brush.Parse("#79DAB9"), Muted = Brush.Parse("#A5BABE"), Danger = Brush.Parse("#FF8585");
    internal static TextBlock Text(string text, double size = 14, IBrush? color = null) => new() { Text = text, FontSize = size, Foreground = color ?? Brushes.WhiteSmoke, TextWrapping = TextWrapping.Wrap };
    internal static TextBlock Note(string text) => Text(text, 13, Muted);
    internal static Button Button(string text, Action action, bool primary = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(13, 8), CornerRadius = new CornerRadius(6) };
        if (primary) { b.Background = Brush.Parse("#296752"); b.Foreground = Brushes.White; }
        b.Click += (_, _) => action(); return b;
    }
    internal static Button AsyncButton(string text, Func<Task> action, bool primary = false)
    {
        Button? b = null;
        b = Button(text, async () => { try { await action(); } catch (Exception e) { await Alert(TopLevel.GetTopLevel(b!) as Window ?? CurrentWindow!, e.Message); } }, primary);
        return b;
    }
    internal static Window? CurrentWindow { get; set; }
    internal static StackPanel Stack(params Control[] children) { var p = new StackPanel { Spacing = 12 }; foreach (var c in children) p.Children.Add(c); return p; }
    internal static WrapPanel Row(params Control[] children) { var p = new WrapPanel { Orientation = Orientation.Horizontal }; foreach (var c in children) { c.Margin = new Thickness(0, 0, 8, 8); p.Children.Add(c); } return p; }
    internal static ScrollViewer Scroll(Control child) => new() { Content = child, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    internal static Border Card(Control child, double padding = 20) => new() { Background = Surface, Padding = new Thickness(padding), CornerRadius = new CornerRadius(9), Child = child };
    internal static Control Split(Control left, Control right, double width = 245)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{width},12,*") };
        grid.Children.Add(left); Grid.SetColumn(right, 2); grid.Children.Add(right); return grid;
    }
    internal static Control Dock(Control top, Control content, Control? bottom = null)
    {
        var p = new DockPanel(); DockPanel.SetDock(top, Avalonia.Controls.Dock.Top); p.Children.Add(top);
        if (bottom is not null) { DockPanel.SetDock(bottom, Avalonia.Controls.Dock.Bottom); p.Children.Add(bottom); }
        p.Children.Add(content); return p;
    }
    internal static async Task<string?> PickFile(Window owner, string extension, string title)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = ["*." + extension] }] });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
    internal static async Task<string?> PickFolder(Window owner, string title)
        => (await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false })).FirstOrDefault()?.TryGetLocalPath();
    internal static async Task<string?> SaveFile(Window owner, string name, string extension)
        => (await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = name, DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = ["*." + extension] }] }))?.TryGetLocalPath();
    internal static Window Dialog(string title, double width = 540, double height = 290) => new() { Title = title, Width = width, Height = height, MinWidth = 380, MinHeight = 220, Background = Background, WindowStartupLocation = WindowStartupLocation.CenterOwner, Padding = new Thickness(24) };
    internal static async Task<string?> Choose(Window owner, string message, params string[] choices)
    {
        var w = Dialog("SILC Studio"); var row = new WrapPanel();
        foreach (string choice in choices) row.Children.Add(Button(choice, () => w.Close(choice)));
        w.Content = Dock(Scroll(Text(message)), new Border(), row);
        return await w.ShowDialog<string?>(owner);
    }
    internal static async Task Alert(Window owner, string message) => _ = await Choose(owner, message, "OK");
    internal static async Task<string?> Prompt(Window owner, string title, string initial = "")
    {
        var w = Dialog(title); var box = new TextBox { Text = initial };
        w.Content = Stack(Text(title, 20), box, Row(Button("Cancel", () => w.Close(null)), Button("Apply", () => w.Close(box.Text), true)));
        return await w.ShowDialog<string?>(owner);
    }
}

internal sealed class EditState(Action changed)
{
    internal Action Changed { get; } = changed;
    private readonly Dictionary<Control, string> _errors = [];
    internal void Error(Control control, string? message) { if (message is null) _errors.Remove(control); else _errors[control] = message; }
    internal void Validate() { if (_errors.Count > 0) throw new InvalidDataException(_errors.Values.First()); }
    internal void Forget(Control root)
    {
        foreach (Control c in _errors.Keys.Where(c => c == root || c.GetSelfAndLogicalAncestors().Contains(root)).ToArray()) _errors.Remove(c);
    }
}

internal sealed class Fields(EditState state)
{
    internal StackPanel Panel { get; } = new() { Spacing = 16, Margin = new Thickness(24), MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Stretch };
    internal void Heading(string text, string? help = null) { Panel.Children.Add(Ui.Text(text, 22, Ui.Accent)); if (help is not null) Panel.Children.Add(Ui.Note(help)); }
    internal void Note(string text) => Panel.Children.Add(Ui.Note(text));
    internal void Add(string label, Control input, string? help = null)
    {
        var p = Ui.Stack(Ui.Text(label, 14), input); p.Spacing = 6; if (!string.IsNullOrEmpty(help)) p.Children.Add(Ui.Note(help)); Panel.Children.Add(p);
    }
    internal TextBox Text(string label, string value, Action<string> set, bool multiline = false, string? help = null, bool code = false)
    {
        var box = new TextBox { Text = value, AcceptsReturn = multiline, AcceptsTab = code, MinHeight = multiline ? (code ? 380 : 110) : 34, TextWrapping = code ? TextWrapping.NoWrap : TextWrapping.Wrap };
        if (code) { box.FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"); box.FontSize = 13; ScrollViewer.SetHorizontalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto); }
        string last = value;
        box.TextChanged += (_, _) => { string text = box.Text ?? ""; if (text == last) return; last = text; set(text); state.Changed(); }; Add(label, box, help); return box;
    }
    internal TextBox Number(string label, string value, Func<string, bool> set, string? help = null)
    {
        var box = new TextBox { Text = value, MaxWidth = 240, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 170 };
        string last = value;
        box.TextChanged += (_, _) => { string text = box.Text ?? ""; if (text == last) return; last = text; bool ok = set(text); state.Error(box, ok ? null : $"{label}: enter a valid number."); box.BorderBrush = ok ? null : Ui.Danger; state.Changed(); };
        Add(label, box, help); return box;
    }
    internal TextBox Int(string label, int value, Action<int> set, string? help = null) => Number(label, value.ToString(CultureInfo.InvariantCulture), s => { if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return false; set(n); return true; }, help);
    internal TextBox Float(string label, float value, Action<float> set) => Number(label, value.ToString("R", CultureInfo.InvariantCulture), s => { if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) || !float.IsFinite(n)) return false; set(n); return true; });
    internal CheckBox Check(string label, bool value, Action<bool> set, string? help = null, bool markDirty = true)
    {
        var b = new CheckBox { Content = label, IsChecked = value }; b.IsCheckedChanged += (_, _) => { set(b.IsChecked == true); if (markDirty) state.Changed(); };
        Panel.Children.Add(b); if (help is not null) Note(help); return b;
    }
    internal ComboBox Choice(string label, int value, string[] names, Action<int> set, string? help = null, int[]? values = null, bool markDirty = true)
    {
        var options = names.Select((name, i) => new Option(values?[i] ?? i, name)).ToList();
        if (!options.Any(o => o.Value == value)) options.Add(new(value, $"Custom ({value})"));
        var box = new ComboBox { ItemsSource = options, SelectedItem = options.First(o => o.Value == value), MinWidth = 220 };
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is Option o) { set(o.Value); if (markDirty) state.Changed(); } }; Add(label, box, help); return box;
    }
    internal AutoCompleteBox Auto(string label, string value, IEnumerable<string> suggestions, Action<string> set, string? help = null)
    {
        var b = new AutoCompleteBox { Text = value, ItemsSource = suggestions.ToArray(), FilterMode = AutoCompleteFilterMode.Contains, MinimumPrefixLength = 0 };
        string last = value;
        b.TextChanged += (_, _) => { string text = b.Text ?? ""; if (text == last) return; last = text; set(text); state.Changed(); }; Add(label, b, help); return b;
    }
    private sealed record Option(int Value, string Name) { public override string ToString() => Name; }
}
