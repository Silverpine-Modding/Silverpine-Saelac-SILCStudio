using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using SilverpineSilcEditor.Core;
using SilverpineSilcEditor.Desktop;
using System.Diagnostics;
using System.Security.Cryptography;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 2) throw new ArgumentException("Usage: PortableTests <Definitions folder> <output folder> [save.sav] [game folder], or --self-test <output folder>");
            bool synthetic = args[0] == "--self-test";
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            AppBuilder.Configure<EditorApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().WithInterFont().SetupWithoutStarting();
            string definitions = synthetic ? Path.Combine(output, "Fixtures") : Path.GetFullPath(args[0]);
            if (synthetic) { CreateFixtures(definitions); args = [definitions, output]; }
            string[] files = Directory.GetFiles(definitions, "*.silc");
            string[] players = files.Where(p => Path.GetFileName(p).StartsWith("PlayerCharacter_")).Select(p => Path.GetFileNameWithoutExtension(p)[16..]).ToArray();
            string[] npcs = files.Where(p => Path.GetFileName(p).StartsWith("NPC_")).Select(p => Path.GetFileNameWithoutExtension(p)[4..]).Concat(NpcDefinitionModel.BaseGameNpcNames).ToArray();
            int accepted = 0, expectedValidation = 0;
            foreach (string path in files)
            {
                byte[] original = File.ReadAllBytes(path); var doc = SilcDocument.FromBytes(path, original);
                Check(doc.Definition is not null, "Parse " + Path.GetFileName(path));
                byte[] before = doc.Definition!.ToBytes(); int changes = 0;
                using var editor = new DefinitionView(doc, () => changes++, npcs, players); Dispatcher.UIThread.RunJobs();
                try { editor.Apply(); accepted++; }
                catch (InvalidDataException e) { Console.WriteLine("Existing validation: " + Path.GetFileName(path) + ": " + e.Message); expectedValidation++; }
                Check(changes == 0, "Opening must not mark dirty: " + path);
                Check(before.AsSpan().SequenceEqual(doc.Definition.ToBytes()), "No-edit fields changed: " + path);
                var roundtrip = SilcDocument.FromBytes(path, doc.BuildBytes());
                Check(roundtrip.Definition!.ToBytes().AsSpan().SequenceEqual(before), "Semantic SILC round trip: " + path);
                Check(SHA256.HashData(original).AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Live file changed: " + path);
            }
            Console.WriteLine($"Round-trip scan: {files.Length} files; {accepted} accepted, {expectedValidation} pre-existing validation warnings.");
            string playerFile = files.First(p => Path.GetFileName(p).StartsWith("PlayerCharacter_"));
            string npcFile = files.First(p => Path.GetFileName(p).StartsWith("NPC_") && new NpcDefinitionModel(SilcDocument.Load(p).Definition!).Routines.Items.Count > 0);
            RenderDefinition(playerFile, npcs, players, output, "player"); RenderDefinition(npcFile, npcs, players, output, "npc");
            TestMainWindow(npcFile, output); TestMapDraft(output); TestMapLayers(output);
            TestComponents(output); TestRacials(playerFile, npcs, players); TestPortraitPreview(playerFile); TestWaypoints(); TestImageComposite(output); TestSafeSave(playerFile, output);
            ExpectThrows(() => DefinitionView.ValidateModelJson("[]"), "Model must be an object"); ExpectThrows(() => DefinitionView.ValidateModelJson("{}"), "Model must not be empty"); DefinitionView.ValidateModelJson("{\"test\":1}");
            if (args.Length > 2)
            {
                var timer = Stopwatch.StartNew(); SavedTileMap map = SavedTileMap.Load(args[2]); timer.Stop();
                Console.WriteLine($"Save: {map.Cells.Count:N0} cells, {map.Regions.Count} regions, {map.ObjectCount:N0} objects, {timer.ElapsedMilliseconds} ms; warnings: {string.Join("; ", map.Warnings)}");
                Check(map.Cells.Count > 1000, "Save has tiles");
                using var canvas = new MapCanvas(); canvas.SetMap(map); canvas.Center(new(0, 0)); canvas.Waypoints = [new(0, 0), new(6, 3), new(12, -4)];
                var window = TestWindow(canvas); window.Show(); Dispatcher.UIThread.RunJobs();
                Check(canvas.ScreenToTile(canvas.TileToScreen(new(-9, 12))) == new Int2(-9, 12), "Coordinate inverse (negative X, north-positive Y)");
                if (args.Length > 3)
                {
                    var art = Task.Run(() => GameAssetSource.LoadAsync(args[3], map, CancellationToken.None, Path.Combine(output, "art-cache"))).GetAwaiter().GetResult(); canvas.SetArt(art); Console.WriteLine($"Artwork: {art.Prefabs.Count} extracted, {art.MissingCount} unavailable.");
                    Check(art.Prefabs["prefab_furniture_rug_1"][0].Layer < art.Prefabs["prefab_furniture_table_wood"][0].Layer, "Native rug is below furniture globally");
                    Check(art.Prefabs["prefab_tile_water"][0].Order > art.Prefabs["prefab_tile_rock"][0].Order, "Native water renders above floor tiles");
                    var table = art.Prefabs["prefab_furniture_table_wood"][0]; Check(table.YSort && table.YOffset == -100, "Native table YSorter offset extracted");
                    Check(art.StaticPlacements.Count > 0 && art.StaticPlacements.All(p => p.Prefab == "prefab_tile_water_bathhouse"), "Fixed non-serialized bathhouse water extracted from scene");
                }
                timer.Restart(); Render(window, Path.Combine(output, "map-detailed.png")); timer.Stop(); Console.WriteLine($"First detailed map render: {timer.ElapsedMilliseconds} ms");
                timer.Restart(); RenderToMemory(window); timer.Stop(); Console.WriteLine($"Cached detailed map drawing (no PNG encoding): {timer.ElapsedMilliseconds} ms"); Render(window, Path.Combine(output, "map-cached.png"));
                var furniture = map.Cells.Values.SelectMany(c => c.Placements).ToArray();
                var tablePair = furniture.FirstOrDefault(t => t.Prefab == "prefab_furniture_table_wood" && furniture.Any(n => n.Prefab == t.Prefab && n.X == t.X && n.Y == t.Y + 1));
                if (tablePair is not null)
                {
                    int chairs = furniture.Count(p => p.Prefab == "prefab_furniture_chair" && Math.Abs(p.X - tablePair.X) == 1 && (p.Y == tablePair.Y || p.Y == tablePair.Y + 1));
                    Console.WriteLine($"Adjacent furniture group at ({tablePair.X}, {tablePair.Y}): 2 tables, {chairs} chairs.");
                    canvas.Center(new((int)tablePair.X, (int)tablePair.Y)); canvas.Zoom(4); Render(window, Path.Combine(output, "map-furniture.png")); canvas.Zoom(.25); canvas.Center(new(0, 0));
                }
                canvas.HideVegetation = true; Render(window, Path.Combine(output, "map-no-vegetation.png")); canvas.HideVegetation = false;
                canvas.Center(new(-20, 23)); canvas.Zoom(3); Render(window, Path.Combine(output, "map-bathhouse.png")); canvas.Zoom(1.0 / 3); canvas.Center(new(0, 0));
                canvas.Zoom(.18); Render(window, Path.Combine(output, "map-overview.png"));
                for (int i = 0; i < 30; i++) { canvas.Center(new(i * 32, 0)); RenderToMemory(window); }
                Check(canvas.CachedChunkCount <= 24, "Bounded map chunk cache"); window.Close();
                byte[] bad = [1, 2, 3, 4]; string badPath = Path.Combine(output, "malformed.sav"); File.WriteAllBytes(badPath, bad);
                ExpectThrows(() => SavedTileMap.Load(badPath), "Malformed save rejected");
                var cts = new CancellationTokenSource(); cts.Cancel(); ExpectThrows(() => SavedTileMap.Load(args[2], cts.Token), "Map cancellation");
            }
            Console.WriteLine($"PASS: {_checks} checks. Live SILCs/saves were only read."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static Window TestWindow(Control content) => new() { Width = 1320, Height = 850, Background = Ui.Background, Content = content };
    private static void TestMainWindow(string path, string output)
    {
        var window = new EditorWindow(Path.GetDirectoryName(path)); window.Show(); Dispatcher.UIThread.RunJobs();
        Task task = window.LoadFile(path); var timeout = Stopwatch.StartNew();
        while (!task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(20)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        Check(task.IsCompletedSuccessfully, "Main window loads asynchronously"); Render(window, Path.Combine(output, "studio-window.png"));
        window.Width = 1060; window.Height = 660; Render(window, Path.Combine(output, "studio-minimum.png"));
        var view = window.GetLogicalDescendants().OfType<DefinitionView>().Distinct().Single();
        var routineTab = view.Items.OfType<TabItem>().Single(t => (t.Header as string) == "Routines"); view.SelectedItem = routineTab; Dispatcher.UIThread.RunJobs();
        var routinesEditor = (RoutinesEditor)routineTab.Content!; var records = (RecordEditor<RoutineRecord>)routinesEditor.Content!; var routine = records.Selected!;
        string originalId = routine.Id;
        routinesEditor.GetLogicalDescendants().OfType<Button>().Distinct().Single(b => (b.Content as string) == "Edit on map…").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var map = window.GetLogicalDescendants().OfType<RoutineMapEditor>().Distinct().Single();
        var activityTab = map.GetLogicalDescendants().OfType<TabItem>().First(t => (t.Header as string) == "Activity");
        var activity = (Control)activityTab.Content!; activity.GetLogicalDescendants().OfType<TextBox>().Distinct().Single(t => t.Text == originalId).Text = originalId + "_map_test"; Dispatcher.UIThread.RunJobs();
        map.GetLogicalDescendants().OfType<Button>().Distinct().Single(b => (b.Content as string) == "Apply routine changes").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        timeout.Restart(); while (ReferenceEquals(routinesEditor.Content, records) && timeout.Elapsed < TimeSpan.FromSeconds(10)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        Check(!ReferenceEquals(routinesEditor.Content, records) && routine.Id == originalId + "_map_test", "Map Apply rebuilds regular routine fields with current data");
        var luaTab = view.Items.OfType<TabItem>().Single(t => (t.Header as string) == "Lua"); var lua = ((Control)luaTab.Content!).GetLogicalDescendants().OfType<TextBox>().Distinct().Single();
        Check(!lua.IsReadOnly || lua.Text == "", "Map routine renaming invalidates a Dynamic Lua selector");
        // Reload the original read-only test input to discard the intentional UI edit.
        task = window.LoadFile(path); timeout.Restart(); while (!task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(20)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        Check(task.IsCompletedSuccessfully, "Main editor reloads after embedded map Apply"); window.Close();
    }
    private static void TestMapDraft(string output)
    {
        var routine = RoutineRecord.Create(); routine.Id = "patrol_test"; routine.TargetX = -4; routine.TargetY = 8;
        var argument = routine.Arguments.Add("RoutineArgument_Patrol"); PatrolWaypoints.Write(argument.Storage!, [new(0, 0), new(4, 8)]);
        routine.Storage.SetString("future_field", "preserved");
        var other = RoutineRecord.Create(); other.Id = "working"; other.Activity = "working activity"; other.TargetX = 10; other.TargetY = -5;
        var overlap = RoutineRecord.Create(); overlap.Id = "overlapping"; overlap.TargetX = 10; overlap.TargetY = -5;
        RoutineRecord[] routines = [routine, other, overlap];
        routine.Apply(); byte[] before = routine.Storage.ToBytes();
        var window = new EditorWindow(output); window.Show(); Dispatcher.UIThread.RunJobs();
        RoutineMapEditor map = null!; MapCanvas canvas = null!; Task<bool> task = null!;
        void Open()
        {
            task = window.EditRoutineMap(routine, routines); Dispatcher.UIThread.RunJobs();
            map = window.GetLogicalDescendants().OfType<RoutineMapEditor>().Single(); canvas = map.GetLogicalDescendants().OfType<MapCanvas>().Single();
            Check(ReferenceEquals(TopLevel.GetTopLevel(map), window), "Map is embedded in the main editor window");
        }
        void Mode(string name) { map.GetLogicalDescendants().OfType<ComboBox>().Single(c => c.ItemsSource is string[] options && options.Contains("Select routine")).SelectedItem = name; Dispatcher.UIThread.RunJobs(); }
        void Click(string name) { map.GetLogicalDescendants().OfType<Button>().Single(b => (b.Content as string) == name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        Open(); Mode("Set destination"); canvas.TileClicked!(new(-22, 41)); Dispatcher.UIThread.RunJobs();
        Check(routine.TargetX == -4 && routine.TargetY == 8, "Map edits staged before Apply"); Render(window, Path.Combine(output, "routine-map-embedded.png"));
        Click("Cancel / return to routines"); Check(task.IsCompleted && !task.Result, "Cancel returns to main editor");
        routine.Apply(); Check(routine.Storage.ToBytes().AsSpan().SequenceEqual(before), "Cancel discards draft without resetting patrol");
        Open(); Mode("Set destination"); canvas.TileClicked!(new(-22, 41)); Dispatcher.UIThread.RunJobs(); Click("Apply routine changes");
        Check(routine.TargetX == -22 && routine.TargetY == 41, "Map destination Apply");
        argument = routine.Arguments.Items.Single();
        Check(PatrolWaypoints.Read(argument.Storage!).SequenceEqual(new[] { new Int2(0, 0), new Int2(4, 8) }), "Destination edit keeps patrol unchanged");
        Check(routine.Storage.GetString("future_field") == "preserved", "Map draft preserves unknown routine fields");
        argument.Storage!.SetInt("currentIndex", 1); argument.Storage.SetInt("turnsLeftToWait", 19);
        Open(); Mode("Add waypoint");
        canvas.TileClicked!(new(-30, 24)); Dispatcher.UIThread.RunJobs();
        Check(PatrolWaypoints.Read(argument.Storage).Count == 2, "Waypoint click remains a draft");
        Click("Apply routine changes"); argument = routine.Arguments.Items.Single();
        Check(PatrolWaypoints.Read(argument.Storage!).Last() == new Int2(-30, 24), "Waypoint click Apply");
        Check(argument.Storage!.GetInt("currentIndex") == 0 && argument.Storage.GetInt("turnsLeftToWait") == -1, "Edited patrol resets runtime cursor");
        Open(); Check(canvas.RoutinePoints.Count == 6, "All routine destinations and patrol waypoints are shown");
        Check(canvas.SelectRoutineMode, "Selection is the default map action");
        var hits = canvas.HitTestRoutinePoints(canvas.TileToScreen(new(10, -5))); Check(hits.Count == 2, "Overlapping routine markers are both selectable");
        map.SelectPoints(hits); Dispatcher.UIThread.RunJobs();
        Check(map.SelectedRoutineIndex == 0, "Overlap chooser does not arbitrarily select a routine");
        map.SelectPoints([hits.Single(p => p.RoutineIndex == 1)]); Dispatcher.UIThread.RunJobs();
        Check(map.SelectedRoutineIndex == 1, "Clicking another routine selects its editor");
        Check(!map.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Patrol waypoints"), "Non-patrol routine has no waypoint controls");
        Check(!map.GetLogicalDescendants().OfType<ComboBox>().Any(c => c.ItemsSource is string[] options && options.Contains("Add waypoint")), "Non-patrol routine has no patrol placement modes");
        var detailTabs = map.GetLogicalDescendants().OfType<TabControl>().First(t => t.Items.OfType<TabItem>().Any(i => (i.Header as string) == "Activity")); detailTabs.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
        map.GetLogicalDescendants().OfType<TextBox>().Distinct().Single(t => t.Text == "working activity").Text = "direct map edit"; Dispatcher.UIThread.RunJobs();
        Check(other.Activity == "working activity", "Direct routine editing stays isolated until Apply");
        map.SelectPoints([canvas.RoutinePoints.First(p => p.RoutineIndex == 0 && p.WaypointIndex == 1)]); Dispatcher.UIThread.RunJobs();
        Check(canvas.SelectedWaypoint == 1, "Selecting a waypoint selects its owning routine and point");
        Check(map.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Patrol waypoints"), "Patrol routine shows waypoint controls");
        map.SelectPoints([canvas.RoutinePoints.First(p => p.RoutineIndex == 1)]); Dispatcher.UIThread.RunJobs();
        Check(map.GetLogicalDescendants().OfType<TextBox>().Any(t => t.Text == "direct map edit"), "Switching routines keeps in-map edits");
        detailTabs.SelectedIndex = 0; Mode("Set destination"); canvas.TileClicked!(new(15, -8)); Dispatcher.UIThread.RunJobs();
        Click("Apply routine changes"); Check(other.Activity == "direct map edit" && other.TargetX == 15 && other.TargetY == -8, "Applies edits to a selected associated routine");
        Open(); Render(window, Path.Combine(output, "routine-map-patrol.png"));
        map.SelectPoints([canvas.RoutinePoints.First(p => p.RoutineIndex == 1)]); Dispatcher.UIThread.RunJobs();
        canvas.FramePoints(canvas.RoutinePoints.Select(p => p.Position)); Render(window, Path.Combine(output, "routine-map-non-patrol.png"));
        window.Width = 1060; window.Height = 660; Render(window, Path.Combine(output, "routine-map-minimum.png"));
        Click("Apply routine changes"); Check(!task.Result, "No-edit map Apply does not mark the definition dirty");
        Open(); map.SelectPoints([canvas.RoutinePoints.First(p => p.RoutineIndex == 1)]); Dispatcher.UIThread.RunJobs();
        detailTabs = map.GetLogicalDescendants().OfType<TabControl>().First(t => t.Items.OfType<TabItem>().Any(i => (i.Header as string) == "Activity"));
        detailTabs.SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
        var componentPanel = (Control)((TabItem)detailTabs.Items[2]!).Content!;
        var typePicker = componentPanel.GetLogicalDescendants().OfType<ComboBox>().Distinct().Single(c => c.Items.Cast<object>().Any(i => i.ToString() == "Patrol"));
        typePicker.SelectedItem = typePicker.Items.Cast<object>().Single(i => i.ToString() == "Patrol");
        void ComponentClick(string label) { componentPanel.GetLogicalDescendants().OfType<Button>().Distinct().Single(b => (b.Content as string) == label).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        ComponentClick("Add component");
        Check(other.Arguments.Items.Count == 0, "Map component edits stay in the draft");
        Check(map.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Patrol waypoints"), "Adding Patrol reveals its waypoint controls");
        detailTabs.SelectedIndex = 0; Dispatcher.UIThread.RunJobs(); Render(window, Path.Combine(output, "routine-map-added-patrol.png"));
        detailTabs.SelectedIndex = 2; ComponentClick("Remove");
        Check(!map.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Patrol waypoints"), "Removing Patrol hides its waypoint controls");
        typePicker.SelectedItem = typePicker.Items.Cast<object>().Single(i => i.ToString() == "Face Direction"); ComponentClick("Add component");
        var direction = componentPanel.GetLogicalDescendants().OfType<ComboBox>().Distinct().Single(c => c.Items.Cast<object>().Any(i => i.ToString() == "Right"));
        direction.SelectedItem = direction.Items.Cast<object>().Single(i => i.ToString() == "Right"); Dispatcher.UIThread.RunJobs();
        Render(window, Path.Combine(output, "routine-map-components.png")); Click("Apply routine changes");
        Check(other.Arguments.Items.Single().Storage!.GetInt("direction") == 3, "Direct map component editing applies to its routine");
        Open(); Mode("Set destination"); canvas.TileClicked!(new(800, 800)); Dispatcher.UIThread.RunJobs();
        var xInput = map.GetLogicalDescendants().OfType<TextBox>().Distinct().First(t => t.Text == "800"); xInput.Text = "invalid"; Dispatcher.UIThread.RunJobs();
        map.SelectPoints([canvas.RoutinePoints.First(p => p.RoutineIndex == 1)]); Dispatcher.UIThread.RunJobs();
        Click("Apply routine changes"); Check(!task.IsCompleted && routine.TargetX == -22, "Invalid input in a different routine blocks the entire Apply");
        Click("Cancel / return to routines"); Check(routine.TargetX == -22, "Cancel discards a multi-routine map session");
        Open(); Mode("Set destination"); canvas.TileClicked!(new(900, 900)); Dispatcher.UIThread.RunJobs(); window.Close();
        Check(task.IsCompleted && !task.Result && routine.TargetX == -22, "Window close returns from embedded map without applying"); window.Close();
    }
    private static void TestMapLayers(string output)
    {
        var map = new SavedTileMap(); var art = new GameAssetSource();
        string Sprite(string name, IBrush color) { string path = Path.Combine(output, name + ".png"); File.WriteAllBytes(path, Png(16, 16, color)); return path; }
        void Add(string name, float x, float y, MapArtwork image)
        {
            var tile = new Int2((int)x, (int)y); if (!map.Cells.TryGetValue(tile, out var cell)) map.Cells[tile] = cell = new MapCell();
            cell.Objects.Add(name); cell.Placements.Add(new(name, x, y)); art.Prefabs[name] = [image];
        }
        MapArtwork Image(string name, IBrush color, int layer, int order, bool shadow = false) => new(Sprite(name, color), -.5f, -.5f, 1, 1, layer, order, false, 0, 0, shadow);
        Add("prefab_furniture_table", 0, 1, Image("test-table", Brushes.Lime, 1, -10));
        Add("prefab_furniture_rug", 0, 0, Image("test-rug", Brushes.Red, -2, 101) with { Left = -1.5f, Bottom = -1.5f, Width = 3, Height = 3 });
        Add("shadow", 0, 0, Image("test-shadow", Brushes.Black, 2, 900, true) with { Left = -1.5f, Bottom = -1.5f, Width = 3, Height = 3 });
        // Water deliberately precedes the floor in the save's enumeration.
        Add("prefab_tile_water", 3, 0, Image("test-water", Brushes.Blue, -2, 101));
        Add("prefab_tile_rock", 3, 0, Image("test-floor", Brushes.Gray, -2, 100));
        using var canvas = new MapCanvas(); canvas.AmbientShadows = false; canvas.SetMap(map); canvas.SetArt(art); canvas.Destination = new(100, 100); canvas.Zoom(4);
        var window = TestWindow(canvas); window.Show(); Dispatcher.UIThread.RunJobs();
        byte[] Pixel(Int2 tile)
        {
            using var image = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new(96, 96)); image.Render(window);
            Point p = canvas.TileToScreen(tile); byte[] pixel = new byte[4]; var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixel, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { image.CopyPixels(new PixelRect((int)p.X, (int)p.Y, 1, 1), handle.AddrOfPinnedObject(), 4, 4); } finally { handle.Free(); } return pixel;
        }
        Check(Pixel(new(0, 1))[1] > 240, "Global layers keep furniture above an overlapping rug and omit shadow overlays");
        art.Prefabs["shadow"] = [art.Prefabs["shadow"][0] with { Layer = -1, Order = 0 }]; canvas.SetArt(art); canvas.AmbientShadows = true;
        Check(Pixel(new(0, 1))[1] > 240, "Globally sorted ambient shadows cannot cover furniture");
        byte[] shaded = Pixel(new(0, 0)); Check(shaded[0] + shaded[1] + shaded[2] < 10, "Native ambient shadow layer shades the ground");
        canvas.AmbientShadows = false; Check(canvas.CachedChunkCount == 0, "Ambient shadow toggle invalidates cached chunks");
        byte[] water = Pixel(new(3, 0)); Check(water[1] < 10 && Math.Max(water[0], water[2]) > 240, "Water draws above rock regardless of save enumeration");
        Add("prefab_tree_oak", 0, 1, Image("test-tree", Brushes.Magenta, 1, 1000)); canvas.SetMap(map); canvas.SetArt(art);
        Check(Pixel(new(0, 1))[1] < 10, "Tree overlay is visible by default");
        canvas.HideVegetation = true; Check(canvas.CachedChunkCount == 0, "Vegetation toggle invalidates cached chunks");
        Check(Pixel(new(0, 1))[1] > 240, "Hide vegetation reveals underlying furniture");
        Check(MapCanvas.IsVegetation("prefab_plant_grass") && !MapCanvas.IsVegetation("prefab_tile_grass"), "Hiding grass objects keeps ground tiles");
        art.Prefabs["prefab_tile_water_bathhouse"] = [Image("test-static-water", Brushes.Lime, -2, 101)];
        art.StaticPlacements.Add(new("prefab_tile_water_bathhouse", 3, 0)); canvas.SetArt(art);
        Check(Pixel(new(3, 0))[1] > 240, "Fixed scene water is added above saved floors");
        Check(!map.Cells[new(3, 0)].Objects.Contains("prefab_tile_water_bathhouse"), "Scene supplements do not modify the save model");
        art.StaticPlacements.Add(new("prefab_tile_water_bathhouse", 40, 40)); canvas.SetArt(art);
        Check(map.Cells.Count == 3, "Scene supplements never generate saved cells");
        var sorted = art.Prefabs["prefab_furniture_table"][0] with { YSort = true, YOffset = -1 };
        Check(sorted.SortOrder(1.25f) == -13 && sorted.SortOrder(-1.25f) == 11, "YSorter uses midpoint-to-even and prefab offsets");
        Render(window, Path.Combine(output, "map-layer-regression.png")); window.Close();
    }
    private static void RenderDefinition(string path, string[] npcs, string[] players, string output, string prefix)
    {
        var doc = SilcDocument.Load(path); byte[] before = doc.Definition!.ToBytes(); int changes = 0;
        using var editor = new DefinitionView(doc, () => changes++, npcs, players); var w = TestWindow(editor); Ui.CurrentWindow = w; w.Show();
        for (int i = 0; i < editor.Items.Count; i++) { editor.SelectedIndex = i; Dispatcher.UIThread.RunJobs(); Render(w, Path.Combine(output, $"{prefix}-{i}.png")); }
        Check(changes == 0, "Viewing all tabs preserves dirty state: " + prefix); Check(before.AsSpan().SequenceEqual(doc.Definition.ToBytes()), "Viewing all tabs preserves bytes: " + prefix); w.Close();
    }
    private static void TestComponents(string output)
    {
        var set = RoutineArgumentSet.Parse(RoutineArgumentSet.EmptyBytes()); var lights = set.Add("RoutineArgument_TurnOnLights"); lights.Storage!.SetInt("targetTurns", 121);
        var state = new EditState(() => { }); Control panel = RoutinesEditor.ComponentFields(lights, state); var w = TestWindow(panel); w.Show(); Dispatcher.UIThread.RunJobs();
        var text = panel.GetLogicalDescendants().OfType<TextBox>().First(t => t.Text == "60.5"); text.Text = "45"; Dispatcher.UIThread.RunJobs(); state.Validate(); Check(lights.Storage.GetInt("targetTurns") == 90, "Light minutes convert to turns");
        text.Text = "-1"; Dispatcher.UIThread.RunJobs(); ExpectThrows(state.Validate, "Negative duration rejected"); text.Text = "60.5"; Dispatcher.UIThread.RunJobs(); state.Validate();
        Check(lights.Storage.GetInt("targetTurns") == 121, "Odd light turn counts preserved"); Render(w, Path.Combine(output, "component-lighting.png")); w.Close();
        var face = set.Add("RoutineArgument_FaceDirection"); face.Storage!.SetInt("direction", 3); panel = RoutinesEditor.ComponentFields(face, state); w = TestWindow(panel); w.Show(); Dispatcher.UIThread.RunJobs();
        Check(panel.GetLogicalDescendants().OfType<ComboBox>().First().SelectedItem!.ToString() == "Right", "Named facing direction"); w.Close();
    }
    private static void TestRacials(string path, string[] npcs, string[] players)
    {
        var doc = SilcDocument.Load(path); doc.Definition!.SetBlob("racials", BinaryData.WriteStringList([" foo ", "Bar", " foo "]));
        using var editor = new DefinitionView(doc, () => { }, npcs, players); editor.Apply(); Check(BinaryData.ReadStringList(doc.Definition.GetBlob("racials")!).SequenceEqual(new[] { " foo ", "Bar", " foo " }), "Racial whitespace/order/duplicates preserved");
    }
    private static void TestWaypoints()
    {
        var set = RoutineArgumentSet.Parse(RoutineArgumentSet.EmptyBytes()); var patrol = set.Add("RoutineArgument_Patrol"); patrol.Storage!.SetString("unknown", "keep");
        PatrolWaypoints.Write(patrol.Storage, [new(-12, 34), new(78, -90)]);
        Check(PatrolWaypoints.Read(patrol.Storage).SequenceEqual(new[] { new Int2(-12, 34), new Int2(78, -90) }), "Waypoint round trip");
        Check(patrol.Storage.GetString("unknown") == "keep" && patrol.Storage.GetInt("currentIndex") == 0 && patrol.Storage.GetInt("turnsLeftToWait") == -1, "Patrol preserves unknown fields and resets runtime cursor");
        ExpectThrows(() => PatrolWaypoints.Write(patrol.Storage, []), "Empty patrol rejected");
        byte[] bytes = set.ToBytes(); Check(RoutineArgumentSet.Parse(bytes).ToBytes().AsSpan().SequenceEqual(bytes), "Polymorphic patrol stream round trip");
    }
    private static void TestPortraitPreview(string path)
    {
        var doc = SilcDocument.Load(path); var model = new PlayerDefinitionModel(doc.Definition!); byte[] before = doc.Definition!.ToBytes(); int changes = 0;
        using var editor = new PortraitEditor(model, new EditState(() => changes++)); var w = TestWindow(editor); w.Show(); Dispatcher.UIThread.RunJobs();
        foreach (var choice in editor.GetLogicalDescendants().OfType<ComboBox>()) choice.SelectedIndex = 1;
        foreach (var check in editor.GetLogicalDescendants().OfType<CheckBox>()) check.IsChecked = false;
        Dispatcher.UIThread.RunJobs(); model.Apply(); Check(changes == 0, "Preview settings do not mark dirty"); Check(before.AsSpan().SequenceEqual(doc.Definition.ToBytes()), "Preview settings do not edit the SILC"); w.Close();
    }
    private static byte[] Png(int width, int height, IBrush? color)
    {
        using var image = new RenderTargetBitmap(new(width, height), new(96, 96)); using (var dc = image.CreateDrawingContext()) { if (color is not null) dc.FillRectangle(color, new Rect(0, 0, width, height)); }
        using var stream = new MemoryStream(); image.Save(stream); return stream.ToArray();
    }
    private static void TestImageComposite(string output)
    {
        byte[] body = Png(8, 12, Brushes.Red), face = Png(8, 4, null); using var combined = ImageEditor.Combine(face, body); combined.Save(Path.Combine(output, "expression-alpha-test.png"));
        byte[] pixels = new byte[8 * 12 * 4]; var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { combined.CopyPixels(new PixelRect(0, 0, 8, 12), handle.AddrOfPinnedObject(), pixels.Length, 8 * 4); } finally { handle.Free(); }
        Check(pixels[3] == 0 && pixels[(8 * 8 * 4) + 3] == 255, "Expression replaces top strip instead of alpha-overlay");
        ExpectThrows(() => ImageEditor.ValidateExpression(Png(7, 4, Brushes.Red), body), "Expression width check");
        ExpectThrows(() => ImageEditor.ValidateExpression(Png(8, 13, Brushes.Red), body), "Expression height check");
    }
    private static void TestSafeSave(string originalPath, string output)
    {
        string path = Path.Combine(output, "save-test.silc"); byte[] bytes = File.ReadAllBytes(originalPath); File.WriteAllBytes(path, bytes);
        var document = SilcDocument.Load(path); document.Definition!.SetString("description", "Test only"); string backup = document.Save(true)!;
        Check(File.ReadAllBytes(backup).AsSpan().SequenceEqual(bytes), "Exact backup before save"); Check(SilcDocument.Load(path).Definition!.GetString("description") == "Test only", "Atomic save validated");
    }
    private static void CreateFixtures(string directory)
    {
        Directory.CreateDirectory(directory);
        static byte[] Serialize(Action<BinaryWriter> write) { using var stream = new MemoryStream(); using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) write(w); return stream.ToArray(); }
        byte[] png = Png(8, 16, Brushes.CornflowerBlue);
        var assets = new VssContainer(); assets.SetBlob("expressionSprites", Serialize(w => w.Write(0)));
        assets.SetBlob("sprites", Serialize(w => { for (int i = 0; i < 6; i++) BinaryData.WriteCountedBytes(w, png); }));
        assets.SetFloat("dialogSpriteScale", 1.000001f); assets.SetFloat("dialogSpriteOffsetX", -12.34045f); assets.SetFloat("dialogSpriteOffsetY", 4.56789f);
        var player = new VssContainer(); player.SetString("description", "Fixture character"); player.SetBlob("assets", assets.ToBytes()); player.SetBlob("allowedSexes", BinaryData.WriteIntList([0])); player.SetBlob("racials", BinaryData.WriteStringList(["Fur"]));
        player.SetBlob("smallNudeSprites", Serialize(w => { w.Write(1); w.Write(0); BinaryData.WriteCountedBytes(w, png); })); player.SetBlob("dialogNudeSprite", [0]);
        var npc = new VssContainer(); npc.SetBool("enabled", true); npc.SetBool("overrideExisting", false); npc.SetString("customPlayerCharacterDefinitionName", "Fixture");
        npc.SetInt2("spawnPosition", new(-7, 12)); npc.SetInt("faction", 2); npc.SetString("species", "Fox"); npc.SetString("occupation", "Ranger"); npc.SetString("personalityTraits", "Calm, Kind");
        npc.SetString("publicCharacterDescription", "%NAME% is a %SEX% %SPECIES%."); npc.SetString("privateCharacterDescription", "%NAME% is a %SEX% %SPECIES% who likes forests.");
        npc.SetString("condensedKnowledgeString", "preserved cache"); npc.SetString("getTargetRoutineIDLuaScript", "if true then\r\n    return 'forest'\r\nend");
        var knowledge = KnowledgeRecord.Create(); knowledge.Name = "Home"; knowledge.Lore = "%NAME% lives in Silverpine."; knowledge.IsPublic = true;
        npc.SetBlob("knowledgeEntries", Serialize(w => { w.Write(1); knowledge.Storage.Write(w); }));
        var relationship = RelationshipRecord.Create(); relationship.TargetName = "Future NPC"; relationship.Relationship = 3; relationship.TargetRole = "friend"; relationship.SelfRole = "friend";
        npc.SetBlob("customNPCRelationships", Serialize(w => { w.Write(1); relationship.Storage.Write(w); }));
        var routine = RoutineRecord.Create(); routine.Id = "forest"; routine.PathingActivity = "walking to the woods"; routine.Activity = "exploring"; routine.VisibleActivity = "exploring the woods"; routine.TargetX = -8; routine.TargetY = 5;
        routine.Arguments.Add("RoutineArgument_FaceDirection"); routine.Arguments.Add("RoutineArgument_TurnOnLights"); routine.Apply();
        npc.SetBlob("possibleRoutines", Serialize(w => { w.Write(1); routine.Storage.Write(w); }));
        var model = new VssContainer(); model.SetString("modelJson", "{\"fixture\":true}");
        foreach (var (prefix, type, definition) in new[] { ("PlayerCharacter", "CustomContentDefinition_PlayerCharacter", player), ("NPC", "CustomContentDefinition_NPC", npc), ("ModelDefinition", "CustomContentDefinition_ModelDefinition", model) })
        {
            byte[] raw = Serialize(w => { w.Write(type); definition.Write(w); w.Write(new byte[] { 17, 42, 91 }); });
            using var compressed = new MemoryStream(); using (var brotli = new System.IO.Compression.BrotliStream(compressed, System.IO.Compression.CompressionLevel.Optimal, true)) brotli.Write(raw);
            var envelope = new VssContainer(); envelope.SetString("version", "Release 1.7.3"); envelope.SetBlob("definition", compressed.ToArray());
            File.WriteAllBytes(Path.Combine(directory, prefix + "_Fixture.silc"), envelope.ToBytes().Concat(new byte[] { 33, 44 }).ToArray());
        }
    }
    private static void Render(Window w, string path) { Dispatcher.UIThread.RunJobs(); using var image = new RenderTargetBitmap(new PixelSize((int)w.Width, (int)w.Height), new(96, 96)); image.Render(w); image.Save(path); }
    private static void RenderToMemory(Window w) { Dispatcher.UIThread.RunJobs(); using var image = new RenderTargetBitmap(new PixelSize((int)w.Width, (int)w.Height), new(96, 96)); image.Render(w); }
    private static void Check(bool pass, string message) { _checks++; if (!pass) throw new Exception(message); }
    private static void ExpectThrows(Action action, string message) { bool threw = false; try { action(); } catch (Exception e) when (e is InvalidDataException or EndOfStreamException or OperationCanceledException) { threw = true; } Check(threw, message); }
}
