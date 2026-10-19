using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using SilverpineSilcEditor.Core;

namespace SilverpineSilcEditor.UI;

internal sealed record EditorSurface(Control Control, Action Apply);

internal static class DefinitionEditors
{
    internal static EditorSurface Build(
        SilcDocument document,
        Action changed,
        IReadOnlyCollection<string> availableNpcNames,
        IReadOnlyCollection<string> availablePlayerNames)
    {
        if (document.Definition is null)
            return new EditorSurface(Message(document.DefinitionParseError ?? "This definition could not be parsed.", true), () => { });

        return document.Kind switch
        {
            SilcDefinitionKind.Npc => BuildNpc(
                document.Name,
                document.Definition,
                changed,
                availableNpcNames
                    .Where(name => !string.Equals(name, document.Name, StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
                availablePlayerNames),
            SilcDefinitionKind.PlayerCharacter => BuildPlayer(document.Definition, changed),
            SilcDefinitionKind.ModelDefinition => BuildModel(document.Definition, changed),
            _ => BuildUnknown(document.Definition, changed)
        };
    }

    private static EditorSurface BuildNpc(
        string npcName,
        VssContainer definition,
        Action changed,
        IReadOnlyCollection<string> availableNpcNames,
        IReadOnlyCollection<string> availablePlayerNames)
    {
        var model = new NpcDefinitionModel(definition);
        var tabs = Tabs();
        var fields = new FormFields(changed);
        fields.Section("Identity & availability");
        CheckBox enabled = fields.Check("Enabled", definition.GetBool("enabled", true));
        CheckBox overrideExisting = fields.Check("Override existing NPC", definition.GetBool("overrideExisting"));
        TextBox character = fields.Text("Character definition", definition.GetString("customPlayerCharacterDefinitionName"));
        character.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        character.AutoCompleteSource = AutoCompleteSource.CustomSource;
        character.AutoCompleteCustomSource.AddRange(availablePlayerNames.OrderBy(name => name).ToArray());
        fields.Section("Spawn & faction", "Coordinates use game tiles. Use the routine map to inspect locations in a save.");
        NumericUpDown spawnX = fields.Integer("Spawn X", definition.GetInt2("spawnPosition", new Int2(10, 0)).X);
        NumericUpDown spawnY = fields.Integer("Spawn Y", definition.GetInt2("spawnPosition", new Int2(10, 0)).Y);
        ComboBox faction = fields.Choice("Faction", ["None", "Other", "Silverpine", "Capital"], definition.GetInt("faction", 2));
        var descriptions = new FormFields(changed);
        descriptions.Section("Character profile", "Use %NAME%, %SEX%, and %SPECIES% where the game expects character placeholders.");
        TextBox species = descriptions.Text("Species", definition.GetString("species"));
        TextBox occupation = descriptions.Text("Occupation", definition.GetString("occupation"));
        TextBox personality = descriptions.Text("Personality traits", definition.GetString("personalityTraits"), true);
        descriptions.Section("Descriptions");
        TextBox publicDescription = descriptions.Text("Public description", definition.GetString("publicCharacterDescription"), true, 150);
        TextBox privateDescription = descriptions.Text("Private description", definition.GetString("privateCharacterDescription"), true, 150);
        descriptions.Section("Generated cache", "Changing knowledge invalidates this cache using the game's editor rules.");
        TextBox condensed = descriptions.Text("Condensed knowledge (generated)", definition.GetString("condensedKnowledgeString"), true);
        fields.Section("Player interaction");
        ComboBox walkUp = fields.Choice("Walk-up chance", ["None", "Low", "Normal", "High"], definition.GetInt("walkUpChance", 2));
        int storedInitialRelationship = definition.GetInt("initialRelationshipLevel");
        var initialRelationshipValues = new List<int> { 0, 14, 49, 79, 100 };
        var initialRelationshipLabels = new List<string> { "Stranger", "Acquaintance", "Friend", "Close Friend", "High" };
        int initialRelationshipIndex = initialRelationshipValues.IndexOf(storedInitialRelationship);
        if (initialRelationshipIndex < 0)
        {
            initialRelationshipIndex = initialRelationshipValues.Count;
            initialRelationshipValues.Add(storedInitialRelationship);
            initialRelationshipLabels.Add($"Custom ({storedInitialRelationship})");
        }
        ComboBox initialRelationship = fields.Choice("Initial player relationship", initialRelationshipLabels, initialRelationshipIndex);
        NumericUpDown knownDays = fields.Integer("Known player for days", definition.GetInt("knownPlayerForDays"), 0, 100000);
        CheckBox dynamicRoutine = fields.Check("Dynamic routine", definition.GetBool("isDynamicRoutine"));
        fields.Note("Generated knowledge and dynamic-routine caches are invalidated using the same rules as Silverpine's editor.");

        var luaFields = new FormFields(changed);
        string originalLua = definition.GetString("getTargetRoutineIDLuaScript");
        TextBox lua = luaFields.Text("Routine selector Lua", NormalizeLuaForDisplay(originalLua), true, 560);
        lua.AcceptsTab = true;
        lua.WordWrap = false;
        lua.ScrollBars = ScrollBars.Both;
        lua.Font = new Font("Cascadia Mono", 10f);
        bool luaChanged = false;
        lua.TextChanged += (_, _) => luaChanged = true;

        void InvalidateDynamicRoutineScript()
        {
            if (dynamicRoutine.Checked && lua.Text.Length > 0)
                lua.Clear();
        }

        void InvalidateKnowledgeCaches(KnowledgeRecord? entry)
        {
            entry?.ClearWordCloud();
            if (condensed.Text.Length > 0)
                condensed.Clear();
            InvalidateDynamicRoutineScript();
        }

        privateDescription.TextChanged += (_, _) => InvalidateDynamicRoutineScript();
        dynamicRoutine.CheckedChanged += (_, _) =>
        {
            if (dynamicRoutine.Checked)
                lua.Clear();
            lua.ReadOnly = dynamicRoutine.Checked;
        };
        lua.ReadOnly = dynamicRoutine.Checked;
        AddTab(tabs, "NPC", fields.Panel);
        AddTab(tabs, "Descriptions", descriptions.Panel);

        AddTab(tabs, "Knowledge", BuildKnowledge(model.Knowledge, npcName, InvalidateKnowledgeCaches, changed));
        AddTab(tabs, "Relationships", BuildRelationships(model.Relationships, changed, availableNpcNames));
        AddTab(tabs, "Routines", new RoutineEditor(model.Routines, InvalidateDynamicRoutineScript, changed));

        AddTab(tabs, "Lua", luaFields.Panel);
        var bindings = new Dictionary<string, StorageFieldBinding>
        {
            ["enabled"] = BindCheck(enabled),
            ["overrideExisting"] = BindCheck(overrideExisting),
            ["customPlayerCharacterDefinitionName"] = BindText(character),
            ["spawnPosition"] = new(
                () => new Int2((int)spawnX.Value, (int)spawnY.Value),
                value =>
                {
                    Int2 position = (Int2)value;
                    SetInteger(spawnX, position.X);
                    SetInteger(spawnY, position.Y);
                }),
            ["faction"] = BindChoice(faction),
            ["species"] = BindText(species),
            ["occupation"] = BindText(occupation),
            ["personalityTraits"] = BindText(personality),
            ["publicCharacterDescription"] = BindText(publicDescription),
            ["privateCharacterDescription"] = BindText(privateDescription),
            ["condensedKnowledgeString"] = BindText(condensed),
            ["walkUpChance"] = BindChoice(walkUp),
            ["initialRelationshipLevel"] = new(
                () => initialRelationshipValues[initialRelationship.SelectedIndex],
                value =>
                {
                    int index = initialRelationshipValues.IndexOf((int)value);
                    if (index < 0)
                    {
                        index = initialRelationshipValues.Count;
                        initialRelationshipValues.Add((int)value);
                    }
                    FormFields.SetChoiceValue(initialRelationship, index, $"Custom ({value})");
                }),
            ["knownPlayerForDays"] = new(() => (int)knownDays.Value, value => SetInteger(knownDays, (int)value)),
            ["isDynamicRoutine"] = BindCheck(dynamicRoutine),
            ["getTargetRoutineIDLuaScript"] = new(
                () => luaChanged ? NormalizeLuaForStorage(lua.Text) : originalLua,
                value =>
                {
                    if (dynamicRoutine.Checked)
                        throw new InvalidDataException("Disable Dynamic routine before editing its generated Lua script.");
                    lua.Text = NormalizeLuaForDisplay((string)value);
                })
        };
        AddTab(tabs, "Advanced", new AdvancedStorageEditor(definition, changed, bindings));

        return new EditorSurface(tabs, () =>
        {
            model.ValidateLikeBaseGame(
                npcName,
                overrideExisting.Checked,
                character.Text,
                publicDescription.Text,
                privateDescription.Text,
                personality.Text,
                species.Text,
                occupation.Text,
                dynamicRoutine.Checked,
                availablePlayerNames);
            definition.SetBool("enabled", enabled.Checked);
            definition.SetBool("overrideExisting", overrideExisting.Checked);
            definition.SetString("customPlayerCharacterDefinitionName", character.Text);
            definition.SetInt2("spawnPosition", new Int2((int)spawnX.Value, (int)spawnY.Value));
            definition.SetInt("faction", FormFields.ChoiceValue(faction));
            definition.SetString("species", species.Text);
            definition.SetString("occupation", occupation.Text);
            definition.SetString("personalityTraits", personality.Text);
            definition.SetString("publicCharacterDescription", publicDescription.Text);
            definition.SetString("privateCharacterDescription", privateDescription.Text);
            definition.SetString("condensedKnowledgeString", condensed.Text);
            definition.SetInt("walkUpChance", FormFields.ChoiceValue(walkUp));
            definition.SetInt("initialRelationshipLevel", initialRelationshipValues[initialRelationship.SelectedIndex]);
            definition.SetInt("knownPlayerForDays", (int)knownDays.Value);
            definition.SetBool("isDynamicRoutine", dynamicRoutine.Checked);
            definition.SetString("getTargetRoutineIDLuaScript", luaChanged ? NormalizeLuaForStorage(lua.Text) : originalLua);
            model.Apply();
        });
    }

    internal static string NormalizeLuaForDisplay(string value)
    {
        string lineFeedText = NormalizeLuaForStorage(value);
        return lineFeedText.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
    }

    internal static string NormalizeLuaForStorage(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static EditorSurface BuildPlayer(VssContainer definition, Action changed)
    {
        var model = new PlayerDefinitionModel(definition);
        var tabs = Tabs();
        var fields = new FormFields(changed);
        fields.Section("Character profile");
        TextBox description = fields.Text("Description", definition.GetString("description", "default"), true);
        fields.CharacterCount(description, 400);

        CheckedListBox sexes = fields.Checks("Allowed sexes", ["Male", "Female", "Intersex"], model.AllowedSexes);
        var racials = new RacialChecklist(model.Racials, changed);
        fields.Section("Portrait placement", "Use Portrait offset for the visual editor with the game's silhouette and guides.");
        NumericUpDown scale = fields.Decimal("Dialog sprite scale", model.Scale, 0.01m, 100m);
        NumericUpDown offsetX = fields.Decimal("Dialog sprite offset X", model.OffsetX);
        NumericUpDown offsetY = fields.Decimal("Dialog sprite offset Y", model.OffsetY);
        fields.Note("Select at least one sex. Each selected sex needs a matching small nude sprite in Images.");
        if (!model.CanEdit)
            fields.Note(model.Error ?? "Player assets are unavailable.", Theme.Danger);
        AddTab(tabs, "Character", fields.Panel);
        AddTab(tabs, "Racials", racials);
        AddTab(tabs, "Images", model.CanEdit ? new PlayerImageEditor(model, changed) : Message(model.Error!, true));
        AddTab(tabs, "Portrait offset", model.CanEdit ? new PortraitOffsetEditor(model, scale, offsetX, offsetY) : Message(model.Error!, true));
        AddTab(tabs, "Advanced", new AdvancedStorageEditor(definition, changed,
            new Dictionary<string, StorageFieldBinding> { ["description"] = BindText(description) }));

        return new EditorSurface(tabs, () =>
        {
            if (model.CanEdit)
            {
                // Keep the original order: the game uses the first entry for NPC sex.
                int[] checkedSexes = sexes.CheckedIndices.Cast<int>().ToArray();
                int[] selectedSexes = model.AllowedSexes.Where(checkedSexes.Contains)
                    .Concat(checkedSexes).Distinct().ToArray();
                model.ValidateAllowedSexes(selectedSexes);
                model.AllowedSexes.Clear();
                model.AllowedSexes.AddRange(selectedSexes);
                string[] selectedRacials = racials.SelectedIds();
                if (!model.Racials.SequenceEqual(selectedRacials))
                {
                    model.Racials.Clear();
                    model.Racials.AddRange(selectedRacials);
                }
                float scaleValue = ((FloatNumericUpDown)scale).FloatValue;
                float offsetXValue = ((FloatNumericUpDown)offsetX).FloatValue;
                float offsetYValue = ((FloatNumericUpDown)offsetY).FloatValue;
                if (scaleValue != model.Scale) model.Scale = scaleValue;
                if (offsetXValue != model.OffsetX) model.OffsetX = offsetXValue;
                if (offsetYValue != model.OffsetY) model.OffsetY = offsetYValue;
                model.Apply();
            }
            definition.SetString("description", description.Text);
        });
    }

    private static EditorSurface BuildModel(VssContainer definition, Action changed)
    {
        var tabs = Tabs();
        var fields = new FormFields(changed);
        fields.Note("Model definitions store their editable model configuration as JSON. The editor validates JSON syntax before saving.");
        TextBox json = fields.Text("Model JSON", definition.GetString("modelJson"), true, 520);
        json.AcceptsTab = true;
        json.WordWrap = false;
        json.ScrollBars = ScrollBars.Both;
        json.Font = new Font("Cascadia Mono", 9.5f);
        json.Height = 520;
        AddTab(tabs, "Model JSON", fields.Panel);
        AddTab(tabs, "Advanced", new AdvancedStorageEditor(definition, changed,
            new Dictionary<string, StorageFieldBinding> { ["modelJson"] = BindText(json) }));

        return new EditorSurface(tabs, () =>
        {
            ValidateModelJson(json.Text);
            definition.SetString("modelJson", json.Text);
        });
    }

    internal static void ValidateModelJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Model JSON can't be empty. Enter a JSON object before saving.");
        try
        {
            using var parsed = System.Text.Json.JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                throw new InvalidDataException("Model JSON must be an object, not null, an array, or a single value.");
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new InvalidDataException($"Model JSON is invalid: {exception.Message}", exception);
        }
    }

    private static StorageFieldBinding BindText(TextBox control) => new(() => control.Text, value => control.Text = (string)value);
    private static StorageFieldBinding BindCheck(CheckBox control) => new(() => control.Checked, value => control.Checked = (bool)value);
    private static StorageFieldBinding BindChoice(ComboBox control) => new(
        () => FormFields.ChoiceValue(control), value => FormFields.SetChoiceValue(control, (int)value));

    private static void SetInteger(NumericUpDown control, int value)
    {
        control.Minimum = Math.Min(control.Minimum, value);
        control.Maximum = Math.Max(control.Maximum, value);
        control.Value = value;
    }

    private static EditorSurface BuildUnknown(VssContainer definition, Action changed)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        var note = Message("This is not one of the three base-game definition types. Primitive fields may be edited below; binary fields are preserved read-only.", false);
        note.Dock = DockStyle.Top;
        note.Height = 65;
        var advanced = new AdvancedStorageEditor(definition, changed) { Dock = DockStyle.Fill };
        panel.Controls.Add(advanced);
        panel.Controls.Add(note);
        return new EditorSurface(panel, () => { });
    }

    private static Control BuildKnowledge(
        ParsedList<KnowledgeRecord> parsed,
        string npcName,
        Action<KnowledgeRecord?> invalidateCaches,
        Action changed)
    {
        if (!parsed.CanEdit)
            return Message(parsed.Error!, true);
        var binding = new BindingList<KnowledgeRecord>(parsed.Items);
        var originalText = parsed.Items.ToDictionary(
            item => item,
            item => (item.Name, item.Lore));
        DataGridView grid = Grid();
        DataGridViewTextBoxColumn nameColumn = TextColumn("Name", nameof(KnowledgeRecord.Name), 190);
        DataGridViewTextBoxColumn loreColumn = TextColumn("Lore", nameof(KnowledgeRecord.Lore), null, true);
        grid.Columns.Add(nameColumn);
        grid.Columns.Add(loreColumn);
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Public", DataPropertyName = nameof(KnowledgeRecord.IsPublic), Width = 75 });
        grid.DataSource = binding;
        grid.CellValueChanged += (_, eventArgs) =>
        {
            if (grid.IsDisposed || grid.Disposing)
                return;
            if (eventArgs.RowIndex >= 0
                && eventArgs.RowIndex < grid.Rows.Count
                && grid.Rows[eventArgs.RowIndex].DataBoundItem is KnowledgeRecord item
                && originalText.TryGetValue(item, out var original))
            {
                bool textChanged = item.Name != original.Name || item.Lore != original.Lore;
                if (textChanged)
                {
                    invalidateCaches(item);
                    originalText[item] = (item.Name, item.Lore);
                }
            }
            changed();
        };
        grid.CellEndEdit += (_, eventArgs) =>
        {
            if (grid.IsDisposed || grid.Disposing || eventArgs.RowIndex < 0
                || eventArgs.RowIndex >= grid.Rows.Count || grid.Rows[eventArgs.RowIndex].DataBoundItem is not KnowledgeRecord item)
                return;
            if (eventArgs.ColumnIndex == nameColumn.Index)
                item.Name = NormalizeKnowledgeText(item.Name, npcName, isLore: false);
            else if (eventArgs.ColumnIndex == loreColumn.Index)
                item.Lore = NormalizeKnowledgeText(item.Lore, npcName, isLore: true);
            else
                return;
            grid.Rows[eventArgs.RowIndex].Cells[eventArgs.ColumnIndex].Value =
                eventArgs.ColumnIndex == nameColumn.Index ? item.Name : item.Lore;
            if (originalText[item] != (item.Name, item.Lore))
            {
                invalidateCaches(item);
                originalText[item] = (item.Name, item.Lore);
                changed();
            }
        };
        grid.DataError += GridDataError;

        var toolbar = new ListEditorToolbar();
        toolbar.Controls.Add(Theme.Button("Add knowledge", (_, _) =>
        {
            KnowledgeRecord item = KnowledgeRecord.Create();
            originalText.Add(item, (item.Name, item.Lore));
            binding.Add(item);
            invalidateCaches(null);
            changed();
        }));
        toolbar.Controls.Add(Theme.Button("Remove selected", (_, _) =>
        {
            if (grid.CurrentRow?.DataBoundItem is not KnowledgeRecord item)
                return;
            binding.Remove(item);
            originalText.Remove(item);
            invalidateCaches(null);
            changed();
        }));
        return WithToolbar(grid, toolbar);
    }

    private static string NormalizeKnowledgeText(string text, string npcName, bool isLore)
    {
        if (!string.IsNullOrWhiteSpace(npcName))
        {
            string pattern = $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(npcName)}(?![\p{{L}}\p{{N}}_])";
            string replaced = Regex.Replace(text, pattern, "%NAME%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!string.Equals(replaced, text, StringComparison.Ordinal))
                return replaced;
        }
        if (!isLore)
            return text;

        (string Prefix, string Replacement)[] starts =
        [
            ("He's", "%NAME%'s"), ("She's", "%NAME%'s"), ("They're", "%NAME%'s"),
            ("He ", "%NAME% "), ("His ", "%NAME%'s "),
            ("She ", "%NAME% "), ("Her ", "%NAME%'s "),
            ("They ", "%NAME% "), ("Their ", "%NAME%'s ")
        ];
        foreach ((string prefix, string replacement) in starts)
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return replacement + text[prefix.Length..];
        return text;
    }

    private static Control BuildRelationships(
        ParsedList<RelationshipRecord> parsed,
        Action changed,
        IReadOnlyCollection<string> availableNpcNames)
    {
        if (!parsed.CanEdit)
            return Message(parsed.Error!, true);
        var binding = new BindingList<RelationshipRecord>(parsed.Items);
        DataGridView grid = Grid();
        DataGridViewTextBoxColumn targetColumn = TextColumn("Target NPC", nameof(RelationshipRecord.TargetName), 180);
        grid.Columns.Add(targetColumn);
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            HeaderText = "Relationship",
            DataPropertyName = nameof(RelationshipRecord.Relationship),
            Width = 145,
            DataSource = new[]
            {
                new ChoiceItem(0, "Stranger"), new ChoiceItem(1, "Acquaintance"),
                new ChoiceItem(2, "Friend"), new ChoiceItem(3, "Important")
            },
            ValueMember = nameof(ChoiceItem.Value),
            DisplayMember = nameof(ChoiceItem.Name)
        });
        grid.Columns.Add(TextColumn("Target is the… of self", nameof(RelationshipRecord.TargetRole), null, true));
        grid.Columns.Add(TextColumn("Self is the… of target", nameof(RelationshipRecord.SelfRole), null, true));
        grid.DataSource = binding;
        grid.CellValueChanged += (_, _) => changed();
        grid.DataError += GridDataError;
        grid.EditingControlShowing += (_, eventArgs) =>
        {
            if (eventArgs.Control is not TextBox textBox)
                return;

            textBox.AutoCompleteMode = AutoCompleteMode.None;
            textBox.AutoCompleteSource = AutoCompleteSource.None;
            textBox.AutoCompleteCustomSource = new AutoCompleteStringCollection();
            if (grid.CurrentCell?.OwningColumn != targetColumn)
                return;

            RelationshipRecord? current = grid.CurrentRow?.DataBoundItem as RelationshipRecord;
            var suggestions = new AutoCompleteStringCollection();
            suggestions.AddRange(GetAvailableRelationshipTargets(parsed.Items, availableNpcNames, current));
            textBox.AutoCompleteCustomSource = suggestions;
            textBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
            textBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        };

        var toolbar = new ListEditorToolbar();
        toolbar.Controls.Add(Theme.Button("Add relationship", (_, _) =>
        {
            RelationshipRecord item = RelationshipRecord.Create();
            string? firstAvailable = GetAvailableRelationshipTargets(parsed.Items, availableNpcNames, null).FirstOrDefault();
            if (firstAvailable is not null)
                item.TargetName = firstAvailable;
            binding.Add(item);
            changed();
        }));
        toolbar.Controls.Add(Theme.Button("Remove selected", (_, _) => RemoveSelected(grid, binding, changed)));
        toolbar.Controls.Add(new Label
        {
            Text = "Known NPCs autocomplete; future custom NPC names may be typed freely.",
            AutoSize = true,
            ForeColor = Theme.Muted,
            Margin = new Padding(12, 9, 3, 3)
        });
        return WithToolbar(grid, toolbar);
    }

    private static string[] GetAvailableRelationshipTargets(
        IEnumerable<RelationshipRecord> relationships,
        IEnumerable<string> availableNpcNames,
        RelationshipRecord? current)
    {
        HashSet<string> usedNames = relationships
            .Where(item => !ReferenceEquals(item, current) && !string.IsNullOrWhiteSpace(item.TargetName))
            .Select(item => item.TargetName.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return availableNpcNames
            .Where(name => !string.IsNullOrWhiteSpace(name) && !usedNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static TabControl Tabs() => new WorkspaceTabs();

    private static void AddTab(TabControl tabs, string title, Control content)
    {
        var page = new TabPage(title) { BackColor = Theme.Background, Padding = new Padding(0) };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        tabs.TabPages.Add(page);
    }

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        EditMode = DataGridViewEditMode.EditOnEnter
    };

    private static DataGridViewTextBoxColumn TextColumn(string title, string property, int? width, bool fill = false) => new()
    {
        HeaderText = title,
        DataPropertyName = property,
        Width = width ?? 100,
        AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None
    };

    private static Control WithToolbar(Control content, Control toolbar)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        content.Dock = DockStyle.Fill;
        toolbar.Dock = DockStyle.Top;
        panel.Controls.Add(content);
        panel.Controls.Add(toolbar);
        Theme.Style(panel);
        return panel;
    }

    private static Control Message(string message, bool error)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(28) };
        panel.Controls.Add(new Label
        {
            Text = message,
            Dock = DockStyle.Top,
            Height = 110,
            ForeColor = error ? Theme.Danger : Theme.Muted,
            Font = new Font("Segoe UI", 10.5f),
            Padding = new Padding(5)
        });
        return panel;
    }

    private static void RemoveSelected<T>(DataGridView grid, BindingList<T> binding, Action changed)
    {
        if (grid.CurrentRow?.DataBoundItem is not T item)
            return;
        binding.Remove(item);
        changed();
    }

    private static void GridDataError(object? sender, DataGridViewDataErrorEventArgs eventArgs)
    {
        eventArgs.ThrowException = false;
        if (sender is DataGridView grid && !grid.IsDisposed && !grid.Disposing
            && eventArgs.RowIndex >= 0 && eventArgs.RowIndex < grid.Rows.Count)
            grid.Rows[eventArgs.RowIndex].ErrorText = eventArgs.Exception?.Message ?? "Invalid value.";
    }

    private sealed record ChoiceItem(int Value, string Name);
}

internal sealed class PlayerImageEditor : UserControl
{
    private readonly PlayerDefinitionModel _model;
    private readonly Action _changed;
    private readonly ListBox _images;
    private readonly PictureBox _preview;
    private readonly Label _details;

    internal PlayerImageEditor(PlayerDefinitionModel model, Action changed)
    {
        _model = model;
        _changed = changed;
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 285, FixedPanel = FixedPanel.Panel1 };
        Theme.InitializeSplitter(split, 285);
        _images = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _images.Items.AddRange(model.Images.Cast<object>().ToArray());
        _images.SelectedIndexChanged += (_, _) => ShowSelected();
        split.Panel1.Padding = new Padding(10);
        split.Panel1.Controls.Add(_images);

        _preview = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(18, 20, 25)
        };
        _details = new Label { Dock = DockStyle.Top, Height = 42, ForeColor = Theme.Muted, Padding = new Padding(8) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), BackColor = Theme.Surface };
        buttons.Controls.Add(Theme.Button("Replace PNG", Replace));
        buttons.Controls.Add(Theme.Button("Export PNG", Export));
        buttons.Controls.Add(Theme.Button("Add expression", AddExpression));
        buttons.Controls.Add(Theme.Button("Remove expression", RemoveExpression));
        buttons.Controls.Add(Theme.Button("Add small nude", (_, _) =>
        {
            var menu = new ContextMenuStrip();
            for (int sex = 0; sex < 3; sex++)
            {
                int selectedSex = sex;
                menu.Items.Add(PlayerDefinitionModel.SexName(sex), null, (_, _) => AddSmallNude(selectedSex));
            }
            menu.Closed += (_, _) => menu.Dispose();
            menu.Show(this, PointToClient(Cursor.Position));
        }));
        split.Panel2.Controls.Add(_preview);
        split.Panel2.Controls.Add(_details);
        split.Panel2.Controls.Add(buttons);
        Controls.Add(split);
        Theme.Style(this);
        if (_images.Items.Count > 0)
            _images.SelectedIndex = 0;
    }

    private PlayerImageAsset? Selected => _images.SelectedItem as PlayerImageAsset;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Image? image = _preview.Image;
            _preview.Image = null;
            image?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void AddSmallNude(int sex)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            Title = $"Small nude sprite for {PlayerDefinitionModel.SexName(sex)}"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            ImportSmallNude(sex, File.ReadAllBytes(dialog.FileName));
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not import sprite", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal void ImportSmallNude(int sex, byte[] bytes)
    {
        if (sex is < 0 or > 2)
            throw new InvalidDataException("Choose Male, Female, or Intersex.");
        ValidatePng(bytes);
        PlayerImageAsset? item = _model.Images.FirstOrDefault(image => image.Category == PlayerImageCategory.SmallNude && image.NumericKey == sex);
        if (item is null)
        {
            item = new PlayerImageAsset(PlayerImageCategory.SmallNude, $"Small nude ({PlayerDefinitionModel.SexName(sex)})", sex, bytes);
            _model.Images.Add(item);
            _images.Items.Add(item);
        }
        else
            item.Data = bytes;
        _images.SelectedItem = item;
        ShowSelected();
        _changed();
    }

    private void ShowSelected()
    {
        _preview.Image?.Dispose();
        _preview.Image = null;
        if (Selected is not { } image)
        {
            _details.Text = "No image selected.";
            return;
        }
        try
        {
            using var stream = new MemoryStream(image.Data, writable: false);
            using Image loaded = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            _preview.Image = new Bitmap(loaded);
            _details.Text = $"{image.DisplayName}  ·  {loaded.Width} × {loaded.Height}  ·  {image.Data.Length:N0} bytes";
        }
        catch (Exception exception)
        {
            _details.Text = $"{image.DisplayName}  ·  Preview failed: {exception.Message}";
        }
    }

    private void Replace(object? sender, EventArgs eventArgs)
    {
        if (Selected is not { } image)
            return;
        using var dialog = new OpenFileDialog { Filter = "PNG image (*.png)|*.png", Title = $"Replace {image.DisplayName}" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        byte[] bytes = File.ReadAllBytes(dialog.FileName);
        ValidatePng(bytes);
        image.Data = bytes;
        _changed();
        ShowSelected();
    }

    private void Export(object? sender, EventArgs eventArgs)
    {
        if (Selected is not { } image)
            return;
        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = SanitizeFileName(image.Name) + ".png",
            Title = $"Export {image.DisplayName}"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            File.WriteAllBytes(dialog.FileName, image.Data);
    }

    private void AddExpression(object? sender, EventArgs eventArgs)
    {
        using var dialog = new OpenFileDialog { Filter = "PNG image (*.png)|*.png", Title = "Add expression" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        byte[] bytes = File.ReadAllBytes(dialog.FileName);
        ValidatePng(bytes);
        string? name = TextPromptDialog.Show(this, "Expression name", "Name stored in the SILC:", Path.GetFileNameWithoutExtension(dialog.FileName));
        if (string.IsNullOrWhiteSpace(name))
            return;
        var item = new PlayerImageAsset(PlayerImageCategory.Expression, name, _model.Images.Count, bytes);
        _model.Images.Add(item);
        _images.Items.Add(item);
        _images.SelectedItem = item;
        _changed();
    }

    private void RemoveExpression(object? sender, EventArgs eventArgs)
    {
        if (Selected is not { Category: PlayerImageCategory.Expression } image)
            return;
        _model.Images.Remove(image);
        _images.Items.Remove(image);
        _changed();
    }

    private static void ValidatePng(byte[] bytes)
    {
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < signature.Length || !bytes.AsSpan(0, signature.Length).SequenceEqual(signature))
            throw new InvalidDataException("The selected file is not a PNG image.");
        using var stream = new MemoryStream(bytes, writable: false);
        using Image _ = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char character in Path.GetInvalidFileNameChars())
            value = value.Replace(character, '_');
        return value;
    }
}

internal sealed class RoutineEditor : UserControl
{
    private readonly ParsedList<RoutineRecord> _parsed;
    private readonly Action _invalidateDynamicRoutineScript;
    private readonly Action _changed;
    private readonly BindingList<RoutineRecord>? _binding;
    private readonly DataGridView? _routines;
    private readonly ListBox? _arguments;
    private readonly Panel? _argumentFields;
    private readonly ComboBox? _addType;

    internal RoutineEditor(
        ParsedList<RoutineRecord> parsed,
        Action invalidateDynamicRoutineScript,
        Action changed)
    {
        _parsed = parsed;
        _invalidateDynamicRoutineScript = invalidateDynamicRoutineScript;
        _changed = changed;
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        if (!parsed.CanEdit)
        {
            Controls.Add(new Label { Text = parsed.Error, Dock = DockStyle.Fill, ForeColor = Theme.Danger, Padding = new Padding(25) });
            return;
        }

        _binding = new BindingList<RoutineRecord>(parsed.Items);
        _routines = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EditMode = DataGridViewEditMode.EditOnEnter
        };
        _routines.Columns.Add(Column("ID", nameof(RoutineRecord.Id), 145));
        _routines.Columns.Add(Column("Pathing activity", nameof(RoutineRecord.PathingActivity), 170));
        _routines.Columns.Add(Column("Activity", nameof(RoutineRecord.Activity), 170));
        _routines.Columns.Add(Column("Visible activity", nameof(RoutineRecord.VisibleActivity), 185));
        _routines.Columns.Add(Column("X", nameof(RoutineRecord.TargetX), 65));
        _routines.Columns.Add(Column("Y", nameof(RoutineRecord.TargetY), 65));
        _routines.DataSource = _binding;
        var originalIds = parsed.Items.ToDictionary(
            item => item,
            item => item.Id);
        _routines.CellValueChanged += (_, eventArgs) =>
        {
            if (IsDisposed || Disposing || _routines.IsDisposed || _routines.Disposing)
                return;
            if (eventArgs.RowIndex >= 0
                && eventArgs.RowIndex < _routines.Rows.Count
                && eventArgs.ColumnIndex == 0
                && _routines.Rows[eventArgs.RowIndex].DataBoundItem is RoutineRecord item
                && originalIds.TryGetValue(item, out string? originalId)
                && item.Id != originalId)
            {
                originalIds[item] = item.Id;
                _invalidateDynamicRoutineScript();
            }
            _changed();
        };
        _routines.SelectionChanged += (_, _) => RefreshArguments();
        _routines.DataError += (_, eventArgs) => eventArgs.ThrowException = false;

        var routineToolbar = new ListEditorToolbar();
        routineToolbar.Controls.Add(Theme.Button("Add routine", (_, _) =>
        {
            RoutineRecord item = RoutineRecord.Create();
            originalIds.Add(item, item.Id);
            _binding.Add(item);
            _routines.ClearSelection();
            _routines.Rows[^1].Selected = true;
            _invalidateDynamicRoutineScript();
            _changed();
        }));
        routineToolbar.Controls.Add(Theme.Button("Remove routine", (_, _) =>
        {
            if (_routines.CurrentRow?.DataBoundItem is RoutineRecord item)
            {
                _binding.Remove(item);
                originalIds.Remove(item);
                _invalidateDynamicRoutineScript();
                _changed();
            }
        }));

        var upper = new Panel { Dock = DockStyle.Fill };
        upper.Controls.Add(_routines);
        upper.Controls.Add(routineToolbar);

        _arguments = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _arguments.DisplayMember = nameof(RoutineArgumentRecord.ShortName);
        _arguments.SelectedIndexChanged += (_, _) => RefreshArgumentFields();
        _argumentFields = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        var argumentSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 260, FixedPanel = FixedPanel.Panel1 };
        Theme.InitializeSplitter(argumentSplit, 260);
        argumentSplit.Panel1.Controls.Add(_arguments);
        argumentSplit.Panel2.Controls.Add(_argumentFields);

        _addType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 270 };
        _addType.Items.AddRange(RoutineArgumentSet.AddableTypes.Cast<object>().ToArray());
        if (_addType.Items.Count > 0)
            _addType.SelectedIndex = 0;
        var argToolbar = new ListEditorToolbar();
        argToolbar.Controls.Add(_addType);
        argToolbar.Controls.Add(Theme.Button("Add argument", AddArgument));
        argToolbar.Controls.Add(Theme.Button("Remove argument", RemoveArgument));
        var lower = new Panel { Dock = DockStyle.Fill };
        lower.Controls.Add(argumentSplit);
        lower.Controls.Add(argToolbar);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 310 };
        Theme.InitializeSplitter(split, 310);
        split.Panel1.Controls.Add(upper);
        split.Panel2.Controls.Add(lower);
        Controls.Add(split);
        Theme.Style(this);
        if (_routines.Rows.Count > 0)
            _routines.Rows[0].Selected = true;
    }

    private RoutineRecord? SelectedRoutine => _routines?.CurrentRow?.DataBoundItem as RoutineRecord;
    private RoutineArgumentRecord? SelectedArgument => _arguments?.SelectedItem as RoutineArgumentRecord;

    private void RefreshArguments()
    {
        if (IsDisposed || Disposing || _arguments is null || _argumentFields is null)
            return;
        _arguments.Items.Clear();
        Theme.DisposeChildren(_argumentFields);
        RoutineRecord? routine = SelectedRoutine;
        if (routine is null)
            return;
        if (!routine.Arguments.CanEdit)
        {
            _argumentFields.Controls.Add(new Label
            {
                Text = routine.Arguments.Error,
                Dock = DockStyle.Fill,
                ForeColor = Theme.Danger,
                Padding = new Padding(18)
            });
            return;
        }
        _arguments.Items.AddRange(routine.Arguments.Items.Cast<object>().ToArray());
        if (_arguments.Items.Count > 0)
            _arguments.SelectedIndex = 0;
    }

    private void RefreshArgumentFields()
    {
        if (IsDisposed || Disposing || _argumentFields is null)
            return;
        Theme.DisposeChildren(_argumentFields);
        if (SelectedArgument is not { } argument)
            return;
        Control content = IsFaceDirection(argument)
            ? BuildFaceDirectionEditor(argument.Storage!)
            : argument.UsesStorage
                ? new AdvancedStorageEditor(argument.Storage!, _changed)
                : new Label
            {
                Text = "This argument type stores no serialized fields.",
                Dock = DockStyle.Fill,
                ForeColor = Theme.Muted,
                Padding = new Padding(20)
            };
        content.Dock = DockStyle.Fill;
        _argumentFields.Controls.Add(content);
    }

    private static bool IsFaceDirection(RoutineArgumentRecord argument) =>
        argument.UsesStorage
        && string.Equals(argument.TypeName, "RoutineArgument_FaceDirection", StringComparison.Ordinal);

    private Control BuildFaceDirectionEditor(VssContainer storage)
    {
        var fields = new FormFields(_changed);
        ComboBox direction = fields.Choice(
            "Direction",
            ["Up", "Down", "Left", "Right"],
            storage.GetInt("direction", 0));
        direction.SelectedIndexChanged += (_, _) => storage.SetInt("direction", FormFields.ChoiceValue(direction));
        fields.Note("Face direction is stored by the game as Up, Down, Left, or Right.");
        return fields.Panel;
    }

    private void AddArgument(object? sender, EventArgs eventArgs)
    {
        if (SelectedRoutine is not { } routine || !routine.Arguments.CanEdit || _addType?.SelectedItem is not string type)
            return;

        RoutineArgumentRecord? sameType = routine.Arguments.Items.FirstOrDefault(
            item => string.Equals(item.TypeName, type, StringComparison.Ordinal));
        if (sameType is not null)
        {
            _arguments!.SelectedItem = sameType;
            return;
        }

        string? group = RoutineArgumentSet.GetEditorGroup(type);
        if (group is not null)
        {
            foreach (RoutineArgumentRecord conflicting in routine.Arguments.Items
                         .Where(item => RoutineArgumentSet.GetEditorGroup(item.TypeName) == group)
                         .ToArray())
            {
                routine.Arguments.Items.Remove(conflicting);
                _arguments!.Items.Remove(conflicting);
            }
        }
        RoutineArgumentRecord item = routine.Arguments.Add(type);
        _arguments!.Items.Add(item);
        _arguments.SelectedItem = item;
        _changed();
    }

    private void RemoveArgument(object? sender, EventArgs eventArgs)
    {
        if (SelectedRoutine is not { } routine || SelectedArgument is not { } argument || !routine.Arguments.CanEdit)
            return;
        routine.Arguments.Items.Remove(argument);
        _arguments!.Items.Remove(argument);
        _changed();
    }

    private static DataGridViewTextBoxColumn Column(string title, string property, int width) => new()
    {
        HeaderText = title,
        DataPropertyName = property,
        Width = width
    };
}
