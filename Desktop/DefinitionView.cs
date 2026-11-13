using Avalonia.Controls;
using SilverpineSilcEditor.Core;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SilverpineSilcEditor.Desktop;

internal sealed class DefinitionView : TabControl, IDisposable
{
    protected override Type StyleKeyOverride => typeof(TabControl);
    private readonly EditState _state;
    private Action _apply = () => { };
    private readonly List<IDisposable> _resources = [];
    internal DefinitionView(SilcDocument doc, Action changed, string[] npcNames, string[] playerNames)
    {
        _state = new EditState(changed);
        if (doc.Definition is not { } d) { Add("Unavailable", Ui.Text(doc.DefinitionParseError ?? "Unknown definition.", 16, Ui.Danger)); return; }
        if (doc.Kind == SilcDefinitionKind.PlayerCharacter) Player(doc.Name, d);
        else if (doc.Kind == SilcDefinitionKind.Npc) Npc(doc.Name, d, npcNames, playerNames);
        else if (doc.Kind == SilcDefinitionKind.ModelDefinition)
        {
            var f = new Fields(_state); f.Heading("Model configuration", "The JSON is checked before saving.");
            f.Text("Model JSON", d.GetString("modelJson"), s => d.SetString("modelJson", s), true, code: true);
            Add("Model JSON", Ui.Scroll(f.Panel)); Add("Advanced", Advanced(d, ["modelJson"]));
            _apply = () => ValidateModelJson(d.GetString("modelJson"));
        }
        else Add("Advanced", Advanced(d));
    }
    internal void Apply() { _state.Validate(); _apply(); }
    private void Add(string title, Control content) => Items.Add(new TabItem { Header = title, Content = content });
    public void Dispose() { foreach (var resource in _resources) resource.Dispose(); _resources.Clear(); }

    private void Player(string name, VssContainer d)
    {
        var model = new PlayerDefinitionModel(d);
        var f = new Fields(_state); f.Heading("Character profile");
        f.Note($"Name comes from the filename: {name}\n" + GameEditorGuidance.Counter(name.Length, GameEditorGuidance.Name));
        f.Note("Base-game counters are advisory. Longer values stay intact and require acknowledgement when saving.");
        f.Text("Description", d.GetString("description"), s => d.SetString("description", s), true, gameLimit: GameEditorGuidance.Description);
        if (model.CanEdit)
        {
            f.Heading("Allowed sexes", "The base-game editor selects one sex. Multiple selections are allowed here. Each needs a matching Small nude sprite in Images; original ordering is retained.");
            var sexCount = Ui.Note(""); Fields.UpdateCounter(sexCount, model.AllowedSexes.Count, 1, "selected sexes"); f.Panel.Children.Add(sexCount);
            for (int sex = 0; sex < 3; sex++) { int key = sex; f.Check(PlayerDefinitionModel.SexName(key), model.AllowedSexes.Contains(key), value => { if (value && !model.AllowedSexes.Contains(key)) model.AllowedSexes.Add(key); if (!value) model.AllowedSexes.RemoveAll(s => s == key); Fields.UpdateCounter(sexCount, model.AllowedSexes.Count, 1, "selected sexes"); }); }
            if (model.AllowedSexes.Any(s => s < 0 || s > 2)) f.Note("Additional unrecognized sex values are preserved.");
            Add("Character", Ui.Scroll(f.Panel)); Add("Racials", Racials(model));
            var images = new ImageEditor(model, _state); _resources.Add(images); Add("Images", images);
            var portrait = new PortraitEditor(model, _state); _resources.Add(portrait); Add("Portrait offset", portrait);
        }
        else { f.Note(model.Error!); Add("Character", Ui.Scroll(f.Panel)); }
        Add("Advanced", Advanced(d, ["description", "allowedSexes", "racials", "assets", "smallNudeSprites", "dialogNudeSprite"]));
        _apply = model.Apply;
    }
    private Control Racials(PlayerDefinitionModel model)
    {
        string[] original = model.Racials.ToArray(); var checks = new Dictionary<string, CheckBox>(StringComparer.Ordinal);
        var list = new StackPanel { Spacing = 8 }; var filter = new TextBox { Watermark = "Search racial traits…" }; var count = Ui.Note("");
        void ApplySelection()
        {
            var selected = original.Where(id => checks[id].IsChecked == true).ToList();
            selected.AddRange(checks.Where(p => p.Value.IsChecked == true && !original.Contains(p.Key, StringComparer.Ordinal)).Select(p => p.Key));
            model.Racials.Clear(); model.Racials.AddRange(selected); count.Text = $"{checks.Count(p => p.Value.IsChecked == true)} selected"; _state.Changed();
        }
        void AddTrait(string id, string explanation, bool selected)
        {
            if (checks.ContainsKey(id)) return;
            var checkbox = new CheckBox { Content = id, IsChecked = selected }; checks[id] = checkbox;
            var card = Ui.Card(Ui.Stack(checkbox, Ui.Note(explanation)), 14); card.Tag = id + " " + explanation; list.Children.Add(card);
            checkbox.IsCheckedChanged += (_, _) => ApplySelection();
        }
        foreach (var trait in RacialCatalog.Traits) AddTrait(trait.Id, trait.Description, original.Contains(trait.Id, StringComparer.Ordinal));
        foreach (string id in original) AddTrait(id, "Custom or unrecognized trait. Its exact stored name is preserved.", true);
        count.Text = $"{checks.Count(p => p.Value.IsChecked == true)} selected";
        filter.TextChanged += (_, _) => { foreach (Control c in list.Children) c.IsVisible = ((string)c.Tag!).Contains(filter.Text ?? "", StringComparison.OrdinalIgnoreCase); };
        var custom = new TextBox { Watermark = "Custom racial name", MinWidth = 240 };
        var add = Ui.Button("Add custom", () => { string id = custom.Text?.Trim() ?? ""; if (id.Length == 0) return; AddTrait(id, "Custom trait. The game must recognize this name to apply its effect.", true); checks[id].IsChecked = true; ApplySelection(); custom.Clear(); });
        var panel = Ui.Stack(Ui.Text("Racial traits", 22, Ui.Accent), Ui.Note("Select the traits available in the game. Existing custom names, ordering, and duplicate entries are kept unless you change their selection."), filter, count, list, Ui.Row(custom, add)); panel.Margin = new Avalonia.Thickness(24);
        return Ui.Scroll(panel);
    }
    private void Npc(string name, VssContainer d, string[] npcNames, string[] playerNames)
    {
        var model = new NpcDefinitionModel(d); var f = new Fields(_state); var desc = new Fields(_state); var luaFields = new Fields(_state);
        var relationshipCount = Ui.Note("");
        void UpdateRelationshipCount()
        {
            int count = model.Relationships.Items.Count(r => r.Relationship == 3);
            Fields.UpdateCounter(relationshipCount, count, d.GetBool("overrideExisting") ? null : GameEditorGuidance.ImportantRelationships, "Important relationships");
            if (d.GetBool("overrideExisting")) relationshipCount.Text += " · no base-game count cap for overrides";
        }
        UpdateRelationshipCount();
        string originalLua = d.GetString("getTargetRoutineIDLuaScript");
        TextBox lua = luaFields.Text("Routine selector Lua", NormalizeLua(originalLua), text => d.SetString("getTargetRoutineIDLuaScript", NormalizeLua(text)), true, "Dynamic routines use generated Lua; disable Dynamic routine to write a selector yourself.", true);
        TextBox? condensed = null;
        void InvalidateScript() { if (d.GetBool("isDynamicRoutine")) { d.SetString("getTargetRoutineIDLuaScript", ""); lua.Text = ""; } }
        void InvalidateKnowledge(KnowledgeRecord? entry) { entry?.ClearWordCloud(); d.SetString("condensedKnowledgeString", ""); if (condensed is not null) condensed.Text = ""; InvalidateScript(); }
        f.Heading("Identity & availability");
        f.Note($"Name comes from the filename: {name}\n" + GameEditorGuidance.Counter(name.Length, GameEditorGuidance.Name));
        f.Note("Base-game counters are advisory, not hard caps. Exceeding them requires acknowledgement when saving.");
        f.Check("Enabled", d.GetBool("enabled", true), b => d.SetBool("enabled", b));
        f.Check("Override existing NPC", d.GetBool("overrideExisting"), b => { d.SetBool("overrideExisting", b); UpdateRelationshipCount(); });
        f.Auto("Character definition", d.GetString("customPlayerCharacterDefinitionName"), playerNames, s => d.SetString("customPlayerCharacterDefinitionName", s), gameLimit: GameEditorGuidance.Name);
        f.Heading("Spawn & faction");
        Int2 spawn = d.GetInt2("spawnPosition", new(10, 0));
        f.Int("Spawn X", spawn.X, x => { spawn = spawn with { X = x }; d.SetInt2("spawnPosition", spawn); });
        f.Int("Spawn Y", spawn.Y, y => { spawn = spawn with { Y = y }; d.SetInt2("spawnPosition", spawn); });
        f.Choice("Faction", d.GetInt("faction", 2), ["None", "Other", "Silverpine", "Capital"], v => d.SetInt("faction", v));
        f.Heading("Player interaction");
        f.Choice("Walk-up chance", d.GetInt("walkUpChance", 2), ["None", "Low", "Normal", "High"], v => d.SetInt("walkUpChance", v));
        f.Choice("Initial player relationship", d.GetInt("initialRelationshipLevel"), ["Stranger", "Acquaintance", "Friend", "Close Friend", "High"], v => d.SetInt("initialRelationshipLevel", v), values: [0, 14, 49, 79, 100]);
        f.Int("Known player for days", d.GetInt("knownPlayerForDays"), v => d.SetInt("knownPlayerForDays", v), gameMin: 0, gameMax: 99999);
        f.Check("Dynamic routine", d.GetBool("isDynamicRoutine"), b => { d.SetBool("isDynamicRoutine", b); lua.IsReadOnly = b; if (b) InvalidateScript(); }, "The game generates the routine selector from this NPC's profile and possible routines.");
        lua.IsReadOnly = d.GetBool("isDynamicRoutine");
        desc.Heading("Character profile", "Descriptions use %NAME%, %SEX%, and %SPECIES%. Separate personality traits with a comma and a space.");
        foreach (var (key, label) in new[] { ("species", "Species"), ("occupation", "Occupation"), ("personalityTraits", "Personality traits") }) desc.Text(label, d.GetString(key), s => d.SetString(key, s), gameLimit: key == "personalityTraits" ? GameEditorGuidance.Personality : GameEditorGuidance.Name);
        desc.Text("Public description", d.GetString("publicCharacterDescription"), s => d.SetString("publicCharacterDescription", s), true, gameLimit: GameEditorGuidance.Description);
        desc.Text("Private description", d.GetString("privateCharacterDescription"), s => { d.SetString("privateCharacterDescription", s); InvalidateScript(); }, true, gameLimit: GameEditorGuidance.PrivateDescription);
        condensed = desc.Text("Condensed knowledge (generated)", d.GetString("condensedKnowledgeString"), s => d.SetString("condensedKnowledgeString", s), true, "Editing knowledge clears this cache using the game's editor rules. The game can regenerate it.");
        Add("NPC", Ui.Scroll(f.Panel)); Add("Descriptions", Ui.Scroll(desc.Panel));
        Add("Knowledge", model.Knowledge.CanEdit ? new RecordEditor<KnowledgeRecord>(model.Knowledge.Items, item => item.Name, () => { InvalidateKnowledge(null); return KnowledgeRecord.Create(); }, item =>
        {
            var k = new Fields(_state); k.Heading("Knowledge entry", "Length limits below belong to the in-game editor, not the lore reader. The game can retrieve longer entries, selecting up to 3 of the NPC's entries per prompt. Its condensed summary uses only the first 16 entries containing %NAME%.");
            bool titleEdited = false, loreEdited = false;
            TextBox title = k.Text("Name", item.Name, s => { titleEdited = true; item.Name = s; InvalidateKnowledge(item); }, gameLimit: GameEditorGuidance.KnowledgeName);
            TextBox lore = k.Text("Lore", item.Lore, s => { loreEdited = true; item.Lore = s; InvalidateKnowledge(item); }, true, "Public lore must start with %NAME%.", gameLimit: GameEditorGuidance.KnowledgeLore);
            k.Check("Public", item.IsPublic, b => item.IsPublic = b);
            void Normalize(TextBox text, bool isLore) { string normalized = NormalizeKnowledge(text.Text ?? "", name, isLore); if (normalized != text.Text) text.Text = normalized; }
            title.LostFocus += (_, _) => { if (titleEdited) Normalize(title, false); }; lore.LostFocus += (_, _) => { if (loreEdited) Normalize(lore, true); };
            return Ui.Scroll(k.Panel);
        }, _state, () => InvalidateKnowledge(null), "knowledge entry") : Ui.Note(model.Knowledge.Error!));
        Add("Relationships", model.Relationships.CanEdit ? Ui.Dock(relationshipCount, new RecordEditor<RelationshipRecord>(model.Relationships.Items, r => r.TargetName, RelationshipRecord.Create, item =>
        {
            var r = new Fields(_state); r.Heading("Relationship", "Important relationships have no editor-imposed limit. You may also type the name of a custom NPC that does not exist yet.");
            var used = model.Relationships.Items.Where(i => i != item).Select(i => i.TargetName.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var auto = r.Auto("Target NPC", item.TargetName, npcNames.Where(n => !used.Contains(n) && !n.Equals(name, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Order(), s => item.TargetName = s, gameLimit: GameEditorGuidance.Name);
            auto.GotFocus += (_, _) => { var nowUsed = model.Relationships.Items.Where(i => i != item).Select(i => i.TargetName.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase); auto.ItemsSource = npcNames.Where(n => !nowUsed.Contains(n) && !n.Equals(name, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(); };
            r.Choice("Relationship", item.Relationship, ["Stranger", "Acquaintance", "Friend", "Important"], v => { item.Relationship = v; UpdateRelationshipCount(); });
            r.Text("Target is the… of self", item.TargetRole, s => item.TargetRole = s, gameLimit: GameEditorGuidance.Name);
            r.Text("Self is the… of target", item.SelfRole, s => item.SelfRole = s, gameLimit: GameEditorGuidance.Name);
            return Ui.Scroll(r.Panel);
        }, _state, () => { }, "relationship", UpdateRelationshipCount)) : Ui.Note(model.Relationships.Error!));
        Add("Routines", new RoutinesEditor(model.Routines, _state, InvalidateScript)); Add("Lua", Ui.Scroll(luaFields.Panel));
        Add("Advanced", Advanced(d, ["enabled", "overrideExisting", "customPlayerCharacterDefinitionName", "spawnPosition", "faction", "species", "occupation", "personalityTraits", "publicCharacterDescription", "privateCharacterDescription", "condensedKnowledgeString", "walkUpChance", "initialRelationshipLevel", "knownPlayerForDays", "isDynamicRoutine", "getTargetRoutineIDLuaScript", "possibleRoutines", "knowledgeEntries", "customNPCRelationships"]));
        _apply = () =>
        {
            model.ValidateLikeBaseGame(name, d.GetBool("overrideExisting"), d.GetString("customPlayerCharacterDefinitionName"), d.GetString("publicCharacterDescription"), d.GetString("privateCharacterDescription"), d.GetString("personalityTraits"), d.GetString("species"), d.GetString("occupation"), d.GetBool("isDynamicRoutine"), playerNames);
            model.Apply();
        };
    }
    internal Control Advanced(VssContainer storage, string[]? managed = null) => AdvancedFields(storage, _state, managed);
    internal static Control AdvancedFields(VssContainer storage, EditState state, string[]? managed = null)
    {
        var fields = new Fields(state); fields.Heading("Advanced fields", "Unrecognized fields are preserved. Binary data and fields managed by other tabs are shown read-only to prevent conflicting edits.");
        foreach (VssEntry entry in storage.Entries)
        {
            if (entry.Kind == VssValueKind.Blob || managed?.Contains(entry.Name) == true)
            {
                var box = new TextBox { Text = entry.Kind == VssValueKind.Blob ? entry.DisplayValue : "Managed by the main editor", IsReadOnly = true };
                fields.Add(entry.Name + " · " + entry.Kind, box); continue;
            }
            if (entry.Kind == VssValueKind.String) fields.Text(entry.Name, (string)entry.Value, s => entry.Value = s, ((string)entry.Value).Contains('\n'));
            else if (entry.Kind == VssValueKind.Bool) fields.Check(entry.Name, (bool)entry.Value, b => entry.Value = b);
            else fields.Number(entry.Name, entry.DisplayValue, text => storage.TrySetDisplayValue(entry, text, out _));
        }
        return Ui.Scroll(fields.Panel);
    }
    internal static string NormalizeLua(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    internal static void ValidateModelJson(string text)
    {
        using var json = JsonDocument.Parse(text);
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.EnumerateObject().Any()) throw new InvalidDataException("Model JSON must be a nonempty JSON object.");
    }
    internal static string NormalizeKnowledge(string text, string name, bool lore)
    {
        string result = Regex.Replace(text, $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}_])", "%NAME%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (result != text || !lore) return result;
        foreach (var (prefix, replacement) in new[] { ("He's", "%NAME%'s"), ("She's", "%NAME%'s"), ("They're", "%NAME%'s"), ("He ", "%NAME% "), ("His ", "%NAME%'s "), ("She ", "%NAME% "), ("Her ", "%NAME%'s "), ("They ", "%NAME% "), ("Their ", "%NAME%'s ") })
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return replacement + text[prefix.Length..];
        return text;
    }
}

internal sealed class RecordEditor<T> : UserControl where T : class
{
    private readonly ListBox _list = new();
    private readonly ContentControl _detail = new();
    internal T? Selected => _list.SelectedItem as T;
    internal void Select(T item) => _list.SelectedItem = item;
    internal RecordEditor(List<T> items, Func<T, string> title, Func<T> create, Func<T, Control> edit, EditState state, Action removed, string singular, Action? collectionChanged = null)
    {
        var cache = new Dictionary<T, Control>(); _detail.Content = Ui.Note("Choose or add a " + singular + ".");
        var labels = new Dictionary<T, TextBlock>();
        _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<T>((item, _) => { var label = Ui.Text(item is null ? "" : string.IsNullOrWhiteSpace(title(item)) ? "Untitled " + singular : title(item)); if (item is not null) labels[item] = label; return label; });
        _list.SelectionChanged += (_, _) =>
        {
            if (Selected is not { } item) { _detail.Content = Ui.Note("Choose or add a " + singular + "."); return; }
            if (!cache.TryGetValue(item, out Control? c)) cache[item] = c = edit(item); _detail.Content = c;
        };
        void Refresh(T? selected) { _list.ItemsSource = null; _list.ItemsSource = items.ToArray(); _list.SelectedItem = selected; collectionChanged?.Invoke(); }
        var toolbar = Ui.Row(Ui.Button("Add " + singular, () => { T item = create(); items.Add(item); Refresh(item); state.Changed(); }), Ui.Button("Remove", () => { if (Selected is not { } item) return; items.Remove(item); if (cache.Remove(item, out Control? c)) state.Forget(c); removed(); Refresh(items.FirstOrDefault()); state.Changed(); }));
        _detail.LostFocus += (_, _) => { foreach (var (item, label) in labels) label.Text = string.IsNullOrWhiteSpace(title(item)) ? "Untitled " + singular : title(item); };
        Content = Ui.Dock(toolbar, Ui.Split(_list, _detail, 215)); Refresh(items.FirstOrDefault());
    }
}
