using SilverpineSilcEditor.Core;

namespace SilverpineSilcEditor.UI;

internal sealed class RacialChecklist : UserControl
{
    private readonly List<string> _original;
    private readonly Dictionary<string, CheckBox> _choices = new(StringComparer.Ordinal);
    private readonly TableLayoutPanel _list;
    private readonly Label _count;
    private readonly TextBox _search;
    private readonly Action _changed;
    private readonly List<(Control Row, string Search)> _rows = [];

    internal RacialChecklist(IEnumerable<string> selected, Action changed)
    {
        _original = selected.ToList();
        _changed = changed;
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        Padding = new Padding(22);
        var header = new Panel { Dock = DockStyle.Top, Height = 100 };
        header.Controls.Add(new Label { Text = "Racial traits", Font = Theme.HeadingFont, AutoSize = true, Location = new Point(0, 0) });
        header.Controls.Add(new Label { Text = "Select the traits applied by the game. Hover-free descriptions keep each effect in view.",
            AutoSize = true, ForeColor = Theme.Muted, Location = new Point(0, 34) });
        _search = new TextBox { PlaceholderText = "Filter traits by name or effect…", Width = 340, Location = new Point(0, 63) };
        _count = new Label { AutoSize = true, ForeColor = Theme.Accent, Location = new Point(360, 67) };
        header.Controls.AddRange([_search, _count]);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        _list = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
        _list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(_list);
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 82, ColumnCount = 2, RowCount = 2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var custom = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "Custom trait ID (requires a supporting game mod)" };
        var add = Theme.Button("Add custom trait", (_, _) =>
        {
            if (AddCustom(custom.Text)) custom.Clear();
        });
        footer.Controls.Add(custom, 0, 0);
        footer.Controls.Add(add, 1, 0);
        var note = new Label { Text = "Unknown traits are kept exactly as stored. Adding one here does not install it in the game.",
            Dock = DockStyle.Fill, ForeColor = Theme.Muted, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        footer.Controls.Add(note, 0, 1);
        footer.SetColumnSpan(note, 2);
        Controls.Add(scroll);
        Controls.Add(footer);
        Controls.Add(header);
        foreach (RacialTrait trait in RacialCatalog.Traits)
            AddRow(trait.Id, trait.Description, _original.Contains(trait.Id));
        foreach (string id in _original.Distinct(StringComparer.Ordinal).Where(id => !_choices.ContainsKey(id)))
            AddRow(id, "Custom / unrecognized trait — retained without renaming or trimming.", true);
        _search.TextChanged += (_, _) => Filter();
        UpdateCount();
    }

    private void AddRow(string id, string description, bool selected)
    {
        var row = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Padding = new Padding(14, 9, 14, 9),
            Margin = new Padding(0, 0, 0, 7), BackColor = Theme.Surface };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var check = new CheckBox { Text = id.Length == 0 ? "(Empty stored ID)" : id, Tag = id, Checked = selected,
            AutoSize = true, Font = Theme.EmphasisFont, Margin = new Padding(0, 0, 0, 4) };
        var detail = new Label { Text = description, AutoSize = true, Dock = DockStyle.Fill, ForeColor = Theme.Muted, Margin = new Padding(22, 0, 0, 0) };
        row.Controls.Add(check, 0, 0);
        row.Controls.Add(detail, 0, 1);
        _choices.Add(id, check);
        _rows.Add((row, id + " " + description));
        _list.Controls.Add(row, 0, _list.RowCount++);
        check.CheckedChanged += (_, _) => { UpdateCount(); _changed(); };
    }

    internal bool AddCustom(string value)
    {
        string id = value.Trim();
        if (id.Length == 0) return false;
        if (_choices.TryGetValue(id, out CheckBox? existing)) existing.Checked = true;
        else
        {
            AddRow(id, "Custom trait — requires a supporting game mod.", true);
            Theme.Style(_list);
            UpdateCount();
            _changed();
        }
        _search.Clear();
        return true;
    }

    // Preserve original order, duplicates, whitespace and unknown IDs on an untouched file.
    // Only explicitly unchecked IDs disappear; newly checked IDs are appended once.
    internal string[] SelectedIds() => _original.Where(id => _choices[id].Checked)
        .Concat(_choices.Where(pair => pair.Value.Checked && !_original.Contains(pair.Key)).Select(pair => pair.Key)).ToArray();

    private void UpdateCount() => _count.Text = $"{_choices.Count(pair => pair.Value.Checked)} selected";
    private void Filter()
    {
        _list.SuspendLayout();
        foreach (var (row, search) in _rows) row.Visible = search.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase);
        _list.ResumeLayout(true);
    }
}
