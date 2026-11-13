using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SilverpineSilcEditor.Core;
using SilverpineSilcEditor.Desktop;
using System.Diagnostics;

internal static partial class Program
{
    private static void PumpUntil(Func<bool> done)
    {
        var timer = Stopwatch.StartNew();
        while (!done() && timer.Elapsed < TimeSpan.FromSeconds(20)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        Check(done(), "Asynchronous guidance test completed");
        Dispatcher.UIThread.RunJobs();
    }
    private static T AwaitUi<T>(Task<T> task) { PumpUntil(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void AwaitUi(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static IEnumerable<T> Descendants<T>(Control c) where T : Control => c.GetLogicalDescendants().OfType<T>().Distinct();
    private static void ClickButton(Control c, string text) => Descendants<Button>(c).Single(b => b.Content as string == text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void TestGuidance(string output)
    {
        string folder = Path.Combine(output, "guidance-" + Guid.NewGuid().ToString("N")); CreateFixtures(folder);
        string playerPath = Path.Combine(folder, "PlayerCharacter_Fixture.silc"), npcPath = Path.Combine(folder, "NPC_Fixture.silc");
        var player = SilcDocument.Load(playerPath);
        Check(GameEditorGuidance.Collect(player, []).Count == 0, "Ordinary player has no advisory warnings");
        player.Definition!.SetString("description", new string('a', 400));
        Check(GameEditorGuidance.Collect(player, []).Count == 0, "Exact 400-character boundary is allowed without warning");
        player.Definition.SetString("description", new string('a', 401));
        byte[] before = player.Definition.ToBytes();
        var warnings = GameEditorGuidance.Collect(player, []);
        Check(warnings.Count == 1 && warnings[0].Detail.Contains("401"), "Over-limit description is a precise advisory");
        Check(before.AsSpan().SequenceEqual(player.Definition.ToBytes()), "Inspecting limits never mutates data");
        Check(GameEditorGuidance.SaveWarning(warnings, true).Contains("exact backup") && GameEditorGuidance.SaveWarning(warnings, false).Contains("backups are OFF"), "Warning states actual backup mode");
        Check(GameEditorGuidance.SaveWarning(warnings, true).Contains("knowingly accept") && GameEditorGuidance.SaveWarning(warnings, true).Contains("runtime selection"), "Warning requires informed acceptance and distinguishes runtime limits");
        Check(GameEditorGuidance.SaveWarning(warnings, true).Contains(GameEditorGuidance.InGameTextLossWarning) && GameEditorGuidance.InGameTextLossWarning.Contains("WILL cut off") && GameEditorGuidance.InGameTextLossWarning.Contains("mod installed and enabled"), "Warning explicitly states in-game truncation and the required limit-disabling mod");

        var pm = new PlayerDefinitionModel(player.Definition);
        pm.OffsetX = -101; pm.OffsetY = 301; pm.Scale = 1.61f;
        byte[] png = pm.Images.First(i => i.Name == "Dialog").Data;
        for (int i = 0; i < 17; i++) pm.Images.Add(new(PlayerImageCategory.Expression, new string((char)('a' + i), 26), i, png));
        pm.Apply(); warnings = GameEditorGuidance.Collect(player, []);
        Check(warnings.Any(w => w.Field == "Expressions") && warnings.Count(w => w.Field.StartsWith("Expression “")) == 17, "Expression count and name limits are advisory");
        Check(warnings.Count(w => w.Field.StartsWith("Portrait")) == 3, "All portrait adjustment ranges reported");
        int changes = 0;
        using (var view = new DefinitionView(player, () => changes++, [], []))
        {
            view.Apply(); Dispatcher.UIThread.RunJobs();
            Check(changes == 0, "Opening over-limit data is not an edit");
            var saved = new PlayerDefinitionModel(player.Definition);
            Check(saved.Scale == 1.61f && saved.OffsetX == -101 && saved.OffsetY == 301 && saved.Images.Count(i => i.Category == PlayerImageCategory.Expression) == 17, "Player Apply preserves over-limit values and expressions");
        }
        var npc = SilcDocument.Load(npcPath); var model = new NpcDefinitionModel(npc.Definition!);
        model.Relationships.Items[0].TargetName = "Gareth"; model.Apply();
        Check(GameEditorGuidance.Collect(npc, []).Count == 0, "Normal NPC fields have no advisory warnings");
        var entry = model.Knowledge.Items[0]; entry.Name = new string('a', 31); entry.Lore = "%NAME% " + new string('x', 500);
        foreach (string target in new[] { "Futurefriend", "Anotherfriend" })
        {
            var relation = RelationshipRecord.Create(); relation.TargetName = target; relation.Relationship = 3; relation.TargetRole = "friend"; relation.SelfRole = "friend";
            model.Relationships.Items.Add(relation);
        }
        var routine = model.Routines.Items[0]; routine.Id = new string('a', 26); routine.Activity = new string('b', 51); routine.PathingActivity = new string('c', 51); routine.VisibleActivity = new string('d', 26);
        routine.Arguments.Items.Single(a => a.ShortName == "TurnOnLights").Storage!.SetInt("targetTurns", 1999);
        var patrol = routine.Arguments.Add("RoutineArgument_Patrol"); patrol.Storage!.SetInt("minWaitTurns", 1000); patrol.Storage.SetInt("maxWaitTurns", 1000);
        Int2[] points = Enumerable.Range(0, 20).Select(i => new Int2(i, -i)).ToArray(); PatrolWaypoints.Write(patrol.Storage, points);
        model.Apply(); before = npc.Definition!.ToBytes(); warnings = GameEditorGuidance.Collect(npc, []);
        Check(warnings.Any(w => w.Field.Contains(" lore") && w.Detail.Contains("507")), "Long lore is reported, not truncated");
        Check(warnings.Any(w => w.Field == "Important relationships") && warnings.Count(w => w.Detail.Contains("existence check")) == 2, "Important and unknown-NPC bypasses remain available and visible");
        Check(warnings.Any(w => w.Field.EndsWith("lighting minutes")) && warnings.Any(w => w.Field.EndsWith("serialized waypoints")), "Lighting and compact waypoint limits reported");
        Check(warnings.Count(w => w.Field.StartsWith("Routine “") && w.Detail.Contains("characters;")) == 5, "Both activity and map fields share all routine text limits");
        Check(before.AsSpan().SequenceEqual(npc.Definition.ToBytes()), "NPC warning collection preserves all bytes");
        using (var view = new DefinitionView(npc, () => { }, [], ["Fixture"])) view.Apply();
        var required = new NpcDefinitionModel(npc.Definition);
        string validLore = required.Knowledge.Items[0].Lore;
        required.Knowledge.Items[0].Lore = new string('x', 507); required.Apply();
        using (var view = new DefinitionView(npc, () => { }, [], ["Fixture"])) ExpectThrows(view.Apply, "Public lore still requires a leading %NAME%, even when length limits are bypassed");
        required.Knowledge.Items[0].Lore = "About %NAME% " + new string('x', 500); required.Apply();
        using (var view = new DefinitionView(npc, () => { }, [], ["Fixture"])) ExpectThrows(view.Apply, "Public lore must start with %NAME%, not merely contain it");
        required.Knowledge.Items[0].Lore = validLore; required.Apply();
        string publicDescription = npc.Definition.GetString("publicCharacterDescription");
        npc.Definition.SetString("publicCharacterDescription", "A %SEX% %SPECIES%.");
        using (var view = new DefinitionView(npc, () => { }, [], ["Fixture"])) ExpectThrows(view.Apply, "NPC description still requires %NAME%");
        npc.Definition.SetString("publicCharacterDescription", publicDescription);
        model = new NpcDefinitionModel(npc.Definition);
        Check(model.Relationships.Items.Count == 3 && model.Knowledge.Items[0].Lore.Length == 507, "Validation permits extra Important relationships, future NPCs, and full lore");
        Check(PatrolWaypoints.Read(model.Routines.Items[0].Arguments.Items.Single(a => a.ShortName == "Patrol").Storage!).SequenceEqual(points), "Waypoint list exceeding 100 characters is preserved");
        Check(!GameEditorGuidance.Collect(npc, ["Futurefriend", "Anotherfriend"]).Any(w => w.Detail.Contains("existence check")), "Loaded custom NPCs are recognized");
        for (int i = 0; i < 17; i++) { var k = KnowledgeRecord.Create(); k.Name = "Knowledge"; k.Lore = "%NAME% knows something."; model.Knowledge.Items.Add(k); }
        model.Apply();
        Check(GameEditorGuidance.Collect(npc, []).Any(w => w.Field == "Condensed knowledge" && w.Detail.Contains("runtime rule")), "Summary selection is identified as a runtime limit, not a text cap");

        TestLiveGuidanceControls(output);
        TestSaveAcknowledgement(playerPath, output);
    }

    private static void TestLiveGuidanceControls(string output)
    {
        int dirty = 0; string value = "start"; float offset = 0;
        var state = new EditState(() => dirty++); var f = new Fields(state);
        var text = f.Text("Lore", value, s => value = s, true, gameLimit: 200);
        var numeric = f.Float("Offset X", 0, n => offset = n, -100, 300);
        var auto = f.Auto("Target", "Gareth", ["Gareth"], _ => { }, gameLimit: 25);
        var window = TestWindow(Ui.Scroll(f.Panel)); window.Show(); Dispatcher.UIThread.RunJobs();
        Check(dirty == 0, "Counter initialization does not mark dirty");
        text.Text = new string('a', 201); numeric.Text = "300.5"; auto.Text = new string('b', 26); Dispatcher.UIThread.RunJobs(); state.Validate();
        Check(value.Length == 201 && text.MaxLength == 0 && offset == 300.5f, "Text and numeric limits do not cap input");
        Check(Descendants<TextBlock>(f.Panel).Count(t => t.Text?.Contains("exceeded (allowed)") == true && ReferenceEquals(t.Foreground, Ui.Warning)) == 2, "Text and autocomplete counters turn amber when over limit");
        Check(Descendants<TextBlock>(f.Panel).Any(t => t.Text?.Contains("outside range (allowed)") == true), "Numeric range is visibly advisory");
        Render(window, Path.Combine(output, "soft-limit-counters.png"));
        text.Text = new string('a', 200); numeric.Text = "300"; auto.Text = "Gareth"; Dispatcher.UIThread.RunJobs();
        Check(!Descendants<TextBlock>(f.Panel).Any(t => ReferenceEquals(t.Foreground, Ui.Warning)), "Counters return to normal at the boundary");
        numeric.Text = "1e35"; Dispatcher.UIThread.RunJobs(); state.Validate();
        Check(Descendants<TextBlock>(f.Panel).Any(t => t.Text?.Contains("outside range (allowed)") == true), "Very large finite floats are correctly flagged without clamping");
        window.Close();
    }

    private static void TestSaveAcknowledgement(string path, string output)
    {
        var window = new EditorWindow(Path.GetDirectoryName(path)); window.Show(); Dispatcher.UIThread.RunJobs(); AwaitUi(window.LoadFile(path));
        var view = Descendants<DefinitionView>(window).Single();
        var tab = view.Items.OfType<TabItem>().Single(t => t.Header as string == "Character"); view.SelectedItem = tab;
        var text = Descendants<TextBox>((Control)tab.Content!).Single();
        text.Text = new string('a', 450); Dispatcher.UIThread.RunJobs();
        int prompts = 0; bool backupEnabled = false;
        window.ConfirmSaveWarnings = (warnings, backup) => { prompts++; backupEnabled = backup; Check(warnings.Any(w => w.Field == "Character description"), "Save prompt reports the actual edited field"); return Task.FromResult(false); };
        byte[] original = File.ReadAllBytes(path); string backupFolder = Path.Combine(Path.GetDirectoryName(path)!, "SILC Editor Backups");
        Check(!AwaitUi(window.Save()) && prompts == 1, "Save requires acceptance when bypassing a limit");
        Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(original) && !Directory.Exists(backupFolder), "Cancel neither modifies the SILC nor creates backups");
        Check(text.Text!.Length == 450, "Cancel retains the user's open draft");

        // Verify the real warning window, including close/Cancel semantics and safe default focus.
        var warnings = new[] { new GameEditorWarning("Character description", "450 characters; base-game editor limit is 400. The complete text will be saved.") };
        var dialog = Ui.SaveWarningDialog(warnings, false); var result = dialog.ShowDialog<bool>(window); Dispatcher.UIThread.RunJobs();
        Check(!Descendants<Button>(dialog).Single(b => b.Content as string == GameEditorGuidance.SaveAnyway).IsDefault, "Enter does not implicitly accept risks");
        Check(Descendants<TextBlock>(dialog).Any(t => t.Text?.Contains("without a limit-disabling mod WILL cut off") == true && ReferenceEquals(t.Foreground, Ui.Warning)), "Prominent truncation warning remains beside the save acknowledgment");
        Render(dialog, Path.Combine(output, "save-limit-warning.png")); dialog.Close();
        Check(!AwaitUi(result), "Closing warning without accepting cancels");
        dialog = Ui.SaveWarningDialog(warnings, true); result = dialog.ShowDialog<bool>(window); Dispatcher.UIThread.RunJobs(); ClickButton(dialog, "Cancel"); Check(!AwaitUi(result), "Explicit Cancel declines save");
        dialog = Ui.SaveWarningDialog(warnings, true); result = dialog.ShowDialog<bool>(window); Dispatcher.UIThread.RunJobs(); ClickButton(dialog, GameEditorGuidance.SaveAnyway); Check(AwaitUi(result), "Explicit acknowledgment accepts save");

        window.ConfirmSaveWarnings = (_, backup) => { prompts++; backupEnabled = backup; return Task.FromResult(true); };
        Check(AwaitUi(window.Save()) && prompts == 2, "Retry prompts again and saves after explicit acceptance");
        Check(SilcDocument.Load(path).Definition!.GetString("description") == new string('a', 450), "Accepted save retains the complete over-limit text");
        if (backupEnabled) Check(Directory.GetFiles(backupFolder, "*.bak", SearchOption.AllDirectories).Any(p => File.ReadAllBytes(p).AsSpan().SequenceEqual(original)), "Accepted save backs up the exact original bytes");
        Check(AwaitUi(window.Save()) && prompts == 3, "Acknowledgment is required on every over-limit save");
        text.Text = "Within game limits"; Dispatcher.UIThread.RunJobs();
        Check(AwaitUi(window.Save()) && prompts == 3, "Within-limit save does not ask for a bypass acknowledgment");
        int backupsBefore = Directory.Exists(backupFolder) ? Directory.GetFiles(backupFolder, "*.bak", SearchOption.AllDirectories).Length : 0;
        var external = SilcDocument.Load(path); external.Definition!.SetString("description", "Changed externally while warning was open"); byte[] externalBytes = external.BuildBytes();
        text.Text = new string('a', 451); Dispatcher.UIThread.RunJobs();
        window.ConfirmSaveWarnings = (_, _) => { File.WriteAllBytes(path, externalBytes); return Task.FromResult(true); };
        var pendingSave = window.Save(); PumpUntil(() => window.OwnedWindows.Count > 0 || pendingSave.IsCompleted);
        var alert = window.OwnedWindows.Single();
        Check(Descendants<TextBlock>(alert).Any(t => t.Text?.Contains("changed on disk") == true), "External writes during acknowledgment are detected");
        ClickButton(alert, "OK"); Check(!AwaitUi(pendingSave), "External change prevents the accepted save");
        Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(externalBytes) && (Directory.Exists(backupFolder) ? Directory.GetFiles(backupFolder, "*.bak", SearchOption.AllDirectories).Length : 0) == backupsBefore, "External data is not overwritten or backed up as part of the rejected save");
        AwaitUi(window.LoadFile(path));
        window.Close();
    }
}
