using System.Drawing.Drawing2D;
using SilverpineSilcEditor.Core;

namespace SilverpineSilcEditor.UI;

internal sealed class RoutineMapCanvas : Control
{
    private const int ChunkSize = 32, PixelsPerTile = 16, CacheCapacity = 24;
    private SavedTileMap? _map;
    private GameArtwork? _art;
    private readonly HashSet<Int2> _occupiedChunks = [];
    private readonly Dictionary<Int2, Bitmap> _cache = [];
    private readonly LinkedList<Int2> _recent = [];
    private float _zoom = 10;
    private double _centerX, _centerY;
    private Point? _panStart;
    private double _panX, _panY;
    internal IReadOnlyList<Int2> Waypoints = [];
    internal IReadOnlyList<(string Name, Int2 Position)> RoutineMarkers = [];
    internal Int2 Destination;
    internal int SelectedWaypoint = -1;
    internal event Action<Int2>? TileClicked;
    internal event Action<Int2, MapCell?>? HoverTile;
    internal int CachedChunkCount => _cache.Count;

    internal RoutineMapCanvas()
    {
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.Background;
        TabStop = true;
        AccessibleName = "Routine tile map";
        AccessibleDescription = "Click a tile to choose a coordinate. Right-drag to pan. Scroll to zoom. Coordinate fields are also available.";
    }

    internal void SetMap(SavedTileMap map)
    {
        _map = map;
        _occupiedChunks.Clear();
        foreach (Int2 point in map.Cells.Keys) _occupiedChunks.Add(new Int2(Chunk(point.X), Chunk(point.Y)));
        ClearCache();
        Invalidate();
    }
    internal void SetArtwork(GameArtwork? art) { _art = art; ClearCache(); Invalidate(); }
    internal void CenterOn(Int2 point) { _centerX = point.X; _centerY = point.Y; Invalidate(); }
    private static int Chunk(int coordinate) => (int)Math.Floor(coordinate / (double)ChunkSize);
    internal PointF WorldToScreen(double x, double y) => new((float)((x - _centerX) * _zoom + Width / 2d), (float)((_centerY - y) * _zoom + Height / 2d));
    internal Int2 ScreenToTile(Point point) => new((int)Math.Floor(_centerX + (point.X - Width / 2d) / _zoom + .5),
        (int)Math.Floor(_centerY - (point.Y - Height / 2d) / _zoom + .5));

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        if (_map is null)
        {
            TextRenderer.DrawText(g, "Load a .sav to inspect tiles and place routine coordinates.\nYour save is used as a read-only reference.",
                Font, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        Int2 upperLeft = ScreenToTile(Point.Empty), lowerRight = ScreenToTile(new Point(Width, Height));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        for (int cy = Chunk(lowerRight.Y); cy <= Chunk(upperLeft.Y); cy++)
        for (int cx = Chunk(upperLeft.X); cx <= Chunk(lowerRight.X); cx++)
        {
            var key = new Int2(cx, cy);
            if (!_occupiedChunks.Contains(key)) continue;
            PointF top = WorldToScreen(cx * ChunkSize - .5, (cy + 1) * ChunkSize - .5);
            g.DrawImage(GetChunk(key), new RectangleF(top.X, top.Y, ChunkSize * _zoom, ChunkSize * _zoom));
        }
        if (_zoom >= 16)
        {
            using var pen = new Pen(Color.FromArgb(45, 230, 245, 235));
            for (int x = upperLeft.X; x <= lowerRight.X; x++)
            {
                float sx = WorldToScreen(x - .5, 0).X;
                g.DrawLine(pen, sx, 0, sx, Height);
            }
            for (int y = lowerRight.Y; y <= upperLeft.Y; y++)
            {
                float sy = WorldToScreen(0, y - .5).Y;
                g.DrawLine(pen, 0, sy, Width, sy);
            }
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var marker in RoutineMarkers)
        {
            PointF point = WorldToScreen(marker.Position.X, marker.Position.Y);
            if (!ClientRectangle.Contains(Point.Round(point))) continue;
            using var brush = new SolidBrush(Color.FromArgb(190, 157, 178, 183));
            g.FillEllipse(brush, point.X - 3, point.Y - 3, 6, 6);
        }
        if (Waypoints.Count > 1)
        {
            using var line = new Pen(Color.FromArgb(220, Theme.Accent), 2) { DashStyle = DashStyle.Dash };
            // These are ordering guides, NOT the game's collision-aware path.
            for (int i = 1; i < Waypoints.Count; i++)
                g.DrawLine(line, WorldToScreen(Waypoints[i - 1].X, Waypoints[i - 1].Y), WorldToScreen(Waypoints[i].X, Waypoints[i].Y));
        }
        for (int i = 0; i < Waypoints.Count; i++) DrawMarker(g, Waypoints[i], (i + 1).ToString(), i == SelectedWaypoint ? Color.White : Theme.Accent);
        DrawMarker(g, Destination, "D", Color.FromArgb(250, 190, 94));
        TextRenderer.DrawText(g, $"{_zoom:0.#} px/tile  •  +Y north  •  right-drag to pan / wheel to zoom",
            Font, new Rectangle(12, Height - 30, Width - 24, 25), Color.White, Color.FromArgb(35, 48, 54), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    private void DrawMarker(Graphics g, Int2 coordinate, string label, Color color)
    {
        PointF point = WorldToScreen(coordinate.X, coordinate.Y);
        if (point.X < -30 || point.Y < -30 || point.X > Width + 30 || point.Y > Height + 30) return;
        using var fill = new SolidBrush(Theme.Background);
        using var outline = new Pen(color, 2);
        g.FillEllipse(fill, point.X - 11, point.Y - 11, 22, 22);
        g.DrawEllipse(outline, point.X - 11, point.Y - 11, 22, 22);
        TextRenderer.DrawText(g, label, Theme.EmphasisFont, Rectangle.Round(new RectangleF(point.X - 14, point.Y - 11, 28, 22)), color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private Bitmap GetChunk(Int2 key)
    {
        if (_cache.TryGetValue(key, out Bitmap? cached)) { _recent.Remove(key); _recent.AddLast(key); return cached; }
        if (_cache.Count >= CacheCapacity)
        {
            Int2 oldest = _recent.First!.Value;
            _cache[oldest].Dispose(); _cache.Remove(oldest); _recent.RemoveFirst();
        }
        var bitmap = new Bitmap(ChunkSize * PixelsPerTile, ChunkSize * PixelsPerTile);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            int left = key.X * ChunkSize, bottom = key.Y * ChunkSize;
            for (int y = bottom; y < bottom + ChunkSize; y++)
            for (int x = left; x < left + ChunkSize; x++)
            {
                if (!_map!.Cells.TryGetValue(new Int2(x, y), out MapCell? cell)) continue;
                using var brush = new SolidBrush(TerrainColor(cell.Terrain));
                g.FillRectangle(brush, (x - left) * PixelsPerTile, (bottom + ChunkSize - 1 - y) * PixelsPerTile, PixelsPerTile, PixelsPerTile);
                if (_art is null && cell.Feature is { } feature)
                {
                    using var marker = new SolidBrush(feature.Contains("wall") ? Color.SlateGray : feature.Contains("tree") ? Color.DarkOliveGreen : Color.Tan);
                    g.FillRectangle(marker, (x - left) * PixelsPerTile + 3, (bottom + ChunkSize - 1 - y) * PixelsPerTile + 3, PixelsPerTile - 6, PixelsPerTile - 6);
                }
            }
            if (_art is not null)
            {
                var placements = new List<MapPlacement>();
                // Include neighboring anchors so a sprite crossing a chunk edge is not cut off.
                for (int y = bottom - 32; y < bottom + ChunkSize + 32; y++)
                for (int x = left - 32; x < left + ChunkSize + 32; x++)
                    if (_map!.Cells.TryGetValue(new Int2(x, y), out MapCell? cell)) placements.AddRange(cell.Placements);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                foreach (MapPlacement placement in placements.OrderBy(p => p.Prefab.StartsWith("prefab_tile_", StringComparison.Ordinal) ? 0 : 1).ThenByDescending(p => p.Y))
                {
                    var loaded = _art.Get(placement.Prefab);
                    if (loaded is not { } sprite) continue;
                    var art = sprite.Art;
                    var rect = new RectangleF((placement.X + art.Left - left + .5f) * PixelsPerTile,
                        (bottom + ChunkSize - .5f - placement.Y - art.Bottom - art.Height) * PixelsPerTile,
                        art.Width * PixelsPerTile, art.Height * PixelsPerTile);
                    if (rect.IntersectsWith(new RectangleF(0, 0, bitmap.Width, bitmap.Height))) g.DrawImage(sprite.Image, rect);
                }
            }
        }
        _cache.Add(key, bitmap); _recent.AddLast(key);
        return bitmap;
    }

    private static Color TerrainColor(string terrain) => terrain.Contains("water") ? Color.FromArgb(40, 93, 121)
        : terrain.Contains("wood") ? Color.FromArgb(127, 99, 68) : terrain.Contains("rock") ? Color.FromArgb(100, 109, 110)
        : terrain.Contains("sand") ? Color.FromArgb(154, 145, 101) : terrain.Contains("dirt") ? Color.FromArgb(101, 89, 68)
        : terrain.Contains("snow") ? Color.FromArgb(181, 205, 210) : terrain.Length == 0 ? Theme.Surface : Color.FromArgb(66, 101, 72);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); Focus();
        if (e.Button is MouseButtons.Right or MouseButtons.Middle)
        {
            _panStart = e.Location; _panX = _centerX; _panY = _centerY; Capture = true;
        }
        else if (e.Button == MouseButtons.Left && _map is not null) TileClicked?.Invoke(ScreenToTile(e.Location));
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panStart is { } start)
        {
            _centerX = _panX - (e.X - start.X) / _zoom; _centerY = _panY + (e.Y - start.Y) / _zoom; Invalidate();
        }
        Int2 point = ScreenToTile(e.Location);
        MapCell? cell = null;
        _map?.Cells.TryGetValue(point, out cell);
        HoverTile?.Invoke(point, cell);
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _panStart = null; Capture = false; }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) _panStart = null; }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        double worldX = _centerX + (e.X - Width / 2d) / _zoom, worldY = _centerY - (e.Y - Height / 2d) / _zoom;
        _zoom = Math.Clamp(_zoom * (float)Math.Pow(1.25, e.Delta / 120d), .5f, 48);
        _centerX = worldX - (e.X - Width / 2d) / _zoom; _centerY = worldY + (e.Y - Height / 2d) / _zoom;
        Invalidate();
    }
    private void ClearCache() { foreach (Bitmap bitmap in _cache.Values) bitmap.Dispose(); _cache.Clear(); _recent.Clear(); }
    protected override void Dispose(bool disposing) { if (disposing) ClearCache(); base.Dispose(disposing); }
}
