using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SilverpineSilcEditor.Core;
using System.Globalization;

namespace SilverpineSilcEditor.Desktop;

internal sealed class PortraitEditor : UserControl, IDisposable
{
    private readonly PortraitCanvas _canvas;
    internal PortraitEditor(PlayerDefinitionModel model, EditState state)
    {
        _canvas = new PortraitCanvas(model); var f = new Fields(state);
        var original = (model.OffsetX, model.OffsetY, model.Scale);
        f.Heading("Portrait placement", "Drag the portrait to adjust its offset. The silhouette and guide lines come from the game's editor. Transparent PNG margins are included in positioning.");
        f.Choice("Preview", 0, ["In-game offset editor", "NPC dialogue (mirrored)"], n => { _canvas.Npc = n == 1; _canvas.InvalidateVisual(); }, markDirty: false);
        f.Choice("Game aspect ratio", 0, ["16:9", "16:10", "21:9", "4:3"], n => { _canvas.Aspect = new[] { 16f / 9, 16f / 10, 21f / 9, 4f / 3 }[n]; _canvas.InvalidateVisual(); }, markDirty: false);
        TextBox x = f.Float("Offset X", model.OffsetX, n => { model.OffsetX = n; _canvas.InvalidateVisual(); });
        TextBox y = f.Float("Offset Y", model.OffsetY, n => { model.OffsetY = n; _canvas.InvalidateVisual(); });
        TextBox scale = f.Float("Scale", model.Scale, n => { model.Scale = n; _canvas.InvalidateVisual(); });
        void Set(float a, float b, float c) { x.Text = a.ToString("R", CultureInfo.InvariantCulture); y.Text = b.ToString("R", CultureInfo.InvariantCulture); scale.Text = c.ToString("R", CultureInfo.InvariantCulture); }
        _canvas.MoveOffset = (a, b) => Set(a, b, model.Scale);
        _canvas.ChangeScale = delta => Set(model.OffsetX, model.OffsetY, Math.Max(.01f, model.Scale + delta));
        f.Check("Game guide lines", true, b => { _canvas.Guides = b; _canvas.InvalidateVisual(); }, markDirty: false);
        f.Check("Game silhouette", true, b => { _canvas.Silhouette = b; _canvas.InvalidateVisual(); }, markDirty: false);
        f.Panel.Children.Add(Ui.Row(Ui.Button("Reset", () => Set(0, 0, 1)), Ui.Button("Restore opened values", () => Set(original.OffsetX, original.OffsetY, original.Scale))));
        f.Note("NPC mode mirrors the portrait and its X offset as the game does. Preview aspect ratio matters; the game's text and backdrop are simplified here.");
        Content = Ui.Split(Ui.Scroll(f.Panel), _canvas, 290);
        AttachedToVisualTree += (_, _) => _canvas.Refresh();
    }
    public void Dispose() => _canvas.Dispose();
}

internal sealed class PortraitCanvas : Control, IDisposable
{
    private readonly PlayerDefinitionModel _model;
    private Bitmap? _portrait, _silhouette, _guides;
    private byte[]? _lastData;
    private Point? _drag;
    private System.Drawing.PointF _dragOffset;
    private float _previewScale = 1;
    internal bool Npc, Guides = true, Silhouette = true;
    internal float Aspect = 16f / 9;
    internal Action<float, float>? MoveOffset;
    internal Action<float>? ChangeScale;
    internal PortraitCanvas(PlayerDefinitionModel model)
    {
        _model = model; ClipToBounds = true; MinHeight = 280; Focusable = true;
        _silhouette = LoadGuide("silhouette.png"); _guides = LoadGuide("rule-of-thirds.png"); Refresh();
        PointerPressed += (_, e) => { if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; Focus(); _drag = e.GetPosition(this); _dragOffset = new(model.OffsetX, model.OffsetY); e.Pointer.Capture(this); e.Handled = true; };
        PointerMoved += (_, e) => { if (_drag is not { } start) return; Point p = e.GetPosition(this); var offset = PortraitLayout.DragOffset(_dragOffset, new((float)(p.X - start.X), (float)(p.Y - start.Y)), _previewScale, Npc); MoveOffset?.Invoke(offset.X, offset.Y); };
        PointerReleased += (_, e) => { _drag = null; e.Pointer.Capture(null); };
        PointerCaptureLost += (_, _) => _drag = null;
        PointerWheelChanged += (_, e) => { ChangeScale?.Invoke((float)e.Delta.Y * .01f); e.Handled = true; };
        KeyDown += (_, e) =>
        {
            float step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            var delta = e.Key switch { Key.Left => new Point(-step, 0), Key.Right => new Point(step, 0), Key.Up => new Point(0, step), Key.Down => new Point(0, -step), _ => default };
            if (delta == default) return; MoveOffset?.Invoke(_model.OffsetX + (float)delta.X * (Npc ? -1 : 1), _model.OffsetY + (float)delta.Y); e.Handled = true;
        };
    }
    private static Bitmap? LoadGuide(string file) { using Stream? stream = typeof(PortraitCanvas).Assembly.GetManifestResourceStream("Guides." + file); return stream is null ? null : new Bitmap(stream); }
    internal void Refresh()
    {
        byte[]? data = _model.Images.FirstOrDefault(i => i.Category == PlayerImageCategory.Fixed && i.Name == "Dialog")?.Data;
        if (ReferenceEquals(data, _lastData)) return; _lastData = data; _portrait?.Dispose(); _portrait = null;
        try { if (data is not null) _portrait = ImageEditor.Decode(data); } catch (Exception e) when (e is InvalidDataException or ArgumentException) { }
        InvalidateVisual();
    }
    public override void Render(DrawingContext dc)
    {
        base.Render(dc); Refresh(); dc.FillRectangle(Brush.Parse("#0C1216"), Bounds.WithX(0).WithY(0));
        float h = PortraitLayout.CanvasHeight(Aspect); _previewScale = (float)Math.Min(Bounds.Width / PortraitLayout.CanvasWidth, Bounds.Height / h);
        if (_previewScale <= 0) return;
        double left = (Bounds.Width - PortraitLayout.CanvasWidth * _previewScale) / 2, top = (Bounds.Height - h * _previewScale) / 2;
        using (dc.PushClip(new Rect(left, top, PortraitLayout.CanvasWidth * _previewScale, h * _previewScale)))
        using (dc.PushTransform(Matrix.CreateScale(_previewScale, _previewScale) * Matrix.CreateTranslation(left, top)))
        {
            dc.FillRectangle(Brush.Parse("#273137"), new Rect(0, 0, PortraitLayout.CanvasWidth, h));
            dc.FillRectangle(Brush.Parse("#161E24"), new Rect(0, h - 180, PortraitLayout.CanvasWidth, 180));
            if (Silhouette && !Npc && _silhouette is not null) Draw(dc, _silhouette, PortraitLayout.Silhouette(new(_silhouette.PixelSize.Width, _silhouette.PixelSize.Height), h));
            if (_portrait is not null && float.IsFinite(_model.Scale + _model.OffsetX + _model.OffsetY)) Draw(dc, _portrait, PortraitLayout.Portrait(new(_portrait.PixelSize.Width, _portrait.PixelSize.Height), h, _model.OffsetX, _model.OffsetY, _model.Scale, Npc));
            if (Guides && _guides is not null) { var r = PortraitLayout.Guides(h); dc.DrawImage(_guides, new Rect(r.X, r.Y, r.Width, r.Height)); }
        }
    }
    private static void Draw(DrawingContext dc, Bitmap image, PortraitPlacement p)
    {
        var b = p.Bounds; if (b.Width <= 0 || b.Height <= 0 || !float.IsFinite(b.X + b.Y + b.Width + b.Height)) return;
        double sx = p.FlipX ? -1 : 1, sy = p.FlipY ? -1 : 1;
        using (dc.PushTransform(Matrix.CreateScale(sx, sy) * Matrix.CreateTranslation(p.FlipX ? b.Right : b.Left, p.FlipY ? b.Bottom : b.Top))) dc.DrawImage(image, new Rect(0, 0, b.Width, b.Height));
    }
    public void Dispose() { _portrait?.Dispose(); _portrait = null; _silhouette?.Dispose(); _guides?.Dispose(); }
}
