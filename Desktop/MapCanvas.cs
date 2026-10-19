using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SilverpineSilcEditor.Core;
using System.Globalization;

namespace SilverpineSilcEditor.Desktop;

internal sealed record RoutineMapPoint(int RoutineIndex, int WaypointIndex, Int2 Position, string Label)
{
    public override string ToString() => Label;
}

internal sealed class MapCanvas : Control, IDisposable
{
    private SavedTileMap? _map;
    private GameAssetSource? _art;
    private readonly Dictionary<(int X, int Y, bool Coarse), RenderTargetBitmap> _chunks = [];
    private readonly LinkedList<(int X, int Y, bool Coarse)> _recent = [];
    private readonly Dictionary<string, Bitmap> _images = [];
    private readonly Queue<string> _imageQueue = [];
    private readonly HashSet<string> _badImages = [];
    private long _imageBytes;
    private readonly Dictionary<Int2, List<(Int2 Position, MapCell Cell)>> _cells = [];
    private readonly Dictionary<Int2, MapCell> _displayCells = [];
    private Point? _panStart;
    private double _panX, _panY, _centerX, _centerY, _pixels = 14;
    private bool _hideVegetation;
    private bool _ambientShadows = true;
    internal bool AmbientShadows { get => _ambientShadows; set { if (_ambientShadows == value) return; _ambientShadows = value; ClearChunks(); InvalidateVisual(); } }
    internal bool HideVegetation { get => _hideVegetation; set { if (_hideVegetation == value) return; _hideVegetation = value; ClearChunks(); InvalidateVisual(); } }
    internal static bool IsVegetation(string prefab) => prefab.StartsWith("prefab_tree_", StringComparison.Ordinal)
        || prefab == "prefab_plant_grass" || prefab.StartsWith("prefab_overgrowth_", StringComparison.Ordinal);
    internal List<Int2> Waypoints { get; set; } = [];
    internal List<Int2> OtherDestinations { get; set; } = [];
    internal List<RoutineMapPoint> RoutinePoints { get; set; } = [];
    internal int SelectedRoutine = -1;
    internal bool SelectRoutineMode;
    internal Action<IReadOnlyList<RoutineMapPoint>>? RoutinePointsClicked;
    internal Int2 Destination;
    internal int SelectedWaypoint = -1;
    internal Action<Int2>? TileClicked;
    internal Action<Int2, MapCell?>? Hover;
    internal int CachedChunkCount => _chunks.Count;
    internal MapCanvas()
    {
        ClipToBounds = true; Focusable = true; MinHeight = 260;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        PointerWheelChanged += (_, e) => { Zoom(Math.Pow(1.18, e.Delta.Y), e.GetPosition(this)); e.Handled = true; };
        PointerPressed += (_, e) =>
        {
            Focus(); var properties = e.GetCurrentPoint(this).Properties;
            if (properties.IsRightButtonPressed || properties.IsMiddleButtonPressed) { _panStart = e.GetPosition(this); _panX = _centerX; _panY = _centerY; e.Pointer.Capture(this); }
            else if (properties.IsLeftButtonPressed)
            {
                if (SelectRoutineMode) RoutinePointsClicked?.Invoke(HitTestRoutinePoints(e.GetPosition(this)));
                else TileClicked?.Invoke(ScreenToTile(e.GetPosition(this)));
            }
            e.Handled = true;
        };
        PointerMoved += (_, e) => { Point p = e.GetPosition(this); if (_panStart is { } start) { _centerX = _panX - (p.X - start.X) / _pixels; _centerY = _panY + (p.Y - start.Y) / _pixels; InvalidateVisual(); } Int2 tile = ScreenToTile(p); Hover?.Invoke(tile, _displayCells.GetValueOrDefault(tile)); };
        PointerReleased += (_, e) => { _panStart = null; e.Pointer.Capture(null); };
        PointerCaptureLost += (_, _) => _panStart = null;
    }
    internal void SetMap(SavedTileMap map) { _map = map; _art = null; ClearChunks(); ClearImages(); IndexCells(); InvalidateVisual(); }
    internal void SetArt(GameAssetSource artwork) { _art = artwork; ClearChunks(); ClearImages(); IndexCells(); InvalidateVisual(); }
    private void IndexCells()
    {
        _cells.Clear(); _displayCells.Clear(); if (_map is null) return;
        foreach (var pair in _map.Cells) _displayCells[pair.Key] = pair.Value;
        var copied = new HashSet<Int2>();
        foreach (var placement in _art?.StaticPlacements ?? [])
        {
            var pos = new Int2((int)MathF.Round(placement.X), (int)MathF.Round(placement.Y));
            // Only supplement an already saved/visible tile; do not reveal or
            // generate unvisited regions. Never mutate the read-only save model.
            if (!_displayCells.TryGetValue(pos, out var cell) || cell.Placements.Contains(placement)) continue;
            if (copied.Add(pos)) { var copy = new MapCell { Zone = cell.Zone }; copy.Objects.AddRange(cell.Objects); copy.Placements.AddRange(cell.Placements); _displayCells[pos] = cell = copy; }
            cell.Objects.Add(placement.Prefab); cell.Placements.Add(placement);
        }
        foreach (var pair in _displayCells) { var key = new Int2(Floor(pair.Key.X, 32), Floor(pair.Key.Y, 32)); if (!_cells.TryGetValue(key, out var bucket)) _cells[key] = bucket = []; bucket.Add((pair.Key, pair.Value)); }
    }
    internal void Center(Int2 tile) { _centerX = tile.X; _centerY = tile.Y; InvalidateVisual(); }
    internal void FramePoints(IEnumerable<Int2> points)
    {
        Int2[] all = points.ToArray(); if (all.Length == 0) return;
        double minX = all.Min(p => p.X), maxX = all.Max(p => p.X), minY = all.Min(p => p.Y), maxY = all.Max(p => p.Y);
        _centerX = (minX + maxX) / 2; _centerY = (minY + maxY) / 2;
        _pixels = Math.Clamp(Math.Min(Math.Max(1, Bounds.Width - 80) / (maxX - minX + 4), Math.Max(1, Bounds.Height - 80) / (maxY - minY + 4)), .75, 60);
        InvalidateVisual();
    }
    internal IReadOnlyList<RoutineMapPoint> HitTestRoutinePoints(Point point)
    {
        var hits = RoutinePoints.Select(p => { Point delta = TileToScreen(p.Position) - point; return (Point: p, Distance: Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y)); }).Where(p => p.Distance <= 12).OrderBy(p => p.Distance).ToArray();
        return hits.Length == 0 ? [] : hits.Where(p => p.Distance <= hits[0].Distance + 2).Select(p => p.Point).ToArray();
    }
    internal void Zoom(double factor, Point? anchor = null)
    {
        Point at = anchor ?? new Point(Bounds.Width / 2, Bounds.Height / 2); double wx = _centerX + (at.X - Bounds.Width / 2) / _pixels, wy = _centerY - (at.Y - Bounds.Height / 2) / _pixels;
        _pixels = Math.Clamp(_pixels * factor, .75, 60); _centerX = wx - (at.X - Bounds.Width / 2) / _pixels; _centerY = wy + (at.Y - Bounds.Height / 2) / _pixels; InvalidateVisual();
    }
    internal Int2 ScreenToTile(Point p) => new((int)Math.Floor(_centerX + (p.X - Bounds.Width / 2) / _pixels + .5), (int)Math.Floor(_centerY - (p.Y - Bounds.Height / 2) / _pixels + .5));
    internal Point TileToScreen(Int2 p) => new(Bounds.Width / 2 + (p.X - _centerX) * _pixels, Bounds.Height / 2 - (p.Y - _centerY) * _pixels);
    private static int Floor(int n, int divisor) => (int)Math.Floor((double)n / divisor);
    public override void Render(DrawingContext dc)
    {
        base.Render(dc); dc.FillRectangle(Brush.Parse("#0C151B"), new Rect(Bounds.Size));
        Int2 upper = ScreenToTile(new(0, 0)), lower = ScreenToTile(new(Bounds.Width, Bounds.Height));
        bool coarse = _pixels < 6; int size = coarse ? 128 : 32;
        if (_map is not null)
            for (int cy = Floor(lower.Y, size); cy <= Floor(upper.Y, size); cy++)
            for (int cx = Floor(upper.X, size); cx <= Floor(lower.X, size); cx++)
            {
                bool occupied = coarse ? Enumerable.Range(0, 4).Any(dx => Enumerable.Range(0, 4).Any(dy => _cells.ContainsKey(new(cx * 4 + dx, cy * 4 + dy)))) : _cells.ContainsKey(new(cx, cy));
                if (!occupied) continue;
                var image = Chunk(cx, cy, coarse); Point p = TileToScreen(new(cx * size, cy * size + size - 1));
                dc.DrawImage(image, new Rect(p.X - _pixels / 2, p.Y - _pixels / 2, size * _pixels, size * _pixels));
            }
        if (_pixels >= 18)
        {
            var pen = new Pen(Brush.Parse("#354B5966"), .6);
            for (int x = upper.X; x <= lower.X + 1; x++) { double px = TileToScreen(new(x, 0)).X - _pixels / 2; dc.DrawLine(pen, new(px, 0), new(px, Bounds.Height)); }
            for (int y = lower.Y; y <= upper.Y + 1; y++) { double py = TileToScreen(new(0, y)).Y + _pixels / 2; dc.DrawLine(pen, new(0, py), new(Bounds.Width, py)); }
        }
        foreach (var tile in OtherDestinations) Marker(dc, tile, "", Ui.Muted, 3);
        foreach (var point in RoutinePoints.Where(p => p.RoutineIndex != SelectedRoutine))
        {
            Marker(dc, point.Position, point.WaypointIndex < 0 ? "D" : "", Ui.Muted, point.WaypointIndex < 0 ? 7 : 4);
            if (_pixels >= 18 && point.WaypointIndex < 0) PointLabel(dc, point, Ui.Muted);
        }
        for (int i = 1; i < Waypoints.Count; i++) dc.DrawLine(new Pen(Ui.Accent, 2, DashStyle.Dash), TileToScreen(Waypoints[i - 1]), TileToScreen(Waypoints[i]));
        for (int i = 0; i < Waypoints.Count; i++) Marker(dc, Waypoints[i], (i + 1).ToString(), i == SelectedWaypoint ? Brushes.White : Ui.Accent, 9);
        Marker(dc, Destination, "D", Brush.Parse("#FFCE88"), 11);
        if (RoutinePoints.FirstOrDefault(p => p.RoutineIndex == SelectedRoutine && p.WaypointIndex < 0) is { } selected) PointLabel(dc, selected, Brush.Parse("#FFCE88"));
        if (_map is null) dc.DrawText(Label("Load a save for terrain, or enter coordinates on the right.", Ui.Muted), new(24, 24));
        else if (coarse) dc.DrawText(Label("Overview · zoom in for game sprites", Ui.Muted), new(18, 16));
    }
    private void PointLabel(DrawingContext dc, RoutineMapPoint point, IBrush color)
    {
        Point p = TileToScreen(point.Position); if (p.X < 0 || p.Y < 0 || p.X > Bounds.Width || p.Y > Bounds.Height) return;
        string name = point.Label.Replace(" · destination", "", StringComparison.Ordinal); if (name.Length > 30) name = name[..29] + "…";
        var label = Label(name, color); var rect = new Rect(p.X + 13, p.Y - label.Height / 2 - 3, label.Width + 8, label.Height + 6);
        dc.FillRectangle(Ui.Background, rect); dc.DrawText(label, new(rect.X + 4, rect.Y + 3));
    }
    private static FormattedText Label(string text, IBrush color) => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, color);
    private void Marker(DrawingContext dc, Int2 tile, string label, IBrush color, double radius)
    {
        Point p = TileToScreen(tile); if (p.X < -50 || p.Y < -50 || p.X > Bounds.Width + 50 || p.Y > Bounds.Height + 50) return;
        dc.DrawEllipse(Ui.Background, new Pen(color, 2), p, radius, radius); if (label.Length > 0) { var text = Label(label, color); dc.DrawText(text, new(p.X - text.Width / 2, p.Y - text.Height / 2)); }
    }
    private IEnumerable<(Int2 Position, MapCell Cell)> InArea(int left, int bottom, int right, int top)
    {
        for (int cy = Floor(bottom, 32); cy <= Floor(top, 32); cy++) for (int cx = Floor(left, 32); cx <= Floor(right, 32); cx++)
            if (_cells.TryGetValue(new(cx, cy), out var bucket)) foreach (var cell in bucket) if (cell.Position.X >= left && cell.Position.X <= right && cell.Position.Y >= bottom && cell.Position.Y <= top) yield return cell;
    }
    private RenderTargetBitmap Chunk(int cx, int cy, bool coarse)
    {
        var key = (cx, cy, coarse);
        if (_chunks.TryGetValue(key, out var existing)) { _recent.Remove(key); _recent.AddLast(key); return existing; }
        int size = coarse ? 128 : 32, px = coarse ? 2 : 16;
        var bitmap = new RenderTargetBitmap(new PixelSize(size * px, size * px), new Vector(96, 96));
        int left = cx * size, bottom = cy * size;
        using (DrawingContext dc = bitmap.CreateDrawingContext())
        {
            foreach (var (pos, cell) in InArea(left, bottom, left + size - 1, bottom + size - 1))
            {
                var rect = new Rect((pos.X - left) * px, (bottom + size - 1 - pos.Y) * px, px, px);
                dc.FillRectangle(TerrainBrush(cell.Terrain), rect);
                if (cell.Feature is not null && !(HideVegetation && IsVegetation(cell.Feature)) && (coarse || _art is null || !_art.Prefabs.ContainsKey(cell.Feature))) dc.DrawRectangle(Brush.Parse("#C9AE82"), null, rect.Deflate(Math.Max(.2, px * .2)));
            }
            if (!coarse && _art is not null)
            {
                var draws = new List<(MapPlacement Placement, MapArtwork Art, Rect Rect)>();
                foreach (var placement in InArea(left - 64, bottom - 64, left + size + 64, bottom + size + 64).SelectMany(c => c.Cell.Placements))
                {
                    if (HideVegetation && IsVegetation(placement.Prefab) || !_art.Prefabs.TryGetValue(placement.Prefab, out var parts)) continue;
                    foreach (var art in parts)
                    {
                    // Native shadow/fake-AO renderers retain their GLOBAL layer,
                    // rather than being baked over the owning prefab. Dynamic
                    // lighting is never simulated; a flat view is optional.
                    if (art.Shadow && !AmbientShadows) continue;
                    Rect rect = new((placement.X + art.Left - left + .5) * px, (bottom + size - .5 - placement.Y - art.Bottom - art.Height) * px, art.Width * px, art.Height * px);
                    if (!rect.Intersects(new Rect(0, 0, size * px, size * px))) continue;
                    draws.Add((placement, art, rect));
                    }
                }
                foreach (var draw in draws.OrderBy(d => d.Art.Layer).ThenBy(d => d.Art.SortOrder(d.Placement.Y)).ThenByDescending(d => d.Placement.Y))
                {
                    Bitmap? image = GetImage(draw.Art.Path); if (image is not null) dc.DrawImage(image, draw.Rect);
                }
            }
        }
        while (_chunks.Count >= 24) { var oldest = _recent.First!.Value; _recent.RemoveFirst(); _chunks[oldest].Dispose(); _chunks.Remove(oldest); }
        _chunks[key] = bitmap; _recent.AddLast(key); return bitmap;
    }
    private Bitmap? GetImage(string path)
    {
        if (_badImages.Contains(path)) return null; if (_images.TryGetValue(path, out var old)) return old;
        try
        {
            if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new InvalidDataException("Oversized cache image");
            byte[] bytes = File.ReadAllBytes(path); PixelSize size = ImageEditor.PngSize(bytes); if (size.Width > 2048 || size.Height > 2048) throw new InvalidDataException("Oversized cache dimensions");
            long count = (long)size.Width * size.Height * 4;
            while (_imageBytes + count > 32 * 1024 * 1024 && _imageQueue.TryDequeue(out var oldest)) { Bitmap removed = _images[oldest]; _imageBytes -= (long)removed.PixelSize.Width * removed.PixelSize.Height * 4; removed.Dispose(); _images.Remove(oldest); }
            var image = ImageEditor.Decode(bytes); _images[path] = image; _imageQueue.Enqueue(path); _imageBytes += count; return image;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException) { _badImages.Add(path); return null; }
    }
    private static IBrush TerrainBrush(string name) => name.Contains("water") ? Water : name.Contains("wood") ? Wood : name.Contains("rock") || name.Contains("stone") ? Stone : name.Contains("snow") ? Snow : name.Contains("dirt") || name.Contains("sand") ? Dirt : Grass;
    private static readonly IBrush Water = Brush.Parse("#325A75"), Wood = Brush.Parse("#79654D"), Stone = Brush.Parse("#656D70"), Snow = Brush.Parse("#B3C7CA"), Dirt = Brush.Parse("#827457"), Grass = Brush.Parse("#3F5B48");
    private void ClearChunks() { foreach (var image in _chunks.Values) image.Dispose(); _chunks.Clear(); _recent.Clear(); }
    private void ClearImages() { foreach (var image in _images.Values) image.Dispose(); _images.Clear(); _imageQueue.Clear(); _badImages.Clear(); _imageBytes = 0; }
    public void Dispose() { ClearChunks(); ClearImages(); _cells.Clear(); _displayCells.Clear(); }
}
