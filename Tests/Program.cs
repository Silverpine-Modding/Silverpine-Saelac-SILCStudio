using System.Text;
using SilverpineSilcEditor.Core;
using SilverpineSilcEditor.UI;

if (args.Length == 1 && Directory.Exists(args[0]))
{
    try
    {
        TestVssRoundTrip();
        TestKnowledgeCacheInvalidation();
        TestRoutineArguments();
        TestLuaLineEndings();
        TestPlayerEditorLoad(args[0], "Thomas");
        UiRegressionTests.Run(args[0]);
        string saveSample = Directory.GetFiles(args[0], "PlayerCharacter_*.silc")[0];
        TestSafeSave(saveSample, File.ReadAllBytes(saveSample));
        return ScanDirectory(args[0]);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception);
        return 1;
    }
}

if (args.Length != 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: SilverpineSilcEditor.SmokeTests <sample.silc|Definitions directory>");
    return 2;
}

try
{
    string path = Path.GetFullPath(args[0]);
    byte[] original = File.ReadAllBytes(path);
    SilcDocument document = SilcDocument.FromBytes(path, original);
    Check(document.Kind == SilcDefinitionKind.PlayerCharacter, "Sample type was not PlayerCharacter.");
    Check(document.IsLegacy, "Release 1.5.0 sample was not detected as legacy.");
    Check(document.Definition is not null, document.DefinitionParseError ?? "Definition was null.");

    byte[] legacyRoundTrip = document.BuildBytes();
    Check(original.AsSpan().SequenceEqual(legacyRoundTrip), "Legacy no-edit round trip was not byte-identical.");
    _ = SilcDocument.FromBytes(path, legacyRoundTrip);

    var player = new PlayerDefinitionModel(document.Definition!);
    Check(player.CanEdit, player.Error ?? "Player model did not parse.");
    Check(player.Images.Count(image => image.Category == PlayerImageCategory.Fixed) == 6, "Expected six fixed player sprites.");
    Check(player.Images.All(image => IsPng(image.Data)), "One or more embedded images were not PNG data.");

    string oldDescription = document.Definition!.GetString("description");
    document.Definition.SetString("description", oldDescription + " [smoke test]");
    byte[] edited = document.BuildBytes();
    SilcDocument editedDocument = SilcDocument.FromBytes(path, edited);
    Check(editedDocument.Definition!.GetString("description").EndsWith("[smoke test]", StringComparison.Ordinal),
        "Edited description did not survive a round trip.");

    editedDocument.SetVersionForTesting("Release 1.7.3", legacy: false);
    byte[] compressed = editedDocument.BuildBytes();
    SilcDocument compressedDocument = SilcDocument.FromBytes(path, compressed);
    Check(!compressedDocument.IsLegacy, "Modern SILC was not detected as Brotli.");
    Check(compressedDocument.Definition!.GetString("description").EndsWith("[smoke test]", StringComparison.Ordinal),
        "Compressed definition did not survive a round trip.");

    TestVssRoundTrip();
    TestKnowledgeCacheInvalidation();
    TestRoutineArguments();
    TestLuaLineEndings();
    UiRegressionTests.Run();
    TestSafeSave(path, original);
    Console.WriteLine($"PASS: {Path.GetFileName(path)} ({original.Length:N0} bytes), {player.Images.Count} embedded PNGs");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}

static void TestVssRoundTrip()
{
    var storage = new VssContainer();
    storage.Add("bool", VssValueKind.Bool, true);
    storage.Add("float", VssValueKind.Float, 1.25f);
    storage.Add("int", VssValueKind.Int, -42);
    storage.Add("string", VssValueKind.String, "Silverpine 🦊");
    storage.Add("color", VssValueKind.Color, new Rgba(1, 0.5f, 0.25f, 1));
    storage.Add("int2", VssValueKind.Vector2Int, new Int2(-10, 25));
    storage.Add("float2", VssValueKind.Vector2, new Float2(2.5f, -7.75f));
    storage.Add("blob", VssValueKind.Blob, new byte[] { 0, 1, 2, 255 });
    byte[] bytes = storage.ToBytes();
    Check(bytes.AsSpan().SequenceEqual(VssContainer.Parse(bytes).ToBytes()), "VSS round trip was not byte-identical.");
}

static void TestRoutineArguments()
{
    var fields = new VssContainer();
    fields.Add("interval", VssValueKind.Int, 4);
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
    {
        writer.Write(2);
        writer.Write("RoutineArgument_BeVendor");
        writer.Write("RoutineArgument_RandomWalk");
        fields.Write(writer);
    }
    byte[] bytes = stream.ToArray();
    RoutineArgumentSet parsed = RoutineArgumentSet.Parse(bytes);
    Check(parsed.CanEdit && parsed.Items.Count == 2, parsed.Error ?? "Routine argument parser failed.");
    Check(parsed.ToBytes().AsSpan().SequenceEqual(bytes), "Routine argument round trip was not byte-identical.");

    using var rawStream = new MemoryStream();
    using (var writer = new BinaryWriter(rawStream, Encoding.UTF8, leaveOpen: true))
    {
        writer.Write(3);
        writer.Write("RoutineArgument_Debug");
        writer.Write("RoutineArgument_Meeting");
        writer.Write("Market");
        writer.Write(false);
        writer.Write(12);
        writer.Write("RoutineArgument_SetNewTimedRoutineIfVendorNotActive");
        writer.Write(7);
    }
    byte[] rawBytes = rawStream.ToArray();
    RoutineArgumentSet rawParsed = RoutineArgumentSet.Parse(rawBytes);
    Check(rawParsed.CanEdit && rawParsed.Items.Count == 3, rawParsed.Error ?? "Raw routine argument parser failed.");
    Check(rawParsed.ToBytes().AsSpan().SequenceEqual(rawBytes), "Raw routine arguments were not byte-identical.");

    Check(RoutineArgumentSet.GetEditorGroup("RoutineArgument_FaceDirection") == "Direction",
        "FaceDirection editor group was incorrect.");
    Check(RoutineArgumentSet.GetEditorGroup("RoutineArgument_FacePoI") == "Direction",
        "FacePoI editor group was incorrect.");
    Check(RoutineArgumentSet.GetEditorGroup("RoutineArgument_BeVendor") is null,
        "Ungrouped routine argument was assigned a group.");
}

static void TestKnowledgeCacheInvalidation()
{
    KnowledgeRecord knowledge = KnowledgeRecord.Create();
    Check(knowledge.Name == "Unnamed", "New knowledge did not use the base-game default name.");

    var populatedWordCloud = new VssContainer();
    foreach (string key in new[] { "nouns", "verbs", "adjectives", "keywords" })
        populatedWordCloud.Add(key, VssValueKind.Blob, BinaryData.WriteStringList([key]));
    knowledge.Storage.SetBlob("wordCloud", populatedWordCloud.ToBytes());
    knowledge.ClearWordCloud();

    VssContainer cleared = VssContainer.Parse(knowledge.Storage.GetBlob("wordCloud")!);
    foreach (string key in new[] { "nouns", "verbs", "adjectives", "keywords" })
        Check(BinaryData.ReadStringList(cleared.GetBlob(key)!).Count == 0, $"Word-cloud field '{key}' was not cleared.");
}

static void TestLuaLineEndings()
{
    const string stored = "function GetTargetRoutineID(hour)\n    return \"silverpine_leisure\"\nend";
    string displayed = DefinitionEditors.NormalizeLuaForDisplay(stored);
    Check(displayed.Split([Environment.NewLine], StringSplitOptions.None).Length == 3,
        "LF-only Lua was not displayed on separate lines.");
    Check(DefinitionEditors.NormalizeLuaForStorage(displayed) == stored, "Lua line endings did not round-trip.");
    Check(DefinitionEditors.NormalizeLuaForDisplay(@"print('\n')") == @"print('\n')",
        "A literal escaped newline inside Lua was altered.");
}

static void TestPlayerEditorLoad(string definitionsDirectory, string playerName)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            string path = Path.Combine(definitionsDirectory, $"PlayerCharacter_{playerName}.silc");
            SilcDocument document = SilcDocument.Load(path);
            string[] npcNames = Directory.GetFiles(definitionsDirectory, "NPC_*.silc")
                .Select(file => Path.GetFileNameWithoutExtension(file)["NPC_".Length..])
                .Concat(NpcDefinitionModel.BaseGameNpcNames)
                .ToArray();
            string[] playerNames = Directory.GetFiles(definitionsDirectory, "PlayerCharacter_*.silc")
                .Select(file => Path.GetFileNameWithoutExtension(file)["PlayerCharacter_".Length..])
                .ToArray();
            EditorSurface surface = DefinitionEditors.Build(document, () => { }, npcNames, playerNames);
            surface.Control.Dispose();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null)
        throw new InvalidOperationException($"The {playerName} editor surface could not be constructed.", failure);
}

static void TestSafeSave(string sourcePath, byte[] original)
{
    string directory = Path.Combine(Path.GetTempPath(), "SilverpineSilcEditorTests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        string path = Path.Combine(directory, Path.GetFileName(sourcePath));
        File.WriteAllBytes(path, original);
        SilcDocument document = SilcDocument.Load(path);
        document.Definition!.SetString("description", "safe-save-test");
        string? backup = document.Save(createBackup: true);
        Check(backup is not null && File.Exists(backup), "Per-save backup was not created.");
        Check(SilcDocument.Load(path).Definition!.GetString("description") == "safe-save-test",
            "Atomically saved SILC did not contain the edit.");
        Check(File.ReadAllBytes(backup!).AsSpan().SequenceEqual(original), "Backup did not preserve the original bytes.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static int ScanDirectory(string directory)
{
    string[] paths = Directory.GetFiles(directory, "*.silc", SearchOption.TopDirectoryOnly);
    string[] playerNames = paths
        .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith("PlayerCharacter_", StringComparison.OrdinalIgnoreCase))
        .Select(path => Path.GetFileNameWithoutExtension(path)["PlayerCharacter_".Length..])
        .ToArray();
    int npcCount = 0;
    int playerCount = 0;
    int modelCount = 0;
    int unknownCount = 0;
    int opaqueArgumentStreams = 0;
    var failures = new List<string>();
    var validationIssues = new List<string>();

    foreach (string path in paths.Order(StringComparer.OrdinalIgnoreCase))
    {
        try
        {
            SilcDocument document = SilcDocument.Load(path);
            Check(document.Definition is not null, document.DefinitionParseError ?? "Definition was null.");
            byte[] rebuilt = document.BuildBytes();
            SilcDocument reparsed = SilcDocument.FromBytes(path, rebuilt);
            Check(reparsed.Kind == document.Kind, "Definition kind changed after rebuilding.");

            switch (document.Kind)
            {
                case SilcDefinitionKind.Npc:
                {
                    npcCount++;
                    var npc = new NpcDefinitionModel(document.Definition!);
                    Check(npc.Knowledge.CanEdit, npc.Knowledge.Error ?? "Knowledge did not parse.");
                    Check(npc.Relationships.CanEdit, npc.Relationships.Error ?? "Relationships did not parse.");
                    Check(npc.Routines.CanEdit, npc.Routines.Error ?? "Routines did not parse.");
                    VssContainer definition = document.Definition!;
                    try
                    {
                        npc.ValidateLikeBaseGame(
                            document.Name,
                            definition.GetBool("overrideExisting"),
                            definition.GetString("customPlayerCharacterDefinitionName"),
                            definition.GetString("publicCharacterDescription"),
                            definition.GetString("privateCharacterDescription"),
                            definition.GetString("personalityTraits"),
                            definition.GetString("species"),
                            definition.GetString("occupation"),
                            definition.GetBool("isDynamicRoutine"),
                            playerNames);
                    }
                    catch (InvalidDataException exception)
                    {
                        validationIssues.Add($"{Path.GetFileName(path)}: {exception.Message}");
                    }
                    opaqueArgumentStreams += npc.Routines.Items.Count(routine => !routine.Arguments.CanEdit);
                    break;
                }
                case SilcDefinitionKind.PlayerCharacter:
                {
                    playerCount++;
                    var player = new PlayerDefinitionModel(document.Definition!);
                    Check(player.CanEdit, player.Error ?? "Player assets did not parse.");
                    break;
                }
                case SilcDefinitionKind.ModelDefinition:
                    modelCount++;
                    break;
                default:
                    unknownCount++;
                    break;
            }
        }
        catch (Exception exception)
        {
            failures.Add($"{Path.GetFileName(path)}: {exception.Message}");
        }
    }

    foreach (string failure in failures)
        Console.Error.WriteLine("FAIL: " + failure);
    foreach (string issue in validationIssues)
        Console.WriteLine("EXISTING VALIDATION ISSUE: " + issue);
    Console.WriteLine($"SCAN: {paths.Length} files; {npcCount} NPC, {playerCount} player, {modelCount} model, {unknownCount} unknown; {opaqueArgumentStreams} opaque routine-argument streams; {validationIssues.Count} existing validation issues; {failures.Count} failures");
    return failures.Count == 0 ? 0 : 1;
}

static bool IsPng(byte[] bytes)
{
    ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
    return bytes.Length >= signature.Length && bytes.AsSpan(0, signature.Length).SequenceEqual(signature);
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
