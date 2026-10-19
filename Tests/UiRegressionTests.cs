using System.Drawing.Imaging;
using System.Reflection;
using System.Text;
using SilverpineSilcEditor.Core;
using SilverpineSilcEditor.UI;

internal static class UiRegressionTests
{
    internal static void Run(string? directory = null)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                TestPlayerPreservationAndValidation();
                TestAdvancedSynchronization();
                TestModelJson();
                TestDisposal();
                TestPortraitLayout();
                TestPortraitOffsets();
                if (directory is not null)
                {
                    TestInstalledPlayerRoundTrips(directory);
                    RenderPortraitPreview(directory);
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException("UI regression checks failed.", failure);
        Console.WriteLine("PASS: UI save validation, Advanced synchronization, numeric preservation, and control/image disposal");
        Console.WriteLine("PASS: portrait layout, mirroring, aspect ratio, numeric/drag/wheel/keyboard synchronization, image refresh, and preview-only preservation");
    }

    private static void TestPlayerPreservationAndValidation()
    {
        SilcDocument doc = PlayerFixture();
        byte[] original = doc.BuildBytes();
        EditorSurface surface = Build(doc);
        using (surface.Control)
        {
            surface.Apply();
            Equal(original, doc.BuildBytes(), "Untouched player fields or extension tails changed.");

            CheckedListBox sexes = Descendants(surface.Control).OfType<CheckedListBox>().Single();
            sexes.SetItemChecked(0, false);
            Reject(surface.Apply, "Empty allowed sexes were accepted.");
            Equal(original, doc.BuildBytes(), "A rejected selection changed the definition.");

            sexes.SetItemChecked(1, true);
            Reject(surface.Apply, "A sex without its small nude sprite was accepted.");
            var images = Descendants(surface.Control).OfType<PlayerImageEditor>().Single();
            images.ImportSmallNude(1, Png());
            surface.Apply();
            var player = new PlayerDefinitionModel(doc.Definition!);
            Check(player.AllowedSexes.SequenceEqual([1]), "Allowed sex was not saved.");
            Check(player.Images.Count(image => image.Category == PlayerImageCategory.SmallNude && image.NumericKey == 1) == 1,
                "Import did not add the selected sex's small nude sprite.");
            images.ImportSmallNude(1, Png());
            sexes.SetItemChecked(0, true);
            surface.Apply();
            player = new PlayerDefinitionModel(doc.Definition!);
            Check(player.AllowedSexes.SequenceEqual([1, 0]), "Adding a sex changed the primary NPC sex.");
            Check(player.Images.Count(image => image.Category == PlayerImageCategory.SmallNude && image.NumericKey == 1) == 1,
                "Importing a second PNG duplicated the small nude slot.");
        }

        doc = PlayerFixture(scale: 150, offsetX: 12000, offsetY: -12000);
        original = doc.BuildBytes();
        surface = Build(doc);
        using (surface.Control)
        {
            surface.Apply();
            Equal(original, doc.BuildBytes(), "Out-of-range numeric fields were clamped.");
            NumericUpDown scale = Field<NumericUpDown>(surface.Control, "Dialog sprite scale");
            Check(scale.Value == 150m && scale.Maximum >= 150m, "Existing scale is not shown accurately.");
            scale.Value = 145.123m;
            surface.Apply();
            Check(new PlayerDefinitionModel(doc.Definition!).Scale == (float)145.123m, "Edited scale was not stored.");
            scale.Value = 150m;
            surface.Apply();
            Equal(original, doc.BuildBytes(), "Restoring the displayed value did not restore the original float.");
        }
    }

    private static void TestAdvancedSynchronization()
    {
        SilcDocument player = PlayerFixture();
        EditorSurface surface = Build(player);
        using (surface.Control)
        {
            EditAdvanced(surface.Control, "description", "Advanced description");
            Check(Field<TextBox>(surface.Control, "Description").Text == "Advanced description", "Advanced did not update the regular field.");
            surface.Apply();
            Check(player.Definition!.GetString("description") == "Advanced description", "Saving discarded the Advanced description.");
            Field<TextBox>(surface.Control, "Description").Text = "Regular description";
            RefreshAdvanced(surface.Control);
            Check(AdvancedRow(surface.Control, "description").Cells[2].Value?.ToString() == "Regular description", "Advanced displayed stale regular text.");
            EditAdvanced(surface.Control, "description", "Second advanced description");
            surface.Apply();
            Check(player.Definition.GetString("description") == "Second advanced description", "Successive edits used a stale snapshot.");
        }

        SilcDocument npc = NpcFixture();
        surface = Build(npc);
        using (surface.Control)
        {
            surface.Apply();
            Check(npc.Definition!.GetInt2("spawnPosition") == new Int2(2_000_000, -2_000_000), "NPC coordinates were clamped.");
            Check(npc.Definition.GetInt("faction") == 99 && npc.Definition.GetInt("walkUpChance") == 88, "Unknown enum values were clamped.");
            EditAdvanced(surface.Control, "species", "fox");
            EditAdvanced(surface.Control, "spawnPosition", "3000000, -3000000");
            EditAdvanced(surface.Control, "faction", "42");
            EditAdvanced(surface.Control, "initialRelationshipLevel", "73");
            EditAdvanced(surface.Control, "customFlag", "True");
            EditAdvanced(surface.Control, "getTargetRoutineIDLuaScript", "return 'test'\n");
            surface.Apply();
            Check(npc.Definition.GetString("species") == "fox", "NPC Advanced text was discarded.");
            Check(npc.Definition.GetInt2("spawnPosition") == new Int2(3_000_000, -3_000_000), "NPC Advanced coordinates were discarded.");
            Check(npc.Definition.GetInt("faction") == 42, "NPC Advanced enum was discarded.");
            Check(npc.Definition.GetInt("initialRelationshipLevel") == 73, "Custom relationship level was discarded.");
            Check(npc.Definition.GetBool("customFlag"), "Unknown Advanced field was discarded.");
            Check(npc.Definition.GetString("getTargetRoutineIDLuaScript") == "return 'test'\n", "Advanced Lua was discarded.");
            EditAdvanced(surface.Control, "isDynamicRoutine", "True");
            surface.Apply();
            Check(npc.Definition.GetString("getTargetRoutineIDLuaScript") == "", "Advanced edits bypassed routine cache invalidation.");
        }
    }

    private static void TestModelJson()
    {
        var definition = new VssContainer();
        const string valid = "{\"modelName\":\"review\"}";
        definition.SetString("modelJson", valid);
        SilcDocument doc = Document("ModelDefinition", definition);
        definition = doc.Definition!;
        EditorSurface surface = Build(doc);
        using (surface.Control)
        {
            TextBox json = Field<TextBox>(surface.Control, "Model JSON");
            foreach (string invalid in new[] { "", "   ", "null", "[]", "123", "{broken" })
            {
                json.Text = invalid;
                Reject(surface.Apply, $"Invalid model JSON was accepted: {invalid}");
                Check(definition.GetString("modelJson") == valid, "Rejected JSON changed the stored model.");
            }
            EditAdvanced(surface.Control, "modelJson", "");
            Reject(surface.Apply, "Advanced bypassed model JSON validation.");
            const string updated = "{\"modelName\":\"updated\"}";
            EditAdvanced(surface.Control, "modelJson", updated);
            surface.Apply();
            Check(definition.GetString("modelJson") == updated, "Valid Advanced JSON was not saved.");
        }
    }

    private static void TestDisposal()
    {
        using var host = new Panel();
        for (int iteration = 0; iteration < 8; iteration++)
        {
            EditorSurface surface = Build(iteration % 2 == 0 ? PlayerFixture() : NpcFixture());
            host.Controls.Add(surface.Control);
            Control[] controls = Descendants(surface.Control).Prepend(surface.Control).ToArray();
            foreach (DataGridView grid in controls.OfType<DataGridView>())
            {
                grid.BindingContext = new BindingContext();
                _ = grid.Handle;
            }
            Image[] previews = controls.OfType<PictureBox>().Select(control => control.Image)
                .Concat(controls.OfType<PortraitOffsetCanvas>().Select(control => control.PortraitImage))
                .OfType<Image>().ToArray();
            Theme.DisposeChildren(host);
            Check(host.Controls.Count == 0, "Old editor remains attached.");
            Check(controls.All(control => control.IsDisposed && !control.IsHandleCreated), "Detached controls retained native handles.");
            foreach (Image preview in previews) AssertDisposed(preview);
        }
    }

    private static void TestInstalledPlayerRoundTrips(string directory)
    {
        int count = 0;
        foreach (string path in Directory.GetFiles(directory, "PlayerCharacter_*.silc"))
        {
            SilcDocument doc = SilcDocument.Load(path);
            byte[] original = doc.Definition!.ToBytes();
            EditorSurface surface = Build(doc);
            using (surface.Control)
                surface.Apply();
            Equal(original, doc.Definition.ToBytes(), $"No-edit UI apply changed {Path.GetFileName(path)}.");
            count++;
        }
        Console.WriteLine($"PASS: all {count} installed player definitions unchanged after no-edit UI apply (in memory only)");
    }

    private static void TestPortraitLayout()
    {
        Near(807.75f, PortraitLayout.CanvasHeight(16f / 9), "16:9 canvas height");
        PortraitPlacement player = PortraitLayout.Portrait(new Size(679, 1347), 807.75f, 0, 0, 1, false);
        Near(891.736505f, player.Bounds.X, "Player left edge from scene pivot");
        Near(130.815695f, player.Bounds.Y, "Player top edge from scene pivot");
        Near(679, player.Bounds.Width, "Player full frame width");
        Near(1347, player.Bounds.Height, "Player full frame height");
        Check(!player.FlipX && !player.FlipY, "Player unexpectedly mirrored.");
        PortraitPlacement npc = PortraitLayout.Portrait(new Size(679, 1347), 807.75f, 0, 0, 1, true);
        Near(-50.361915f, npc.Bounds.X, "NPC left edge with 177 - X and mirrored pivot");
        Near(131.090235f, npc.Bounds.Y, "NPC top edge from scene pivot");
        Check(npc.FlipX && !npc.FlipY, "NPC must be horizontally mirrored.");
        PortraitPlacement moved = PortraitLayout.Portrait(new Size(679, 1347), 807.75f, 25, 40, 1, true);
        Near(npc.Bounds.X - 25, moved.Bounds.X, "Stored NPC X direction");
        Near(npc.Bounds.Y - 40, moved.Bounds.Y, "Stored Y direction");
        PortraitPlacement square = PortraitLayout.Portrait(new Size(500, 500), 807.75f, 0, 0, 1, false);
        Near(679, square.Bounds.Height, "Square PNG must preserve aspect, not stretch to frame");
        PortraitPlacement tall = PortraitLayout.Portrait(new Size(100, 1000), 807.75f, 0, 0, 1, false);
        Near(134.7f, tall.Bounds.Width, "Tall PNG must be height-limited");
        PortraitPlacement wideScreen = PortraitLayout.Portrait(new Size(679, 1347), PortraitLayout.CanvasHeight(21f / 9), 0, 0, 1, false);
        Near(player.Bounds.X, wideScreen.Bounds.X, "Aspect change must not change logical X");
        Near(player.Bounds.Y - (807.75f - PortraitLayout.CanvasHeight(21f / 9)), wideScreen.Bounds.Y, "Bottom anchored at another aspect");
        Near(.750031f, PortraitLayout.Guides(807.75f).Y, "Native guide overlay top");
        Near(807, PortraitLayout.Guides(1000).Height, "Guide height must not stretch to viewport height");
        Check(PortraitLayout.DragOffset(new PointF(10, 20), new PointF(30, -15), .5f, false) == new PointF(70, 50), "Player drag coordinate conversion.");
        Check(PortraitLayout.DragOffset(new PointF(10, 20), new PointF(30, -15), .5f, true) == new PointF(-50, 50), "NPC drag coordinate conversion.");
    }

    private static void TestPortraitOffsets()
    {
        SilcDocument doc = PlayerFixture();
        byte[] original = doc.BuildBytes();
        EditorSurface surface = Build(doc);
        using (surface.Control)
        {
            var editor = Descendants(surface.Control).OfType<PortraitOffsetEditor>().Single();
            // Test a real-sized, detached editor because an unshown TabPage has zero layout area.
            using var detachedEditor = editor;
            editor.Parent!.Controls.Remove(editor);
            editor.Dock = DockStyle.None;
            PortraitOffsetCanvas canvas = editor.Canvas;
            editor.Size = new Size(1100, 760);
            editor.PerformLayout();
            var x = Field<NumericUpDown>(surface.Control, "Dialog sprite offset X");
            var y = Field<NumericUpDown>(surface.Control, "Dialog sprite offset Y");
            var scale = Field<NumericUpDown>(surface.Control, "Dialog sprite scale");
            var previewX = Descendants(editor).OfType<NumericUpDown>().Single(control => control.Name == "PortraitOffsetX");
            var mode = Descendants(editor).OfType<ComboBox>().Single(control => control.Name == "PortraitPreviewMode");
            var aspect = Descendants(editor).OfType<ComboBox>().Single(control => control.Name == "PortraitAspectRatio");
            mode.SelectedIndex = 1;
            aspect.SelectedIndex = 2;
            editor.RefreshPortrait();
            surface.Apply();
            Equal(original, doc.BuildBytes(), "Opening/changing the preview mutated SILC data.");
            Check(canvas.Npc && !canvas.ShowSilhouette, "NPC preview must hide the reference occupying its slot.");
            previewX.Value = 41.25m;
            Check(x.Value == 41.25m && canvas.Offset.X == 41.25f, "Visual numeric edit didn't reach Character/canvas.");
            x.Value = 19.75m;
            Check(previewX.Value == 19.75m, "Character edit didn't reach visual numeric field.");
            mode.SelectedIndex = 0;
            aspect.SelectedIndex = 0;
            x.Value = y.Value = 0;
            scale.Value = 1;
            RectangleF viewport = canvas.Viewport;
            Check(viewport.Width > 0, $"Portrait canvas has no layout area: editor {editor.Size}, canvas {canvas.Size}.");
            var start = new Point((int)(viewport.X + viewport.Width / 2), (int)(viewport.Y + viewport.Height / 2));
            Raise(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
            Raise(canvas, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, start.X + 40, start.Y - 20, 0));
            Raise(canvas, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, start.X + 40, start.Y - 20, 0));
            Near(40 / (viewport.Width / PortraitLayout.CanvasWidth), (float)x.Value, "Drag did not update X");
            Near(20 / (viewport.Width / PortraitLayout.CanvasWidth), (float)y.Value, "Drag did not update Y");
            Raise(canvas, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, start.X, start.Y, 120));
            Check(scale.Value == 1.05m, "Wheel did not update scale.");
            float beforeY = canvas.Offset.Y;
            Raise(canvas, "OnKeyDown", new KeyEventArgs(Keys.Shift | Keys.Up));
            Near(beforeY + 10, canvas.Offset.Y, "Arrow key movement");
            surface.Apply();
            var stored = new PlayerDefinitionModel(doc.Definition!);
            Near(canvas.Offset.X, stored.OffsetX, "Drag X not persisted");
            Near(canvas.Offset.Y, stored.OffsetY, "Drag Y not persisted");
            Near(1.05f, stored.Scale, "Wheel scale not persisted");
            Check(stored.Images.All(asset => asset.Data.AsSpan().SequenceEqual(Png())), "Moving a portrait modified image bytes.");
            Button restore = Descendants(editor).OfType<Button>().Single(button => button.Text == "Restore opened values");
            Raise(restore, "OnClick", EventArgs.Empty);
            surface.Apply();
            Equal(original, doc.BuildBytes(), "Restore lost original float precision or other SILC data.");

            // Replacing the model's Dialog PNG must refresh and dispose the previous bitmap.
            var model = (PlayerDefinitionModel)typeof(PortraitOffsetEditor).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            PlayerImageAsset dialog = model.Images.Single(asset => asset.Category == PlayerImageCategory.Fixed && asset.Name == "Dialog");
            Image old = canvas.PortraitImage!;
            using var png = new Bitmap(17, 31);
            using var stream = new MemoryStream();
            png.Save(stream, ImageFormat.Png);
            dialog.Data = stream.ToArray();
            editor.RefreshPortrait();
            Check(canvas.PortraitImage!.Size == new Size(17, 31), "Preview retained the old Dialog PNG.");
            AssertDisposed(old);
            Image current = canvas.PortraitImage;
            canvas.SetPortrait([0, 1, 2]);
            Check(canvas.PortraitImage is null && canvas.ImageError is not null, "Malformed PNG didn't fail safely.");
            AssertDisposed(current);
        }
    }

    private static void RenderPortraitPreview(string directory)
    {
        string path = Path.Combine(directory, "PlayerCharacter_Thomas.silc");
        if (!File.Exists(path)) return;
        EditorSurface surface = Build(SilcDocument.Load(path));
        using (surface.Control)
        {
            var editor = Descendants(surface.Control).OfType<PortraitOffsetEditor>().Single();
            // Render the actual WinForms control off-screen; never launch or modify the game.
            editor.Parent!.Controls.Remove(editor);
            using (editor)
            {
                editor.Dock = DockStyle.None;
                editor.Size = new Size(1180, 810);
                Theme.Style(editor);
                using var host = new PreviewRenderHost { ClientSize = editor.Size, FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000), ShowInTaskbar = false };
                host.Controls.Add(editor);
                host.Show();
                editor.PerformLayout();
                foreach (Control child in Descendants(editor).Prepend(editor)) _ = child.Handle;
                Application.DoEvents();
                foreach (ComboBox combo in Descendants(editor).OfType<ComboBox>())
                    Check(!string.IsNullOrEmpty(combo.Text), "Preview choice has no displayed text.");
                foreach (NumericUpDown number in Descendants(editor).OfType<NumericUpDown>())
                    Check(!string.IsNullOrEmpty(number.Text), "Preview number has no displayed text.");
                using var bitmap = new Bitmap(editor.Width, editor.Height);
                string output = Path.Combine(AppContext.BaseDirectory, "Portrait-preview-player.png");
                editor.DrawToBitmap(bitmap, editor.ClientRectangle);
                bitmap.Save(output, ImageFormat.Png);
                var mode = Descendants(editor).OfType<ComboBox>().Single(control => control.Name == "PortraitPreviewMode");
                mode.SelectedIndex = 1;
                editor.DrawToBitmap(bitmap, editor.ClientRectangle);
                bitmap.Save(Path.Combine(AppContext.BaseDirectory, "Portrait-preview-npc.png"), ImageFormat.Png);
                editor.Size = new Size(710, 470); // Approximate the main window's minimum right-pane size.
                editor.PerformLayout();
                Check(editor.Canvas.Height > 150, "Small window left no usable portrait canvas.");
                using var compact = new Bitmap(editor.Width, editor.Height);
                editor.DrawToBitmap(compact, editor.ClientRectangle);
                compact.Save(Path.Combine(AppContext.BaseDirectory, "Portrait-preview-compact.png"), ImageFormat.Png);
                host.Hide();
                Console.WriteLine("RENDER: " + output);
            }
        }
    }

    private static void Raise(Control control, string method, EventArgs args) =>
        control.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [args]);
    private static void Near(float expected, float actual, string message) => Check(Math.Abs(expected - actual) < .002f, $"{message}: expected {expected}, got {actual}.");
    private static void AssertDisposed(Image image)
    {
        try { _ = image.Width; }
        catch (Exception exception) when (exception is ArgumentException or ObjectDisposedException) { return; }
        throw new InvalidOperationException("Replaced preview bitmap was not disposed.");
    }

    private sealed class PreviewRenderHost : Form
    {
        protected override bool ShowWithoutActivation => true;
    }

    private static EditorSurface Build(SilcDocument doc) => DefinitionEditors.Build(doc, () => { }, NpcDefinitionModel.BaseGameNpcNames.ToArray(), ["test"]);

    private static IEnumerable<Control> Descendants(Control control) =>
        control.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private static T Field<T>(Control root, string label) where T : Control
    {
        Label match = Descendants(root).OfType<Label>().Single(control => control.Text == label);
        var panel = (TableLayoutPanel)match.Parent!;
        return (T)panel.GetControlFromPosition(1, panel.GetRow(match))!;
    }

    private static DataGridViewRow AdvancedRow(Control root, string key) => Descendants(root)
        .OfType<AdvancedStorageEditor>().SelectMany(editor => Descendants(editor).OfType<DataGridView>())
        .SelectMany(grid => grid.Rows.Cast<DataGridViewRow>()).Single(row => ((VssEntry)row.Tag!).Name == key);

    private static void EditAdvanced(Control root, string key, string value)
    {
        RefreshAdvanced(root);
        DataGridViewRow row = AdvancedRow(root, key);
        DataGridView grid = row.DataGridView!;
        _ = grid.Handle;
        grid.CurrentCell = row.Cells[2];
        Check(grid.BeginEdit(true), $"Could not start editing {key}.");
        ((TextBox)grid.EditingControl!).Text = value;
        Check(grid.EndEdit(), $"Advanced rejected {key}: {row.ErrorText}");
        grid.CurrentCell = row.Cells[0];
    }

    private static void RefreshAdvanced(Control root)
    {
        var tabs = (TabControl)root;
        tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "Advanced");
        AdvancedStorageEditor editor = Descendants(tabs.SelectedTab).OfType<AdvancedStorageEditor>().Single();
        typeof(Control).GetMethod("OnEnter", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, [EventArgs.Empty]);
    }

    private static SilcDocument PlayerFixture(float scale = 1.3998469f, float offsetX = -22.578491f, float offsetY = 58.032516f)
    {
        byte[] png = Png();
        var assets = new VssContainer();
        assets.SetFloat("dialogSpriteScale", scale);
        assets.SetFloat("dialogSpriteOffsetX", offsetX);
        assets.SetFloat("dialogSpriteOffsetY", offsetY);
        assets.SetBlob("sprites", Bytes(writer =>
        {
            for (int index = 0; index < 6; index++) BinaryData.WriteCountedBytes(writer, png);
        }));
        var definition = new VssContainer();
        definition.SetString("description", "Test player");
        definition.SetBlob("allowedSexes", BinaryData.WriteIntList([0]));
        definition.SetBlob("racials", BinaryData.WriteStringList([" foo ", "Bar"]));
        definition.SetBlob("smallNudeSprites", Bytes(writer =>
        {
            writer.Write(1);
            writer.Write(0);
            BinaryData.WriteCountedBytes(writer, png);
        }));
        definition.SetBlob("assets", [.. assets.ToBytes(), 0xca, 0xfe]);
        return Document("PlayerCharacter", definition);
    }

    private static SilcDocument NpcFixture()
    {
        var definition = new VssContainer();
        definition.SetBool("overrideExisting", true);
        definition.SetString("publicCharacterDescription", "%NAME% %SEX% %SPECIES%");
        definition.SetString("privateCharacterDescription", "%NAME% %SEX% %SPECIES%");
        definition.SetString("species", "wolf");
        definition.SetString("getTargetRoutineIDLuaScript", "return 'old'\n");
        definition.SetBool("isDynamicRoutine", false);
        definition.SetBool("customFlag", false);
        definition.SetInt2("spawnPosition", new Int2(2_000_000, -2_000_000));
        definition.SetInt("faction", 99);
        definition.SetInt("walkUpChance", 88);
        definition.SetInt("initialRelationshipLevel", 101);
        foreach (string key in new[] { "knowledgeEntries", "customNPCRelationships", "possibleRoutines" })
            definition.SetBlob(key, Bytes(writer => writer.Write(0)));
        return Document("NPC", definition);
    }

    private static SilcDocument Document(string type, VssContainer definition)
    {
        var envelope = new VssContainer();
        envelope.SetString("version", "Release 1.5.0");
        envelope.SetBlob("definition", Bytes(writer =>
        {
            writer.Write("CustomContentDefinition_" + type);
            definition.Write(writer);
            writer.Write(new byte[] { 0xde, 0xad });
        }));
        return SilcDocument.FromBytes(Path.Combine(Path.GetTempPath(), type + "_Review.silc"), [.. envelope.ToBytes(), 0xbe, 0xef]);
    }

    private static byte[] Png()
    {
        using var bitmap = new Bitmap(2, 2);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        write(writer);
        return stream.ToArray();
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Equal(byte[] expected, byte[] actual, string message) => Check(expected.AsSpan().SequenceEqual(actual), message);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
