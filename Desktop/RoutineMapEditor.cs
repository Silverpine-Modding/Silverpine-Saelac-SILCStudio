using Avalonia;
using Avalonia.Controls;
using SilverpineSilcEditor.Core;
using System.Globalization;

namespace SilverpineSilcEditor.Desktop;

// An in-window workspace. All routine edits stay isolated until Apply.
internal sealed class RoutineMapEditor : UserControl, IDisposable
{
    private static SavedTileMap? _sessionMap;
    private static GameAssetSource? _sessionArt;
    private static bool _sessionHideVegetation;
    private static bool _sessionAmbientShadows = true;
    private readonly Action<bool> _complete;
    private readonly MapCanvas _canvas = new();
    private readonly RoutineDraft[] _routines;
    private readonly Dictionary<RoutineDraft, RoutineDetails> _details = [];
    private readonly Dictionary<RoutineDraft, List<TextBlock>> _routineLabels = [];
    private readonly ContentControl _detail = new();
    private readonly EditState _state;
    private readonly ComboBox _routinesList = new() { MinWidth = 240, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly ComboBox _pointChoices = new() { IsVisible = false, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly ComboBox _mode = new() { MinWidth = 210 };
    private readonly ComboBox _regions = new() { MinWidth = 180, MaxWidth = 300, PlaceholderText = "Saved region" };
    private readonly TextBlock _status = Ui.Note("Save and Unity assets are read-only. Coordinates can also be edited without loading a save."), _hover = Ui.Note("");
    private readonly Button _load, _artButton, _apply;
    private readonly CancellationTokenSource _cancellation = new();
    private RoutineDraft _selected;
    private bool _busy, _closed, _refreshing;
    private Window Owner => TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("The map must be hosted inside the editor.");
    internal int SelectedRoutineIndex => Array.IndexOf(_routines, _selected);

    internal RoutineMapEditor(RoutineRecord routine, IReadOnlyList<RoutineRecord> routines, Action<bool> complete)
    {
        _complete = complete; _state = new EditState(RefreshMarkers);
        _routines = routines.Select(r => new RoutineDraft(r)).ToArray();
        _selected = _routines.First(r => ReferenceEquals(r.Original, routine));
        Background = Ui.Background; Padding = new Thickness(16);
        _routinesList.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<RoutineDraft>((r, _) =>
        {
            var label = Ui.Text(r?.Label ?? "");
            if (r is not null) { if (!_routineLabels.TryGetValue(r, out var labels)) _routineLabels[r] = labels = []; labels.Add(label); }
            return label;
        });
        _routinesList.ItemsSource = _routines;
        _routinesList.SelectionChanged += (_, _) => { if (!_refreshing && _routinesList.SelectedItem is RoutineDraft r) SelectRoutine(r); };
        _pointChoices.SelectionChanged += (_, _) => { if (!_refreshing && _pointChoices.SelectedItem is RoutineMapPoint p) SelectPoint(p); };
        _mode.SelectionChanged += (_, _) => _canvas.SelectRoutineMode = _mode.SelectedIndex == 0;
        _load = Ui.AsyncButton("Load save…", LoadSave); _artButton = Ui.AsyncButton("Load game artwork…", LoadArtwork);
        _apply = Ui.Button("Apply routine changes", ApplyDraft, true);
        _regions.SelectionChanged += (_, _) => { if (_regions.SelectedItem is MapRegion r) _canvas.Center(r.Center); };
        var vegetation = new CheckBox { Content = "Hide trees & grass", IsChecked = _sessionHideVegetation };
        ToolTip.SetTip(vegetation, "Hide trees and grass/overgrowth objects. Ground tiles stay visible. This only changes the map preview.");
        _canvas.HideVegetation = _sessionHideVegetation;
        vegetation.IsCheckedChanged += (_, _) => _canvas.HideVegetation = _sessionHideVegetation = vegetation.IsChecked == true;
        var shadows = new CheckBox { Content = "Ambient shadows", IsChecked = _sessionAmbientShadows };
        ToolTip.SetTip(shadows, "Native shadow and sprite_fakeao overlays, sorted globally below walls/furniture. Disable for a flat planning view. No time-of-day lighting is simulated.");
        _canvas.AmbientShadows = _sessionAmbientShadows;
        shadows.IsCheckedChanged += (_, _) => _canvas.AmbientShadows = _sessionAmbientShadows = shadows.IsChecked == true;
        var toolbar = Ui.Row(_load, _artButton, _regions, _mode, Ui.Button("Show all points", () => _canvas.FramePoints(_canvas.RoutinePoints.Select(p => p.Position))), Ui.Button("−", () => _canvas.Zoom(1 / 1.4)), Ui.Button("+", () => _canvas.Zoom(1.4)), vegetation, shadows);
        _canvas.Hover = (p, cell) =>
        {
            var points = _canvas.RoutinePoints.Where(r => r.Position == p).Select(r => r.Label).Take(5).ToArray();
            _hover.Text = $"X {p.X} · Y {p.Y}  |  " + (points.Length > 0 ? string.Join(" · ", points) : cell is null ? "No saved tile" : (cell.Zone is null ? "" : cell.Zone + " · ") + string.Join(", ", cell.Objects.Take(5)));
        };
        _canvas.RoutinePointsClicked = SelectPoints;
        _canvas.TileClicked = p =>
        {
            RoutineDetails detail = _details[_selected];
            if (_mode.SelectedIndex == 1) detail.SetDestination(p);
            else if (_mode.SelectedIndex == 2) detail.AddPoint(p);
            else if (_mode.SelectedIndex == 3) detail.MovePoint(p);
        };
        var mapPanel = Ui.Dock(toolbar, _canvas, Ui.Stack(_hover, Ui.Note("Select routine: click a marker · placement modes: click a tile · right/middle-drag: pan · wheel: zoom")));
        var sidebar = Ui.Dock(Ui.Stack(Ui.Text("Routine", 20, Ui.Accent), _routinesList, _pointChoices), _detail);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,380") }; grid.Children.Add(mapPanel); Grid.SetColumn(sidebar, 2); grid.Children.Add(sidebar);
        Content = Ui.Dock(Ui.Stack(Ui.Text("Routine map", 24, Ui.Accent), _status), grid,
            Ui.Stack(Ui.Note("All points belong to this NPC. Apply keeps edits to every routine; Cancel discards this map session. Use Save changes afterward to write the SILC."),
                Ui.Row(Ui.Button("Cancel / return to routines", () => _complete(false)), _apply)));
        SelectRoutine(_selected); _canvas.Center(new(routine.TargetX, routine.TargetY));
        if (_sessionMap is not null) { _canvas.SetMap(_sessionMap); _regions.ItemsSource = _sessionMap.Regions; if (_sessionArt is not null) _canvas.SetArt(_sessionArt); _status.Text = Path.GetFileName(_sessionMap.SourcePath) + " · " + _sessionMap.Cells.Count.ToString("N0") + " saved tiles"; }
    }
    private void SelectRoutine(RoutineDraft routine)
    {
        _selected = routine;
        if (!_details.TryGetValue(routine, out var panel)) _details[routine] = panel = new RoutineDetails(routine, _state, _canvas, ComponentsChanged);
        _detail.Content = panel; _refreshing = true; _routinesList.SelectedItem = routine; _refreshing = false;
        RefreshModes(); RefreshMarkers();
    }
    private void ComponentsChanged() { _details[_selected].RefreshPatrol(); RefreshModes(); RefreshMarkers(); }
    private void RefreshModes()
    {
        string? old = _mode.SelectedItem as string;
        string[] modes = _selected.Patrol is null ? ["Select routine", "Set destination"] : ["Select routine", "Set destination", "Add waypoint", "Move selected waypoint"];
        _mode.ItemsSource = modes; _mode.SelectedIndex = Math.Max(0, Array.IndexOf(modes, old));
        _canvas.SelectRoutineMode = _mode.SelectedIndex == 0;
    }
    internal void SelectPoints(IReadOnlyList<RoutineMapPoint> points)
    {
        _refreshing = true; _pointChoices.ItemsSource = points; _pointChoices.SelectedIndex = -1; _pointChoices.IsVisible = points.Count > 1; _refreshing = false;
        if (points.Count == 1) SelectPoint(points[0]);
        else if (points.Count > 1) { _pointChoices.PlaceholderText = $"{points.Count} overlapping points — choose one"; _pointChoices.IsDropDownOpen = true; }
    }
    private void SelectPoint(RoutineMapPoint point)
    {
        SelectRoutine(_routines[point.RoutineIndex]); _details[_selected].SelectWaypoint(point.WaypointIndex);
    }
    private void RefreshMarkers()
    {
        if (_closed) return;
        var points = new List<RoutineMapPoint>();
        for (int i = 0; i < _routines.Length; i++)
        {
            RoutineDraft r = _routines[i]; points.Add(new(i, -1, new(r.Working.TargetX, r.Working.TargetY), r.Label + " · destination"));
            if (r.Patrol is not null) for (int p = 0; p < r.Points.Count; p++) points.Add(new(i, p, r.Points[p], r.Label + " · waypoint " + (p + 1)));
        }
        _canvas.RoutinePoints = points; _canvas.SelectedRoutine = SelectedRoutineIndex;
        _canvas.Destination = new(_selected.Working.TargetX, _selected.Working.TargetY);
        _canvas.Waypoints = _selected.Patrol is not null ? _selected.Points : [];
        _canvas.SelectedWaypoint = _details.TryGetValue(_selected, out var detail) ? detail.SelectedWaypoint : -1;
        foreach (var (routine, labels) in _routineLabels) foreach (var label in labels) label.Text = routine.Label;
        _canvas.InvalidateVisual();
    }
    private void ApplyDraft()
    {
        if (_closed || _busy) return;
        try
        {
            _state.Validate();
            // Validate every routine before changing any original record.
            foreach (var r in _routines) r.Flush();
            var changed = _routines.Where(r => !r.Working.CreateDraft().Storage.ToBytes().AsSpan().SequenceEqual(r.Before)).ToArray();
            foreach (var r in changed) r.Original.AcceptDraft(r.Working);
            _complete(changed.Length > 0);
        }
        catch (InvalidDataException e) { _status.Text = "Not applied: " + e.Message; }
    }
    private void Busy(bool value) { _busy = value; _load.IsEnabled = _artButton.IsEnabled = _apply.IsEnabled = !value; }
    private async Task LoadSave()
    {
        if (_busy || _closed) return; string? path = await Ui.PickFile(Owner, "sav", "Choose a Silverpine save (read-only)"); if (path is null || _closed) return;
        Busy(true); _status.Text = "Reading saved tile data…";
        bool loaded = false;
        try
        {
            SavedTileMap map = await Task.Run(() => SavedTileMap.Load(path, _cancellation.Token)); if (_closed) return;
            _sessionMap = map; _sessionArt = null; _canvas.SetMap(map); _regions.ItemsSource = map.Regions; loaded = true;
            _status.Text = $"{Path.GetFileName(path)} · {map.Cells.Count:N0} saved tiles · {map.Regions.Count} regions. Only saved areas are shown." + (map.Warnings.Count > 0 ? " " + string.Join(" ", map.Warnings) : "");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!_closed) _status.Text = "Could not load save: " + e.Message; }
        finally { if (!_closed) Busy(false); }
        if (!_closed && loaded && EditorPaths.FindGame() is not null) await LoadArtwork();
    }
    private async Task LoadArtwork()
    {
        if (_busy || _closed) return; if (_sessionMap is null) { _status.Text = "Load a save first to identify the required sprites."; return; }
        string? folder = EditorPaths.FindGame();
        if (folder is null) { folder = await Ui.PickFolder(Owner, "Choose the Silverpine game folder"); if (folder is null || _closed) return; if (EditorPaths.GameData(folder) is null) throw new InvalidDataException("Choose the folder containing Silverpine_Data or the Silverpine.app bundle."); EditorPaths.Write("game-folder", folder); }
        Busy(true); _status.Text = "Reading Unity artwork… The first extraction takes longer; subsequent loads use the per-user cache.";
        try
        {
            GameAssetSource artwork = await GameAssetSource.LoadAsync(folder, _sessionMap, _cancellation.Token); if (_closed) return;
            _sessionArt = artwork; _canvas.SetArt(artwork); _status.Text = $"{artwork.Prefabs.Count} prefabs loaded; {artwork.MissingCount} unavailable. Native global sorting layers; optional ambient shadows. Dynamic lighting and object states are not simulated.";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!_closed) _status.Text = "Artwork unavailable; coordinate map still usable. " + e.Message; }
        finally { if (!_closed) Busy(false); }
    }
    public void Dispose() { if (_closed) return; _closed = true; _cancellation.Cancel(); _canvas.Dispose(); }

    private sealed class RoutineDraft
    {
        internal RoutineRecord Original { get; }
        internal RoutineRecord Working { get; }
        internal byte[] Before { get; }
        internal RoutineArgumentRecord? Patrol { get; private set; }
        internal List<Int2> Points { get; } = [];
        private Int2[] _beforePoints = [];
        private readonly RoutineArgumentRecord? _initialPatrol;
        internal string? PatrolError { get; private set; }
        internal string Label => string.IsNullOrWhiteSpace(Working.Id) ? "Untitled routine" : Working.Id;
        internal RoutineDraft(RoutineRecord original) { Original = original; Working = original.CreateDraft(); Before = Working.Storage.ToBytes(); SyncPatrol(); _initialPatrol = Patrol; }
        internal void SyncPatrol()
        {
            var patrol = Working.Arguments.Items.FirstOrDefault(a => a.TypeName == "RoutineArgument_Patrol");
            if (ReferenceEquals(patrol, Patrol)) return;
            Patrol = patrol; Points.Clear(); PatrolError = null;
            try { if (patrol?.Storage is { } storage) Points.AddRange(PatrolWaypoints.Read(storage)); }
            catch (Exception e) when (e is InvalidDataException or EndOfStreamException) { PatrolError = e.Message; }
            _beforePoints = Points.ToArray();
        }
        internal bool CanEditPatrol => Patrol is not null && PatrolError is null && Working.Arguments.CanEdit;
        internal void Flush()
        {
            if (!CanEditPatrol) return;
            if (ReferenceEquals(Patrol, _initialPatrol) && Points.SequenceEqual(_beforePoints)) return;
            if (Points.Count == 0) throw new InvalidDataException(Label + ": Patrol needs at least one waypoint.");
            PatrolWaypoints.Write(Patrol!.Storage!, Points);
        }
    }
    private sealed class RoutineDetails : UserControl
    {
        private readonly RoutineDraft _routine;
        private readonly EditState _state;
        private readonly ContentControl _patrolHost = new();
        private readonly TextBox _x, _y;
        private TextBox? _pointX, _pointY;
        private ListBox? _waypoints;
        private int _manualX, _manualY;
        internal int SelectedWaypoint => _waypoints?.SelectedIndex ?? -1;
        internal RoutineDetails(RoutineDraft routine, EditState state, MapCanvas canvas, Action componentsChanged)
        {
            _routine = routine; _state = state; var record = routine.Working;
            var f = new Fields(state); f.Panel.Margin = new Thickness(12); f.Heading("Destination");
            _x = f.Int("X", record.TargetX, n => record.TargetX = n); _y = f.Int("Y", record.TargetY, n => record.TargetY = n);
            f.Panel.Children.Add(Ui.Button("Center destination", () => canvas.Center(new(record.TargetX, record.TargetY))));
            if (!record.Arguments.CanEdit) f.Note(record.Arguments.Error! + " Original components are preserved.");
            f.Panel.Children.Add(_patrolHost);
            var activity = new Fields(state); activity.Panel.Margin = new Thickness(12);
            activity.Text("ID", record.Id, s => record.Id = s, help: "The Lua selector returns this ID. Changing it invalidates a Dynamic selector when applied.");
            activity.Text("Pathing activity", record.PathingActivity, s => record.PathingActivity = s, true);
            activity.Text("Activity", record.Activity, s => record.Activity = s, true);
            activity.Text("Visible activity", record.VisibleActivity, s => record.VisibleActivity = s, true);
            var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = "Position", Content = Ui.Scroll(f.Panel) });
            tabs.Items.Add(new TabItem { Header = "Activity", Content = Ui.Scroll(activity.Panel) });
            tabs.Items.Add(new TabItem { Header = "Components", Content = RoutinesEditor.Components(record, state, componentsChanged, compact: true) });
            Content = tabs; RefreshPatrol();
        }
        internal void SetDestination(Int2 p) { _x.Text = p.X.ToString(CultureInfo.InvariantCulture); _y.Text = p.Y.ToString(CultureInfo.InvariantCulture); }
        internal void SelectWaypoint(int index) { if (_waypoints is not null) _waypoints.SelectedIndex = index; }
        internal void RefreshPatrol()
        {
            if (_patrolHost.Content is Control old) _state.Forget(old);
            _routine.SyncPatrol(); _patrolHost.Content = null; _waypoints = null;
            if (_routine.Patrol is null) return;
            var f = new Fields(_state); f.Panel.Margin = new Thickness(0);
            f.Heading("Patrol waypoints", "Numbered lines show visit order, not obstacle-avoiding pathfinding.");
            if (_routine.PatrolError is { } error) { f.Note("Unreadable waypoints are preserved: " + error); _patrolHost.Content = f.Panel; return; }
            _waypoints = new ListBox { Height = 160 }; f.Panel.Children.Add(_waypoints);
            _pointX = f.Int("Waypoint X", 0, n => _manualX = n); _pointY = f.Int("Waypoint Y", 0, n => _manualY = n);
            _waypoints.SelectionChanged += (_, _) => { if (SelectedWaypoint >= 0 && SelectedWaypoint < _routine.Points.Count) { Int2 p = _routine.Points[SelectedWaypoint]; _pointX.Text = p.X.ToString(CultureInfo.InvariantCulture); _pointY.Text = p.Y.ToString(CultureInfo.InvariantCulture); } _state.Changed(); };
            f.Panel.Children.Add(Ui.Row(Ui.Button("Add", () => AddPoint(new(_manualX, _manualY))), Ui.Button("Update", () => MovePoint(new(_manualX, _manualY))), Ui.Button("Remove", () => { int i = SelectedWaypoint; if (_routine.CanEditPatrol && i >= 0) { _routine.Points.RemoveAt(i); RefreshPoints(Math.Min(i, _routine.Points.Count - 1)); } })));
            f.Panel.Children.Add(Ui.Row(Ui.Button("Move up", () => Reorder(-1)), Ui.Button("Move down", () => Reorder(1))));
            _patrolHost.Content = f.Panel; RefreshPoints(-1);
        }
        internal void AddPoint(Int2 point) { if (!_routine.CanEditPatrol || _routine.Points.Count >= 10000) return; _routine.Points.Add(point); RefreshPoints(_routine.Points.Count - 1); }
        internal void MovePoint(Int2 point) { if (!_routine.CanEditPatrol || SelectedWaypoint < 0) return; int i = SelectedWaypoint; _routine.Points[i] = point; RefreshPoints(i); }
        private void Reorder(int delta) { int i = SelectedWaypoint, j = i + delta; var points = _routine.Points; if (_routine.CanEditPatrol && i >= 0 && j >= 0 && j < points.Count) { (points[i], points[j]) = (points[j], points[i]); RefreshPoints(j); } }
        private void RefreshPoints(int selected) { if (_waypoints is null) return; _waypoints.ItemsSource = _routine.Points.Select((p, i) => $"{i + 1}.    X {p.X}   Y {p.Y}").ToArray(); _waypoints.SelectedIndex = selected; _state.Changed(); }
    }
}
