namespace SilverpineSilcEditor.UI;

internal sealed class WorkspaceTabs : TabControl
{
    internal WorkspaceTabs()
    {
        Dock = DockStyle.Fill;
        DrawMode = TabDrawMode.OwnerDrawFixed;
        Padding = new Point(17, 10);
        Multiline = true;
        Font = Theme.EmphasisFont;
        BackColor = Theme.Background;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        bool selected = e.Index == SelectedIndex;
        Rectangle rect = GetTabRect(e.Index);
        using var background = new SolidBrush(selected ? Theme.Raised : Theme.Surface);
        e.Graphics.FillRectangle(background, rect);
        TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, Font, rect, selected ? Theme.Accent : Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (selected)
        {
            using var line = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(line, rect.Left + 12, rect.Bottom - 3, rect.Width - 24, 3);
        }
        if (Focused && selected) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(rect, -5, -5));
    }
}

internal sealed class DefinitionLibraryView : ListView
{
    private readonly ImageList _spacing = new() { ImageSize = new Size(1, 47) };

    internal DefinitionLibraryView()
    {
        Dock = DockStyle.Fill;
        View = View.Details;
        FullRowSelect = true;
        HideSelection = false;
        MultiSelect = false;
        HeaderStyle = ColumnHeaderStyle.None;
        BorderStyle = BorderStyle.None;
        OwnerDraw = true;
        DoubleBuffered = true;
        SmallImageList = _spacing;
        Columns.Add("Definition", 280);
        Resize += (_, _) => Columns[0].Width = Math.Max(80, ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e) => e.DrawDefault = true;
    protected override void OnDrawItem(DrawListViewItemEventArgs e) { }
    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        if (e.Item?.Tag is not DefinitionListItem item) { e.DrawDefault = true; return; }
        bool selected = e.Item.Selected;
        Rectangle rect = e.Bounds;
        using var background = new SolidBrush(selected ? Theme.Raised : Theme.Surface);
        e.Graphics.FillRectangle(background, rect);
        if (selected)
        {
            using var line = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(line, rect.X, rect.Y + 5, 3, rect.Height - 10);
        }
        TextRenderer.DrawText(e.Graphics, item.Name, Theme.EmphasisFont, new Rectangle(rect.X + 13, rect.Y + 5, rect.Width - 20, 21),
            selected ? Theme.Accent : Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, item.TypeLabel + " definition", Font, new Rectangle(rect.X + 13, rect.Y + 25, rect.Width - 20, 18),
            Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { SmallImageList = null; _spacing.Dispose(); }
        base.Dispose(disposing);
    }
}
