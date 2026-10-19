using System.ComponentModel;
using System.Globalization;
using SilverpineSilcEditor.Core;

namespace SilverpineSilcEditor.UI;

internal static class Theme
{
    internal static void DisposeChildren(Control parent)
    {
        foreach (Control child in parent.Controls.Cast<Control>().ToArray())
            child.Dispose();
    }

    internal static readonly Color Background = Color.FromArgb(18, 26, 31);
    internal static readonly Color Surface = Color.FromArgb(26, 38, 44);
    internal static readonly Color Raised = Color.FromArgb(36, 50, 57);
    internal static readonly Color Border = Color.FromArgb(53, 72, 80);
    internal static readonly Color Text = Color.FromArgb(234, 241, 240);
    internal static readonly Color Muted = Color.FromArgb(157, 178, 183);
    internal static readonly Color Accent = Color.FromArgb(119, 218, 185);
    internal static readonly Color Danger = Color.FromArgb(232, 102, 102);
    internal static readonly Color Success = Color.FromArgb(89, 190, 137);
    internal static readonly Font BodyFont = new("Segoe UI", 10f);
    internal static readonly Font HeadingFont = new("Segoe UI Semibold", 17f);
    internal static readonly Font EmphasisFont = new("Segoe UI Semibold", 10f);
    internal static readonly Font CodeFont = new("Cascadia Mono", 10f);

    internal static void Style(Control control)
    {
        // Preserve deliberate heading/code fonts and warning/accent colors.
        if (control.Font.Equals(Control.DefaultFont)) control.Font = BodyFont;
        if (control.ForeColor == SystemColors.ControlText || control.ForeColor == SystemColors.WindowText) control.ForeColor = Text;
        control.BackColor = control switch
        {
            TextBoxBase => Raised,
            ListView => Surface,
            ListBox => Surface,
            DataGridView => Surface,
            TabControl => Background,
            _ => control.BackColor
        };

        if (control is Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 72, 76);
            button.BackColor = Equals(button.Tag, "primary") ? Color.FromArgb(38, 100, 82) : Raised;
            button.Padding = new Padding(12, 5, 12, 5);
            button.Cursor = Cursors.Hand;
        }
        else if (control is TextBox textBox)
        {
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }
        else if (control is NumericUpDown numeric)
        {
            numeric.BackColor = Raised;
            numeric.ForeColor = Text;
            numeric.BorderStyle = BorderStyle.FixedSingle;
        }
        else if (control is ComboBox combo)
        {
            combo.BackColor = Raised;
            combo.ForeColor = Text;
            combo.FlatStyle = FlatStyle.Flat;
        }
        else if (control is DataGridView grid)
        {
            StyleGrid(grid);
        }
        else if (control is ListBox list) list.BorderStyle = BorderStyle.None;

        foreach (Control child in control.Controls)
            Style(child);
    }

    internal static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Border;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Raised;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Raised;
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(43, 85, 79);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Background;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersHeight = 40;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersDefaultCellStyle.Font = EmphasisFont;
        grid.DefaultCellStyle.Padding = new Padding(7, 4, 7, 4);
        grid.RowHeadersVisible = false;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        grid.RowTemplate.Height = 38;
    }

    internal static Button Button(string text, EventHandler click)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(0, 36), Height = 36 };
        button.Click += click;
        return button;
    }

    internal static Label Label(string text, bool muted = false) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = muted ? Muted : Text,
        Margin = new Padding(3, 8, 8, 3)
    };

    internal static void InitializeSplitter(SplitContainer split, int desiredDistance)
    {
        bool initialized = false;
        void TryInitialize()
        {
            if (initialized)
                return;
            int available = split.Orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height;
            if (available < desiredDistance + 140)
                return;
            split.SplitterDistance = Math.Min(desiredDistance, available - 120);
            initialized = true;
        }

        split.Resize += (_, _) => TryInitialize();
        split.HandleCreated += (_, _) => split.BeginInvoke(() =>
        {
            if (!split.IsDisposed)
                TryInitialize();
        });
    }
}

internal sealed class FormFields
{
    private readonly Action _changed;
    private int _row;

    internal FormFields(Action changed)
    {
        _changed = changed;
        Panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Background,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 1
        };
        Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 215));
        Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    }

    internal TableLayoutPanel Panel { get; }

    internal void Section(string title, string? description = null)
    {
        var heading = new Label { Text = title, AutoSize = true, Font = Theme.EmphasisFont, ForeColor = Theme.Accent,
            Margin = new Padding(3, _row == 0 ? 0 : 18, 3, 12) };
        Panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Panel.Controls.Add(heading, 0, _row++);
        Panel.SetColumnSpan(heading, 2);
        if (description is not null) Note(description);
    }

    internal Label CharacterCount(TextBox input, int referenceLimit)
    {
        Label label = Note("");
        void Update()
        {
            label.Text = $"{input.TextLength:N0} / {referenceLimit:N0} characters in the game editor" +
                (input.TextLength > referenceLimit ? " — longer existing text is preserved." : "");
            label.ForeColor = input.TextLength > referenceLimit ? Theme.Danger : Theme.Muted;
        }
        input.TextChanged += (_, _) => Update();
        Update();
        return label;
    }

    internal TextBox Text(string label, string value, bool multiline = false, int multilineHeight = 105)
    {
        var control = new TextBox
        {
            Text = value,
            Dock = DockStyle.Top,
            Multiline = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            Height = multiline ? multilineHeight : 28,
            AcceptsReturn = multiline
        };
        control.TextChanged += (_, _) => _changed();
        Add(label, control, multiline ? multilineHeight + 13 : 38);
        return control;
    }

    internal CheckBox Check(string label, bool value)
    {
        var control = new CheckBox { Checked = value, AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
        control.CheckedChanged += (_, _) => _changed();
        Add(label, control, 38);
        return control;
    }

    internal NumericUpDown Integer(string label, int value, int min = -1_000_000, int max = 1_000_000)
    {
        var control = new NumericUpDown
        {
            Minimum = Math.Min(min, value),
            Maximum = Math.Max(max, value),
            Width = 180,
            ThousandsSeparator = true
        };
        control.Value = value;
        control.ValueChanged += (_, _) => _changed();
        Add(label, control, 38);
        return control;
    }

    internal NumericUpDown Decimal(string label, float value, decimal min = -10000, decimal max = 10000)
    {
        var control = new FloatNumericUpDown(value)
        {
            Minimum = Math.Min(min, (decimal)value),
            Maximum = Math.Max(max, (decimal)value),
            DecimalPlaces = 5,
            Increment = 0.05m,
            Width = 180
        };
        control.Value = (decimal)value;
        control.ValueChanged += (_, _) => _changed();
        Add(label, control, 38);
        return control;
    }

    internal ComboBox Choice(string label, IEnumerable<string> values, int selectedIndex)
    {
        var control = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
        control.Items.AddRange(values.Select((name, value) => (object)new NumericChoice(value, name)).ToArray());
        SetChoiceValue(control, selectedIndex);
        control.SelectedIndexChanged += (_, _) => _changed();
        Add(label, control, 38);
        return control;
    }

    internal static int ChoiceValue(ComboBox control) => ((NumericChoice)control.SelectedItem!).Value;

    internal static void SetChoiceValue(ComboBox control, int value, string? customLabel = null)
    {
        NumericChoice? item = control.Items.Cast<NumericChoice>().FirstOrDefault(item => item.Value == value);
        if (item is null)
        {
            item = new NumericChoice(value, customLabel ?? $"Custom ({value})");
            control.Items.Add(item);
        }
        control.SelectedItem = item;
    }

    private sealed record NumericChoice(int Value, string Name)
    {
        public override string ToString() => Name;
    }

    internal CheckedListBox Checks(string label, IEnumerable<string> values, IEnumerable<int> selected)
    {
        var selectedSet = selected.ToHashSet();
        var control = new CheckedListBox
        {
            Height = 90,
            Dock = DockStyle.Top,
            CheckOnClick = true,
            BorderStyle = BorderStyle.FixedSingle
        };
        int index = 0;
        foreach (string value in values)
            control.Items.Add(value, selectedSet.Contains(index++));
        control.ItemCheck += (_, _) => BeginChanged(control);
        Add(label, control, 102);
        return control;
    }

    internal Label Note(string text, Color? color = null)
    {
        var note = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(780, 0),
            ForeColor = color ?? Theme.Muted,
            Margin = new Padding(3, 5, 3, 12)
        };
        Panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Panel.Controls.Add(note, 0, _row);
        Panel.SetColumnSpan(note, 2);
        _row++;
        return note;
    }

    private void Add(string label, Control control, int height)
    {
        Panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height + 8));
        Panel.Controls.Add(Theme.Label(label, true), 0, _row);
        Panel.Controls.Add(control, 1, _row);
        _row++;
    }

    private void BeginChanged(Control control)
    {
        if (control.IsHandleCreated)
            control.BeginInvoke(_changed);
        else
            _changed();
    }
}

internal sealed class FloatNumericUpDown(float originalValue) : NumericUpDown
{
    // NumericUpDown uses decimal; converting a float through decimal can round it.
    // Keep the exact original bits whenever the displayed value is unchanged.
    internal float FloatValue => Value == (decimal)originalValue ? originalValue : (float)Value;
}

internal sealed record StorageFieldBinding(Func<object> Read, Action<object> Write);

internal sealed class AdvancedStorageEditor : UserControl
{
    private readonly DataGridView _grid;
    private bool _loading;

    internal AdvancedStorageEditor(
        VssContainer storage,
        Action changed,
        IReadOnlyDictionary<string, StorageFieldBinding>? bindings = null)
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Key", ReadOnly = true, Width = 260 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type", ReadOnly = true, Width = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        Controls.Add(_grid);

        _loading = true;
        foreach (VssEntry entry in storage.Entries)
        {
            int row = _grid.Rows.Add(entry.Name, entry.Kind.ToString(), entry.DisplayValue);
            _grid.Rows[row].Tag = entry;
            if (entry.Kind == VssValueKind.Blob)
            {
                _grid.Rows[row].Cells[2].ReadOnly = true;
                _grid.Rows[row].Cells[2].Style.ForeColor = Theme.Muted;
            }
        }
        _loading = false;

        void RefreshValues()
        {
            _loading = true;
            try
            {
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    var entry = (VssEntry)row.Tag!;
                    object value = bindings is not null && bindings.TryGetValue(entry.Name, out var binding)
                        ? binding.Read() : entry.Value;
                    row.Cells[2].Value = new VssEntry { Name = entry.Name, Kind = entry.Kind, Value = value }.DisplayValue;
                }
            }
            finally { _loading = false; }
        }
        VisibleChanged += (_, _) => { if (Visible) RefreshValues(); };
        Enter += (_, _) => RefreshValues();

        _grid.CellValidating += (_, eventArgs) =>
        {
            if (_loading || IsDisposed || Disposing || eventArgs.RowIndex < 0
                || eventArgs.RowIndex >= _grid.Rows.Count || eventArgs.ColumnIndex != 2)
                return;
            var entry = (VssEntry)_grid.Rows[eventArgs.RowIndex].Tag!;
            if (entry.Kind == VssValueKind.Blob)
                return;
            string value = Convert.ToString(eventArgs.FormattedValue, CultureInfo.InvariantCulture) ?? "";
            var parsed = new VssEntry { Name = entry.Name, Kind = entry.Kind, Value = entry.Value };
            if (!storage.TrySetDisplayValue(parsed, value, out string? error))
            {
                eventArgs.Cancel = true;
                _grid.Rows[eventArgs.RowIndex].ErrorText = error ?? "Invalid value.";
            }
            else
            {
                try
                {
                    if (bindings is not null && bindings.TryGetValue(entry.Name, out var binding))
                    {
                        if (!Equals(binding.Read(), parsed.Value))
                            binding.Write(parsed.Value);
                    }
                    else
                        entry.Value = parsed.Value;
                    _grid.Rows[eventArgs.RowIndex].ErrorText = "";
                    changed();
                }
                catch (Exception exception)
                {
                    eventArgs.Cancel = true;
                    _grid.Rows[eventArgs.RowIndex].ErrorText = exception.Message;
                }
            }
        };
        Theme.Style(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _loading = true;
        base.Dispose(disposing);
    }
}

internal sealed class TextPromptDialog : Form
{
    private readonly TextBox _input;

    private TextPromptDialog(string title, string prompt, string value)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(440, 145);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;

        var label = Theme.Label(prompt);
        label.Location = new Point(18, 16);
        _input = new TextBox { Text = value, Location = new Point(20, 48), Width = 398 };
        var ok = Theme.Button("OK", (_, _) => { DialogResult = DialogResult.OK; Close(); });
        ok.Location = new Point(244, 95);
        var cancel = Theme.Button("Cancel", (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        cancel.Location = new Point(334, 95);
        Controls.AddRange([label, _input, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
        Theme.Style(this);
    }

    internal static string? Show(IWin32Window owner, string title, string prompt, string value = "")
    {
        using var dialog = new TextPromptDialog(title, prompt, value);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._input.Text.Trim() : null;
    }
}

internal sealed class ListEditorToolbar : FlowLayoutPanel
{
    internal ListEditorToolbar(string? warning = null)
    {
        Dock = DockStyle.Top;
        Height = warning is null ? 45 : 70;
        Padding = new Padding(8, 6, 8, 4);
        WrapContents = false;
        BackColor = Theme.Surface;
        if (warning is not null)
        {
            var label = Theme.Label(warning, true);
            label.MaximumSize = new Size(700, 0);
            Controls.Add(label);
        }
    }
}
