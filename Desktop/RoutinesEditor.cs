using Avalonia.Controls;
using SilverpineSilcEditor.Core;
using System.Globalization;

namespace SilverpineSilcEditor.Desktop;

internal sealed class RoutinesEditor : UserControl
{
    internal RoutinesEditor(ParsedList<RoutineRecord> parsed, EditState state, Action invalidateScript)
    {
        if (!parsed.CanEdit) { Content = Ui.Note(parsed.Error!); return; }
        void Build(RoutineRecord? selected)
        {
        if (Content is Control oldContent) state.Forget(oldContent);
        Content = new RecordEditor<RoutineRecord>(parsed.Items, r => r.Id, () => { invalidateScript(); return RoutineRecord.Create(); }, routine =>
        {
            var f = new Fields(state); f.Heading("Routine", "Choose where the NPC goes, what they are doing, and what happens after they arrive.");
            f.Text("ID", routine.Id, s => { routine.Id = s; invalidateScript(); }, help: "The Lua routine selector returns this ID. Renaming it clears generated selectors in Dynamic mode.");
            f.Text("Pathing activity", routine.PathingActivity, s => routine.PathingActivity = s, help: "What the NPC is doing while travelling to the destination.");
            f.Text("Activity", routine.Activity, s => routine.Activity = s, help: "What the NPC does after arriving.");
            f.Text("Visible activity", routine.VisibleActivity, s => routine.VisibleActivity = s, help: "The activity description visible to the player.");
            TextBox x = f.Int("Destination X", routine.TargetX, n => routine.TargetX = n), y = f.Int("Destination Y", routine.TargetY, n => routine.TargetY = n);
            var components = new ContentControl();
            f.Panel.Children.Add(Ui.AsyncButton("Edit on map…", async () =>
            {
                state.Validate();
                string[] ids = parsed.Items.Select(r => r.Id).ToArray();
                var owner = TopLevel.GetTopLevel(this) as EditorWindow ?? throw new InvalidOperationException("Open the routine map from the main editor.");
                if (await owner.EditRoutineMap(routine, parsed.Items))
                {
                    if (!ids.SequenceEqual(parsed.Items.Select(r => r.Id))) invalidateScript();
                    Build(routine); state.Changed();
                }
            }, true));
            var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = "Destination & activity", Content = Ui.Scroll(f.Panel) });
            components.Content = Components(routine, state); tabs.Items.Add(new TabItem { Header = "Routine components", Content = components }); return tabs;
        }, state, invalidateScript, "routine");
        if (selected is not null) ((RecordEditor<RoutineRecord>)Content).Select(selected);
        }
        Build(null);
    }
    internal static Control Components(RoutineRecord routine, EditState state, Action? componentsChanged = null, bool compact = false)
    {
        if (!routine.Arguments.CanEdit) return Ui.Note(routine.Arguments.Error! + " The original argument bytes will be preserved.");
        var list = new ListBox(); var detail = new ContentControl(); var cache = new Dictionary<RoutineArgumentRecord, Control>();
        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<RoutineArgumentRecord>((a, _) => Ui.Text(a is null ? "" : RoutinePresentation.Name(a.TypeName)));
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not RoutineArgumentRecord a) { detail.Content = Ui.Note("Add a component to describe what happens when the NPC arrives."); return; }
            if (!cache.TryGetValue(a, out var c)) cache[a] = c = ComponentFields(a, state); detail.Content = c;
        };
        void Refresh(RoutineArgumentRecord? selected) { list.ItemsSource = null; list.ItemsSource = routine.Arguments.Items.ToArray(); list.SelectedItem = selected; }
        var addTypes = RoutineArgumentSet.AddableTypes.Select(t => new ComponentOption(t)).ToArray();
        var choose = new ComboBox { ItemsSource = addTypes, SelectedIndex = 0, MinWidth = 220 };
        var add = Ui.AsyncButton("Add component", async () =>
        {
            if (choose.SelectedItem is not ComponentOption option) return;
            if (routine.Arguments.Items.FirstOrDefault(a => a.TypeName == option.Type) is { } existing) { list.SelectedItem = existing; return; }
            string? group = RoutineArgumentSet.GetEditorGroup(option.Type);
            var conflicts = routine.Arguments.Items.Where(a => group is not null && RoutineArgumentSet.GetEditorGroup(a.TypeName) == group).ToArray();
            if (conflicts.Length > 0 && await Ui.Choose(Ui.CurrentWindow!, $"{RoutinePresentation.Name(option.Type)} replaces {string.Join(", ", conflicts.Select(a => RoutinePresentation.Name(a.TypeName)))}. The game allows one component from this group. Continue?", "Cancel", "Replace") != "Replace") return;
            foreach (var conflict in conflicts) { routine.Arguments.Items.Remove(conflict); if (cache.Remove(conflict, out Control? c)) state.Forget(c); }
            var item = routine.Arguments.Add(option.Type); Refresh(item); componentsChanged?.Invoke(); state.Changed();
        });
        var remove = Ui.Button("Remove", () => { if (list.SelectedItem is not RoutineArgumentRecord a) return; routine.Arguments.Items.Remove(a); if (cache.Remove(a, out Control? c)) state.Forget(c); Refresh(routine.Arguments.Items.FirstOrDefault()); componentsChanged?.Invoke(); state.Changed(); });
        if (compact) list.Height = 100;
        var panel = Ui.Dock(Ui.Row(choose, add, remove), compact ? Ui.Dock(list, detail) : Ui.Split(list, detail, 215)); Refresh(routine.Arguments.Items.FirstOrDefault()); return panel;
    }
    internal static Control ComponentFields(RoutineArgumentRecord argument, EditState state)
    {
        var f = new Fields(state); f.Heading(RoutinePresentation.Name(argument.TypeName), RoutinePresentation.Description(argument.TypeName));
        string[]? keys = RoutinePresentation.EditableFields(argument.TypeName);
        if (argument.Storage is { } storage)
        {
            if (keys is not null) foreach (string key in keys)
            {
                string label = RoutinePresentation.FieldName(key), help = RoutinePresentation.FieldHelp(key);
                if (key == "direction") f.Choice(label, storage.GetInt(key), ["Up", "Down", "Left", "Right"], n => storage.SetInt(key, n), help);
                else if (key == "targetTurns")
                    f.Number("Minutes", (storage.GetInt(key) / 2m).ToString(CultureInfo.InvariantCulture), text => { if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal minutes) || minutes < 0 || minutes > int.MaxValue / 2m || minutes * 2 != decimal.Truncate(minutes * 2)) return false; storage.SetInt(key, (int)(minutes * 2)); return true; }, help + " Half-minute values preserve odd turn counts.");
                else if (storage.Find(key)?.Kind == VssValueKind.Bool) f.Check(label, storage.GetBool(key), b => storage.SetBool(key, b), help);
                else f.Number(label, storage.GetInt(key).ToString(CultureInfo.InvariantCulture), text => { if (!int.TryParse(text, out int n) || n < 0) return false; storage.SetInt(key, n); return true; }, help);
            }
            if (argument.TypeName == "RoutineArgument_Patrol") f.Note("Edit waypoints in the map's Position tab. Outside the map, use ‘Edit on map’ on Destination & activity to get there. Manual coordinates work without loading a save.");
            var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = "Behavior", Content = Ui.Scroll(f.Panel) });
            tabs.Items.Add(new TabItem { Header = "Advanced / runtime", Content = DefinitionView.AdvancedFields(storage, state, (keys ?? []).Concat(new[] { "waypoints" }).ToArray()) }); return tabs;
        }
        f.Note("This component has no additional settings."); return Ui.Scroll(f.Panel);
    }
    private sealed record ComponentOption(string Type) { public override string ToString() => RoutinePresentation.Name(Type); }
}
