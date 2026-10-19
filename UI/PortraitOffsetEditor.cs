using System.ComponentModel;
using System.Drawing.Drawing2D;
using SilverpineSilcEditor.Core;

namespace SilverpineSilcEditor.UI;

internal sealed class PortraitOffsetEditor : UserControl
{
    private readonly PlayerDefinitionModel _model;
    private readonly FloatNumericUpDown _scale, _offsetX, _offsetY;
    private readonly List<(NumericUpDown Source, EventHandler Handler)> _sourceHandlers = [];
    private readonly decimal _originalScale, _originalX, _originalY;
    private readonly Label _hint;
    private byte[]? _lastData;

    internal PortraitOffsetCanvas Canvas { get; } = new() { Dock = DockStyle.Fill };

    internal PortraitOffsetEditor(PlayerDefinitionModel model, NumericUpDown scale, NumericUpDown offsetX, NumericUpDown offsetY)
    {
        _model = model;
        _scale = (FloatNumericUpDown)scale;
        _offsetX = (FloatNumericUpDown)offsetX;
        _offsetY = (FloatNumericUpDown)offsetY;
        (_originalScale, _originalX, _originalY) = (scale.Value, offsetX.Value, offsetY.Value);
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        var views = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        var values = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        var mode = Choice("PortraitPreviewMode", "In-game offset editor", "NPC dialogue (mirrored)");
        mode.Width = 205;
        var aspect = Choice("PortraitAspectRatio", "16:9", "16:10", "21:9", "4:3");
        aspect.Width = 85;
        var guides = new CheckBox { Text = "Game guide lines", Checked = true, AutoSize = true, Margin = new Padding(10, 7, 3, 3) };
        var silhouette = new CheckBox { Text = "Game silhouette", Checked = true, AutoSize = true, Margin = new Padding(10, 7, 3, 3) };
        views.Controls.AddRange([Caption("View"), mode, Caption("Game aspect"), aspect, guides, silhouette]);
        values.Controls.AddRange([Caption("Offset X"), Bind(offsetX, "PortraitOffsetX"), Caption("Offset Y"), Bind(offsetY, "PortraitOffsetY"),
            Caption("Scale"), Bind(scale, "PortraitScale")]);
        var reset = new Button { Text = "Reset to 0, 0 / 1×", AutoSize = true };
        var restore = new Button { Text = "Restore opened values", AutoSize = true };
        values.Controls.AddRange([reset, restore]);
        _hint = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
        layout.Controls.Add(views, 0, 0);
        layout.Controls.Add(values, 0, 1);
        layout.Controls.Add(Canvas, 0, 2);
        layout.Controls.Add(_hint, 0, 3);
        Controls.Add(layout);

        mode.SelectedIndexChanged += (_, _) =>
        {
            Canvas.Npc = mode.SelectedIndex == 1;
            silhouette.Enabled = !Canvas.Npc; // The game's reference silhouette occupies the NPC slot.
            Canvas.ShowSilhouette = silhouette.Checked && !Canvas.Npc;
            Canvas.Invalidate();
            UpdateHint();
        };
        aspect.SelectedIndexChanged += (_, _) =>
        {
            Canvas.AspectRatio = new[] { 16f / 9, 16f / 10, 21f / 9, 4f / 3 }[aspect.SelectedIndex];
            Canvas.Invalidate();
        };
        guides.CheckedChanged += (_, _) => { Canvas.ShowGuides = guides.Checked; Canvas.Invalidate(); };
        silhouette.CheckedChanged += (_, _) => { Canvas.ShowSilhouette = silhouette.Checked && !Canvas.Npc; Canvas.Invalidate(); };
        reset.Click += (_, _) => SetValues(0, 0, 1);
        restore.Click += (_, _) => SetValues(_originalX, _originalY, _originalScale);
        Canvas.OffsetRequested += point => { SetValue(_offsetX, (decimal)point.X); SetValue(_offsetY, (decimal)point.Y); };
        Canvas.ScaleRequested += delta => SetValue(_scale, Math.Clamp(_scale.Value + delta, .01m, Math.Max(100m, _scale.Maximum)));
        VisibleChanged += (_, _) => { if (Visible) RefreshPortrait(); };
        UpdateValues();
        RefreshPortrait();
    }

    private NumericUpDown Bind(NumericUpDown source, string name)
    {
        var copy = new NumericUpDown { Name = name, Width = 108, DecimalPlaces = source.DecimalPlaces,
            Increment = source.Increment, Minimum = source.Minimum, Maximum = source.Maximum, Value = source.Value };
        bool syncing = false;
        EventHandler handler = (_, _) =>
        {
            syncing = true;
            try { SetValue(copy, source.Value); }
            finally { syncing = false; }
            UpdateValues();
        };
        source.ValueChanged += handler;
        _sourceHandlers.Add((source, handler));
        copy.ValueChanged += (_, _) => { if (!syncing) SetValue(source, copy.Value); };
        return copy;
    }

    private void SetValues(decimal x, decimal y, decimal scale)
    {
        SetValue(_offsetX, x);
        SetValue(_offsetY, y);
        SetValue(_scale, scale);
    }

    private static void SetValue(NumericUpDown control, decimal value)
    {
        control.Minimum = Math.Min(control.Minimum, value);
        control.Maximum = Math.Max(control.Maximum, value);
        control.Value = value;
    }

    private void UpdateValues()
    {
        Canvas.Offset = new PointF(_offsetX.FloatValue, _offsetY.FloatValue);
        Canvas.PortraitScale = _scale.FloatValue;
        Canvas.Invalidate();
    }

    internal void RefreshPortrait()
    {
        byte[]? data = _model.Images.FirstOrDefault(asset => asset.Category == PlayerImageCategory.Fixed && asset.Name == "Dialog")?.Data;
        if (!ReferenceEquals(data, _lastData))
        {
            _lastData = data;
            Canvas.SetPortrait(data);
        }
        UpdateHint();
    }

    protected override void OnEnter(EventArgs e) { base.OnEnter(e); RefreshPortrait(); }

    private void UpdateHint() => _hint.Text = Canvas.ImageError ??
        "Drag anywhere in the preview • Wheel: scale • Arrow keys: move 1 unit (Shift: 10). Changes use the normal Save and backup settings.\n" +
        (Canvas.Npc ? "NPC view uses the game's mirrored portrait and X = 177 − stored X. Reference silhouette hidden."
            : "Original game silhouette and guides. Choose your game's aspect ratio; +Y moves up. Replace the portrait in Images → Dialog.");

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(6, 7, 3, 3) };
    private static ComboBox Choice(string name, params string[] items)
    {
        var combo = new ComboBox { Name = name, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(items);
        combo.SelectedIndex = 0;
        return combo;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var (source, handler) in _sourceHandlers) source.ValueChanged -= handler;
        base.Dispose(disposing);
    }
}

internal sealed class PortraitOffsetCanvas : Control
{
    private readonly Bitmap _silhouette = LoadGuide("silhouette.png");
    private readonly Bitmap _guides = LoadGuide("rule-of-thirds.png");
    private Bitmap? _portrait;
    private Point _dragStart;
    private PointF _dragOffset;
    private bool _dragging;

    internal event Action<PointF>? OffsetRequested;
    internal event Action<decimal>? ScaleRequested;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal PointF Offset { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal float PortraitScale { get; set; } = 1;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal float AspectRatio { get; set; } = 16f / 9;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Npc { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowGuides { get; set; } = true;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowSilhouette { get; set; } = true;
    internal string? ImageError { get; private set; }
    internal Bitmap? PortraitImage => _portrait;

    internal PortraitOffsetCanvas()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = true;
        BackColor = Theme.Background;
        Cursor = Cursors.SizeAll;
        AccessibleName = "Portrait placement preview";
        AccessibleDescription = "Drag to position, scroll to scale, or use arrow keys. Numeric controls provide equivalent editing.";
    }

    internal void SetPortrait(byte[]? bytes)
    {
        _portrait?.Dispose();
        _portrait = null;
        ImageError = null;
        try
        {
            if (bytes is null || bytes.Length == 0) throw new InvalidDataException("No Dialog PNG is available.");
            using var stream = new MemoryStream(bytes, writable: false);
            using var decoded = Image.FromStream(stream);
            _portrait = new Bitmap(decoded);
        }
        catch (Exception exception) when (exception is ArgumentException or OutOfMemoryException or InvalidDataException)
        {
            ImageError = "Portrait preview unavailable: " + exception.Message + " Replace it in Images → Dialog. Stored image bytes are unchanged.";
        }
        Invalidate();
    }

    private static Bitmap LoadGuide(string file)
    {
        using Stream stream = typeof(PortraitOffsetCanvas).Assembly.GetManifestResourceStream("SilverpineSilcEditor.Assets.PortraitGuides." + file)
            ?? throw new InvalidOperationException("Missing embedded portrait guide: " + file);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    internal RectangleF Viewport
    {
        get
        {
            float height = PortraitLayout.CanvasHeight(AspectRatio);
            float fit = Math.Max(0, Math.Min((ClientSize.Width - 16) / PortraitLayout.CanvasWidth, (ClientSize.Height - 16) / height));
            return new RectangleF((ClientSize.Width - PortraitLayout.CanvasWidth * fit) / 2,
                (ClientSize.Height - height * fit) / 2, PortraitLayout.CanvasWidth * fit, height * fit);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        RectangleF viewport = Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0) return;
        Graphics graphics = e.Graphics;
        using var background = new SolidBrush(Color.FromArgb(82, 86, 91));
        graphics.FillRectangle(background, viewport);
        GraphicsState state = graphics.Save();
        try
        {
            graphics.SetClip(viewport);
            graphics.TranslateTransform(viewport.X, viewport.Y);
            float fit = viewport.Width / PortraitLayout.CanvasWidth;
            graphics.ScaleTransform(fit, fit);
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float height = PortraitLayout.CanvasHeight(AspectRatio);
            if (ShowSilhouette && !Npc) Draw(graphics, _silhouette, PortraitLayout.Silhouette(_silhouette.Size, height));
            if (_portrait is not null)
                Draw(graphics, _portrait, PortraitLayout.Portrait(_portrait.Size, height, Offset.X, Offset.Y, PortraitScale, Npc));
            if (ShowGuides) graphics.DrawImage(_guides, PortraitLayout.Guides(height));
        }
        finally { graphics.Restore(state); }
        using var border = new Pen(Focused ? Theme.Accent : Theme.Border, 1);
        graphics.DrawRectangle(border, viewport.X, viewport.Y, viewport.Width, viewport.Height);
        if (_portrait is null)
            TextRenderer.DrawText(graphics, "No readable Dialog portrait", Font, Rectangle.Round(viewport), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static void Draw(Graphics graphics, Image image, PortraitPlacement placement)
    {
        RectangleF rect = placement.Bounds;
        if (rect.Width <= 0 || rect.Height <= 0 || !float.IsFinite(rect.X) || !float.IsFinite(rect.Y)
            || !float.IsFinite(rect.Right) || !float.IsFinite(rect.Bottom)) return;
        float left = placement.FlipX ? rect.Right : rect.Left, right = placement.FlipX ? rect.Left : rect.Right;
        float top = placement.FlipY ? rect.Bottom : rect.Top, bottom = placement.FlipY ? rect.Top : rect.Bottom;
        graphics.DrawImage(image, [new PointF(left, top), new PointF(right, top), new PointF(left, bottom)],
            new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _portrait is null || !Viewport.Contains(e.Location)) return;
        Focus();
        _dragStart = e.Location;
        _dragOffset = Offset;
        _dragging = true;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || Viewport.Width <= 0) return;
        OffsetRequested?.Invoke(PortraitLayout.DragOffset(_dragOffset,
            new PointF(e.X - _dragStart.X, e.Y - _dragStart.Y), Viewport.Width / PortraitLayout.CanvasWidth, Npc));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) { _dragging = false; Capture = false; }
    }

    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) _dragging = false; }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_portrait is not null && Viewport.Contains(e.Location)) ScaleRequested?.Invoke(e.Delta / 120m * .05m);
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        float step = e.Shift ? 10 : 1;
        PointF delta = e.KeyCode switch
        {
            Keys.Left => new PointF(-step, 0), Keys.Right => new PointF(step, 0),
            Keys.Up => new PointF(0, -step), Keys.Down => new PointF(0, step), _ => PointF.Empty
        };
        if (delta == PointF.Empty || _portrait is null) return;
        OffsetRequested?.Invoke(PortraitLayout.DragOffset(Offset, delta, 1, Npc));
        e.Handled = e.SuppressKeyPress = true;
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _portrait?.Dispose(); _silhouette.Dispose(); _guides.Dispose(); }
        base.Dispose(disposing);
    }
}
