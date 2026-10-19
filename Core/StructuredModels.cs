using System.Text;

namespace SilverpineSilcEditor.Core;

public sealed class NpcDefinitionModel
{
    public static IReadOnlyList<string> BaseGameNpcNames { get; } =
    [
        "Hayden", "Rosalyn", "Aldric", "Gareth", "Celandine",
        "Oriana", "Darian", "Mirel", "Acacia", "Orson"
    ];

    public NpcDefinitionModel(VssContainer definition)
    {
        Definition = definition;
        Knowledge = ReadList(definition.GetBlob("knowledgeEntries"), KnowledgeRecord.Read, "knowledge entries");
        Relationships = ReadList(definition.GetBlob("customNPCRelationships"), RelationshipRecord.Read, "relationships");
        Routines = ReadList(definition.GetBlob("possibleRoutines"), RoutineRecord.Read, "routines");
    }

    public VssContainer Definition { get; }
    public ParsedList<KnowledgeRecord> Knowledge { get; }
    public ParsedList<RelationshipRecord> Relationships { get; }
    public ParsedList<RoutineRecord> Routines { get; }

    public void Apply()
    {
        if (Knowledge.CanEdit)
            Definition.SetBlob("knowledgeEntries", WriteList(Knowledge.Items, static (writer, item) => item.Storage.Write(writer)));
        if (Relationships.CanEdit)
            Definition.SetBlob("customNPCRelationships", WriteList(Relationships.Items, static (writer, item) => item.Storage.Write(writer)));
        if (Routines.CanEdit)
        {
            foreach (RoutineRecord routine in Routines.Items)
                routine.Apply();
            Definition.SetBlob("possibleRoutines", WriteList(Routines.Items, static (writer, item) => item.Storage.Write(writer)));
        }
    }

    public void ValidateLikeBaseGame(
        string npcName,
        bool overrideExisting,
        string playerDefinitionName,
        string publicDescription,
        string privateDescription,
        string personalityTraits,
        string species,
        string occupation,
        bool dynamicRoutine,
        IReadOnlyCollection<string> availablePlayerNames)
    {
        bool PlayerDefinitionExists() => availablePlayerNames.Contains(playerDefinitionName, StringComparer.OrdinalIgnoreCase);
        bool ContainsDescriptionVariables(string value) =>
            value.Contains("%NAME%", StringComparison.Ordinal)
            && value.Contains("%SEX%", StringComparison.Ordinal)
            && value.Contains("%SPECIES%", StringComparison.Ordinal);

        if (!overrideExisting && !PlayerDefinitionExists())
            throw new InvalidDataException($"Custom player character definition '{playerDefinitionName}' doesn't exist.");
        if ((!overrideExisting || privateDescription.Length > 0) && publicDescription.Length == 0)
            throw new InvalidDataException("Public character description can't be empty.");
        if ((!overrideExisting || publicDescription.Length > 0) && privateDescription.Length == 0)
            throw new InvalidDataException("Private character description can't be empty.");
        if ((!overrideExisting || privateDescription.Length > 0) && !ContainsDescriptionVariables(publicDescription))
            throw new InvalidDataException("Public character description must contain %NAME%, %SEX%, and %SPECIES%.");
        if ((!overrideExisting || publicDescription.Length > 0) && !ContainsDescriptionVariables(privateDescription))
            throw new InvalidDataException("Private character description must contain %NAME%, %SEX%, and %SPECIES%.");

        KnowledgeRecord? invalidKnowledgeName = Knowledge.Items.FirstOrDefault(item => item.Name.Length == 0);
        if (invalidKnowledgeName is not null)
            throw new InvalidDataException("Knowledge entry name can't be empty.");
        KnowledgeRecord? invalidKnowledgeLore = Knowledge.Items.FirstOrDefault(item => item.Lore.Length == 0);
        if (invalidKnowledgeLore is not null)
            throw new InvalidDataException($"Knowledge entry '{invalidKnowledgeLore.Name}' content can't be empty.");
        KnowledgeRecord? invalidPublicKnowledge = Knowledge.Items.FirstOrDefault(
            item => item.IsPublic && !item.Lore.StartsWith("%NAME%", StringComparison.Ordinal));
        if (invalidPublicKnowledge is not null)
            throw new InvalidDataException($"Public knowledge entry '{invalidPublicKnowledge.Name}' must start with %NAME%.");

        if ((!overrideExisting || personalityTraits.Length > 0)
            && personalityTraits.Split(", ", StringSplitOptions.RemoveEmptyEntries).Length < 2)
            throw new InvalidDataException("At least two personality traits must be defined, separated by ', '.");
        if (!overrideExisting && species.Length == 0)
            throw new InvalidDataException("Species can't be empty.");
        if (!overrideExisting && occupation.Length == 0)
            throw new InvalidDataException("Occupation can't be empty.");

        RelationshipRecord? blankTarget = Relationships.Items.FirstOrDefault(item => item.TargetName.Length == 0);
        if (!overrideExisting && blankTarget is not null)
            throw new InvalidDataException("Relationship target can't be empty.");
        RelationshipRecord? blankTargetRole = Relationships.Items.FirstOrDefault(item => item.TargetRole.Length == 0);
        if (!overrideExisting && blankTargetRole is not null)
            throw new InvalidDataException("Target relationship designation can't be empty.");
        RelationshipRecord? blankSelfRole = Relationships.Items.FirstOrDefault(item => item.SelfRole.Length == 0);
        if (!overrideExisting && blankSelfRole is not null)
            throw new InvalidDataException("Self relationship designation can't be empty.");
        if (!overrideExisting && Relationships.Items.Any(
                item => string.Equals(item.TargetName, npcName, StringComparison.Ordinal)))
            throw new InvalidDataException("NPC can't have self as relationship target.");

        IGrouping<string, RelationshipRecord>? duplicateTarget = Relationships.Items
            .GroupBy(item => item.TargetName, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateTarget is not null)
            throw new InvalidDataException($"Duplicate relationship target '{duplicateTarget.Key}'.");

        if (!overrideExisting && Routines.Items.Any(item => item.Id.Length == 0))
            throw new InvalidDataException("Routine ID can't be empty.");
        if (!overrideExisting && Routines.Items.Any(item => item.Id == "silverpine_leisure"))
            throw new InvalidDataException("Routine ID can't be 'silverpine_leisure'.");
        IGrouping<string, RoutineRecord>? duplicateRoutine = Routines.Items
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateRoutine is not null)
            throw new InvalidDataException($"Duplicate routine '{duplicateRoutine.Key}'.");
        if (!overrideExisting && Routines.Items.Any(
                item => item.PathingActivity.Length == 0 || item.Activity.Length == 0 || item.VisibleActivity.Length == 0))
            throw new InvalidDataException("Routines can't have empty activity fields.");
        if (!overrideExisting && dynamicRoutine && Routines.Items.Count < 3)
            throw new InvalidDataException("At least three possible routines must be defined when using dynamic mode.");
    }

    private static ParsedList<T> ReadList<T>(byte[]? blob, Func<BinaryReader, T> read, string label)
    {
        if (blob is null)
            return new ParsedList<T>([], false, $"The definition does not contain {label}.");
        try
        {
            using var stream = new MemoryStream(blob, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            int count = BinaryData.ReadSafeCount(reader, 100_000);
            var items = new List<T>(count);
            for (int index = 0; index < count; index++)
                items.Add(read(reader));
            BinaryData.RequireEnd(stream);
            return new ParsedList<T>(items, true, null);
        }
        catch (Exception exception)
        {
            return new ParsedList<T>([], false, $"Could not parse {label}: {exception.Message}");
        }
    }

    private static byte[] WriteList<T>(IReadOnlyCollection<T> items, Action<BinaryWriter, T> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(items.Count);
        foreach (T item in items)
            write(writer, item);
        return stream.ToArray();
    }
}

public sealed class ParsedList<T>(List<T> items, bool canEdit, string? error)
{
    public List<T> Items { get; } = items;
    public bool CanEdit { get; } = canEdit;
    public string? Error { get; } = error;
}

public sealed class KnowledgeRecord
{
    private KnowledgeRecord(VssContainer storage) => Storage = storage;

    public VssContainer Storage { get; }
    public string Name { get => Storage.GetString("entryName", "Unnamed"); set => Storage.SetString("entryName", value); }
    public string Lore { get => Storage.GetString("lore"); set => Storage.SetString("lore", value); }
    public bool IsPublic { get => Storage.GetBool("isPublic"); set => Storage.SetBool("isPublic", value); }

    public void ClearWordCloud() => Storage.SetBlob("wordCloud", CreateEmptyWordCloud());

    public static KnowledgeRecord Read(BinaryReader reader) => new(VssContainer.Read(reader));

    public static KnowledgeRecord Create()
    {
        var storage = new VssContainer();
        storage.Add("entryName", VssValueKind.String, "Unnamed");
        storage.Add("lore", VssValueKind.String, "");
        storage.Add("isPublic", VssValueKind.Bool, false);
        storage.Add("wordCloud", VssValueKind.Blob, CreateEmptyWordCloud());
        return new KnowledgeRecord(storage);
    }

    private static byte[] CreateEmptyWordCloud()
    {
        var wordCloud = new VssContainer();
        foreach (string key in new[] { "nouns", "verbs", "adjectives", "keywords" })
            wordCloud.Add(key, VssValueKind.Blob, BinaryData.WriteStringList([]));
        return wordCloud.ToBytes();
    }
}

public sealed class RelationshipRecord
{
    private RelationshipRecord(VssContainer storage) => Storage = storage;

    public VssContainer Storage { get; }
    public string TargetName { get => Storage.GetString("targetName"); set => Storage.SetString("targetName", value); }
    public int Relationship { get => Storage.GetInt("npcRelationship", 1); set => Storage.SetInt("npcRelationship", value); }
    public string TargetRole { get => Storage.GetString("targetIsTheXXXOfSelf"); set => Storage.SetString("targetIsTheXXXOfSelf", value); }
    public string SelfRole { get => Storage.GetString("selfIsTheXXXOfTarget"); set => Storage.SetString("selfIsTheXXXOfTarget", value); }

    public static RelationshipRecord Read(BinaryReader reader) => new(VssContainer.Read(reader));

    public static RelationshipRecord Create()
    {
        var storage = new VssContainer();
        storage.Add("targetName", VssValueKind.String, "");
        storage.Add("npcRelationship", VssValueKind.Int, 1);
        storage.Add("targetIsTheXXXOfSelf", VssValueKind.String, "");
        storage.Add("selfIsTheXXXOfTarget", VssValueKind.String, "");
        return new RelationshipRecord(storage);
    }
}

public sealed class RoutineRecord
{
    private RoutineRecord(VssContainer storage)
    {
        Storage = storage;
        Arguments = RoutineArgumentSet.Parse(storage.GetBlob("routineArguments"));
    }

    public VssContainer Storage { get; private set; }
    public RoutineArgumentSet Arguments { get; private set; }
    public string Id { get => Storage.GetString("id", "new_routine"); set => Storage.SetString("id", value); }
    public string PathingActivity { get => Storage.GetString("pathingActivity"); set => Storage.SetString("pathingActivity", value); }
    public string Activity { get => Storage.GetString("activity"); set => Storage.SetString("activity", value); }
    public string VisibleActivity { get => Storage.GetString("visibleActivity"); set => Storage.SetString("visibleActivity", value); }
    public int TargetX { get => Storage.GetInt2("targetPosition").X; set => Storage.SetInt2("targetPosition", new Int2(value, TargetY)); }
    public int TargetY { get => Storage.GetInt2("targetPosition").Y; set => Storage.SetInt2("targetPosition", new Int2(TargetX, value)); }

    public static RoutineRecord Read(BinaryReader reader) => new(VssContainer.Read(reader));

    internal RoutineRecord CreateDraft()
    {
        var storage = VssContainer.Parse(Storage.ToBytes());
        if (Arguments.CanEdit) storage.SetBlob("routineArguments", Arguments.ToBytes());
        return new RoutineRecord(storage);
    }
    internal void AcceptDraft(RoutineRecord draft)
    {
        var copy = draft.CreateDraft();
        Storage = copy.Storage; Arguments = copy.Arguments;
    }

    public static RoutineRecord Create()
    {
        var storage = new VssContainer();
        storage.Add("id", VssValueKind.String, "");
        storage.Add("pathingActivity", VssValueKind.String, "");
        storage.Add("activity", VssValueKind.String, "");
        storage.Add("visibleActivity", VssValueKind.String, "");
        storage.Add("targetPosition", VssValueKind.Vector2Int, new Int2(0, 0));
        storage.Add("routineArguments", VssValueKind.Blob, RoutineArgumentSet.EmptyBytes());
        return new RoutineRecord(storage);
    }

    public void Apply()
    {
        if (Arguments.CanEdit)
            Storage.SetBlob("routineArguments", Arguments.ToBytes());
    }
}

public enum RoutineArgumentEncoding
{
    Empty,
    VersionSafeStorage,
    MeetingRaw,
    IntRaw
}

public sealed class RoutineArgumentRecord(string typeName, RoutineArgumentEncoding encoding, VssContainer? storage)
{
    public string TypeName { get; } = typeName;
    public string ShortName => TypeName.StartsWith("RoutineArgument_", StringComparison.Ordinal)
        ? TypeName["RoutineArgument_".Length..]
        : TypeName;
    public RoutineArgumentEncoding Encoding { get; } = encoding;
    public bool UsesStorage => Encoding != RoutineArgumentEncoding.Empty;
    public VssContainer? Storage { get; } = storage;
}

public sealed class RoutineArgumentSet
{
    private static readonly HashSet<string> EmptyTypes = new(StringComparer.Ordinal)
    {
        "RoutineArgument_AcaciaDetect",
        "RoutineArgument_AnswerDoor",
        "RoutineArgument_BeVendor",
        "RoutineArgument_Debug",
        "RoutineArgument_EndOverrideIf_TurnOnLightsDone",
        "RoutineArgument_FacePoI",
        "RoutineArgument_LockAllDoors",
        "RoutineArgument_LockOwnedDoors",
        "RoutineArgument_UnlockAllDoors"
    };

    private static readonly HashSet<string> StorageTypes = new(StringComparer.Ordinal)
    {
        "RoutineArgument_AcaciaHunt", "RoutineArgument_AddThingThatHappened",
        "RoutineArgument_Construct",
        "RoutineArgument_EndOverrideIf_AfterTurns", "RoutineArgument_EndOverrideIf_Overridden",
        "RoutineArgument_FaceDirection", "RoutineArgument_Following", "RoutineArgument_GuardResponse",
        "RoutineArgument_PassOnRumor", "RoutineArgument_Patrol",
        "RoutineArgument_PlayLoop", "RoutineArgument_RandomPath", "RoutineArgument_RandomWalk",
        "RoutineArgument_SearchAndDestroy", "RoutineArgument_SeekPlayerAndTalk", "RoutineArgument_Sleep",
        "RoutineArgument_SleepWith", "RoutineArgument_TakeGatheringSpot",
        "RoutineArgument_TurnOnLights", "RoutineArgument_Visiting"
    };

    private RoutineArgumentSet(List<RoutineArgumentRecord> items, bool canEdit, string? error)
    {
        Items = items;
        CanEdit = canEdit;
        Error = error;
    }

    public List<RoutineArgumentRecord> Items { get; }
    public bool CanEdit { get; }
    public string? Error { get; }
    public static IReadOnlyList<string> AddableTypes { get; } = new[]
    {
        "RoutineArgument_BeVendor", "RoutineArgument_FaceDirection", "RoutineArgument_FacePoI",
        "RoutineArgument_LockAllDoors", "RoutineArgument_LockOwnedDoors", "RoutineArgument_Patrol",
        "RoutineArgument_RandomPath", "RoutineArgument_RandomWalk", "RoutineArgument_Sleep",
        "RoutineArgument_TurnOnLights", "RoutineArgument_UnlockAllDoors"
    };

    public static string? GetEditorGroup(string typeName) => typeName switch
    {
        "RoutineArgument_Sleep" or "RoutineArgument_TurnOnLights" => "Exclusive",
        "RoutineArgument_Patrol" or "RoutineArgument_RandomPath" or "RoutineArgument_RandomWalk" => "Movement",
        "RoutineArgument_LockAllDoors" or "RoutineArgument_LockOwnedDoors" or "RoutineArgument_UnlockAllDoors" => "Doors",
        "RoutineArgument_FaceDirection" or "RoutineArgument_FacePoI" => "Direction",
        _ => null
    };

    public static RoutineArgumentSet Parse(byte[]? bytes)
    {
        if (bytes is null)
            return new RoutineArgumentSet([], false, "This routine has no routineArguments field.");
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            int count = BinaryData.ReadSafeCount(reader, 10_000);
            var result = new List<RoutineArgumentRecord>(count);
            for (int index = 0; index < count; index++)
            {
                string typeName = reader.ReadString();
                string unqualified = typeName.Split('.').Last();
                if (EmptyTypes.Contains(unqualified))
                    result.Add(new RoutineArgumentRecord(typeName, RoutineArgumentEncoding.Empty, null));
                else if (unqualified == "RoutineArgument_Meeting")
                {
                    var fields = new VssContainer();
                    fields.Add("meetingMapZoneName", VssValueKind.String, reader.ReadString());
                    fields.Add("confrontationAdded", VssValueKind.Bool, reader.ReadBoolean());
                    fields.Add("giveUpTimer", VssValueKind.Int, reader.ReadInt32());
                    result.Add(new RoutineArgumentRecord(typeName, RoutineArgumentEncoding.MeetingRaw, fields));
                }
                else if (unqualified == "RoutineArgument_SetNewTimedRoutineIfVendorNotActive")
                {
                    var fields = new VssContainer();
                    fields.Add("npcName", VssValueKind.Int, reader.ReadInt32());
                    result.Add(new RoutineArgumentRecord(typeName, RoutineArgumentEncoding.IntRaw, fields));
                }
                else if (StorageTypes.Contains(unqualified))
                    result.Add(new RoutineArgumentRecord(typeName, RoutineArgumentEncoding.VersionSafeStorage, VssContainer.Read(reader)));
                else
                    throw new InvalidDataException($"Unknown polymorphic routine argument type '{typeName}'.");
            }
            BinaryData.RequireEnd(stream);
            return new RoutineArgumentSet(result, true, null);
        }
        catch (Exception exception)
        {
            return new RoutineArgumentSet([], false,
                $"Arguments are being preserved as an opaque blob because they could not be safely parsed: {exception.Message}");
        }
    }

    public byte[] ToBytes()
    {
        if (!CanEdit)
            throw new InvalidOperationException(Error);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Items.Count);
        foreach (RoutineArgumentRecord item in Items)
        {
            writer.Write(item.TypeName);
            switch (item.Encoding)
            {
                case RoutineArgumentEncoding.Empty:
                    break;
                case RoutineArgumentEncoding.VersionSafeStorage:
                    item.Storage!.Write(writer);
                    break;
                case RoutineArgumentEncoding.MeetingRaw:
                    writer.Write(item.Storage!.GetString("meetingMapZoneName"));
                    writer.Write(item.Storage.GetBool("confrontationAdded"));
                    writer.Write(item.Storage.GetInt("giveUpTimer"));
                    break;
                case RoutineArgumentEncoding.IntRaw:
                    writer.Write(item.Storage!.GetInt("npcName"));
                    break;
            }
        }
        return stream.ToArray();
    }

    public static byte[] EmptyBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0);
        return stream.ToArray();
    }

    public RoutineArgumentRecord Add(string typeName)
    {
        if (!CanEdit)
            throw new InvalidOperationException(Error);
        bool usesStorage = StorageTypes.Contains(typeName);
        if (!usesStorage && !EmptyTypes.Contains(typeName))
            throw new InvalidOperationException($"Unknown routine argument type '{typeName}'.");
        var item = new RoutineArgumentRecord(
            typeName,
            usesStorage ? RoutineArgumentEncoding.VersionSafeStorage : RoutineArgumentEncoding.Empty,
            usesStorage ? CreateDefaultStorage(typeName) : null);
        Items.Add(item);
        return item;
    }

    private static VssContainer CreateDefaultStorage(string typeName)
    {
        var storage = new VssContainer();
        switch (typeName)
        {
            case "RoutineArgument_FaceDirection":
                storage.Add("direction", VssValueKind.Int, 0);
                break;
            case "RoutineArgument_Patrol":
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(1);
                    writer.Write(0);
                    writer.Write(0);
                    storage.Add("waypoints", VssValueKind.Blob, stream.ToArray());
                }
                storage.Add("shuffle", VssValueKind.Bool, false);
                storage.Add("outsideOnly", VssValueKind.Bool, true);
                storage.Add("minWaitTurns", VssValueKind.Int, 15);
                storage.Add("maxWaitTurns", VssValueKind.Int, 25);
                storage.Add("currentIndex", VssValueKind.Int, 0);
                storage.Add("turnsLeftToWait", VssValueKind.Int, -1);
                break;
            case "RoutineArgument_RandomPath":
                storage.Add("randomPathTimer", VssValueKind.Int, -1);
                storage.Add("randomPathInterval", VssValueKind.Int, 8);
                storage.Add("zoneName", VssValueKind.String, "");
                storage.Add("initialTargetPositionHadSubmerge", VssValueKind.Bool, false);
                break;
            case "RoutineArgument_RandomWalk":
                storage.Add("interval", VssValueKind.Int, 0);
                break;
            case "RoutineArgument_Sleep":
                storage.Add("zTimer", VssValueKind.Int, -1);
                break;
            case "RoutineArgument_TurnOnLights":
                storage.Add("targetTurns", VssValueKind.Int, 120);
                storage.Add("workCounter", VssValueKind.Int, -1);
                storage.Add("targetMapZone", VssValueKind.String, "");
                storage.Add("toLightPosition", VssValueKind.Vector2Int, new Int2(0, 0));
                storage.Add("freeTurfPosition", VssValueKind.Vector2Int, new Int2(0, 0));
                storage.Add("currentUnlitPosition", VssValueKind.Vector2Int, new Int2(13337, 13337));
                storage.Add("darkOnly", VssValueKind.Bool, false);
                storage.Add("heatSourcesOnly", VssValueKind.Bool, false);
                storage.Add("done", VssValueKind.Bool, false);
                break;
        }
        return storage;
    }
}

public sealed class PlayerDefinitionModel
{
    private static readonly string[] FixedSpriteKeys = ["Dialog", "Small", "Up", "Down", "Left", "Right"];
    private readonly VssContainer _definition;
    private readonly VssContainer? _assetStorage;
    private readonly byte[] _assetTail = [];

    public PlayerDefinitionModel(VssContainer definition)
    {
        _definition = definition;
        try
        {
            AllowedSexes = definition.GetBlob("allowedSexes") is { } sexBytes
                ? BinaryData.ReadIntList(sexBytes)
                : [0];
            Racials = definition.GetBlob("racials") is { } racialBytes
                ? BinaryData.ReadStringList(racialBytes)
                : [];
            byte[] assetBytes = definition.GetBlob("assets") ?? throw new InvalidDataException("Missing assets.");
            using (var assetStream = new MemoryStream(assetBytes, writable: false))
            using (var assetReader = new BinaryReader(assetStream, Encoding.UTF8, leaveOpen: true))
            {
                _assetStorage = VssContainer.Read(assetReader);
                int tailLength = checked((int)(assetStream.Length - assetStream.Position));
                _assetTail = tailLength == 0 ? [] : assetReader.ReadBytes(tailLength);
            }
            ReadExpressions(_assetStorage.GetBlob("expressionSprites"));
            ReadFixedSprites(_assetStorage.GetBlob("sprites"));
            ReadSmallNudes(definition.GetBlob("smallNudeSprites"));
            ReadDialogNude(definition.GetBlob("dialogNudeSprite"));
            CanEdit = true;
        }
        catch (Exception exception)
        {
            Error = $"Player assets could not be parsed and will be preserved unchanged: {exception.Message}";
        }
    }

    public bool CanEdit { get; }
    public string? Error { get; }
    public List<int> AllowedSexes { get; } = [];
    public List<string> Racials { get; } = [];
    public List<PlayerImageAsset> Images { get; } = [];
    public float Scale { get => _assetStorage?.GetFloat("dialogSpriteScale", 1) ?? 1; set => _assetStorage?.SetFloat("dialogSpriteScale", value); }
    public float OffsetX { get => _assetStorage?.GetFloat("dialogSpriteOffsetX") ?? 0; set => _assetStorage?.SetFloat("dialogSpriteOffsetX", value); }
    public float OffsetY { get => _assetStorage?.GetFloat("dialogSpriteOffsetY") ?? 0; set => _assetStorage?.SetFloat("dialogSpriteOffsetY", value); }

    public void ValidateAllowedSexes(IReadOnlyCollection<int> sexes)
    {
        if (sexes.Count == 0)
            throw new InvalidDataException("Select at least one allowed sex before saving.");
        foreach (int sex in sexes)
        {
            if (!Images.Any(image => image.Category == PlayerImageCategory.SmallNude && image.NumericKey == sex))
                throw new InvalidDataException($"{SexName(sex)} needs a matching small nude sprite. Use 'Add small nude' on the Images tab before saving.");
        }
    }

    public void Apply()
    {
        if (!CanEdit || _assetStorage is null)
            return;
        ValidateAllowedSexes(AllowedSexes);
        if (_definition.Contains("allowedSexes") || !AllowedSexes.SequenceEqual([0]))
            _definition.SetBlob("allowedSexes", BinaryData.WriteIntList(AllowedSexes));
        if (_definition.Contains("racials") || Racials.Count > 0)
            _definition.SetBlob("racials", BinaryData.WriteStringList(Racials));

        using (var expressionStream = new MemoryStream())
        using (var writer = new BinaryWriter(expressionStream, Encoding.UTF8, leaveOpen: true))
        {
            PlayerImageAsset[] expressions = Images.Where(image => image.Category == PlayerImageCategory.Expression).ToArray();
            writer.Write(expressions.Length);
            foreach (PlayerImageAsset image in expressions)
            {
                writer.Write(image.Name);
                BinaryData.WriteCountedBytes(writer, image.Data);
            }
            if (_assetStorage.Contains("expressionSprites") || expressions.Length > 0)
                _assetStorage.SetBlob("expressionSprites", expressionStream.ToArray());
        }

        using (var fixedStream = new MemoryStream())
        using (var writer = new BinaryWriter(fixedStream, Encoding.UTF8, leaveOpen: true))
        {
            foreach (string key in FixedSpriteKeys)
            {
                PlayerImageAsset image = Images.Single(asset => asset.Category == PlayerImageCategory.Fixed && asset.Name == key);
                BinaryData.WriteCountedBytes(writer, image.Data);
            }
            _assetStorage.SetBlob("sprites", fixedStream.ToArray());
        }

        using (var nudeStream = new MemoryStream())
        using (var writer = new BinaryWriter(nudeStream, Encoding.UTF8, leaveOpen: true))
        {
            PlayerImageAsset[] smallNudes = Images.Where(image => image.Category == PlayerImageCategory.SmallNude).ToArray();
            writer.Write(smallNudes.Length);
            foreach (PlayerImageAsset image in smallNudes)
            {
                writer.Write(image.NumericKey);
                BinaryData.WriteCountedBytes(writer, image.Data);
            }
            if (_definition.Contains("smallNudeSprites") || smallNudes.Length > 0)
                _definition.SetBlob("smallNudeSprites", nudeStream.ToArray());
        }

        PlayerImageAsset? dialogNude = Images.FirstOrDefault(image => image.Category == PlayerImageCategory.DialogNude);
        using (var dialogStream = new MemoryStream())
        using (var writer = new BinaryWriter(dialogStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(dialogNude is not null);
            if (dialogNude is not null)
                BinaryData.WriteCountedBytes(writer, dialogNude.Data);
            if (_definition.Contains("dialogNudeSprite") || dialogNude is not null)
                _definition.SetBlob("dialogNudeSprite", dialogStream.ToArray());
        }

        byte[] assetStorageBytes = _assetStorage.ToBytes();
        if (_assetTail.Length == 0)
            _definition.SetBlob("assets", assetStorageBytes);
        else
        {
            var combined = new byte[assetStorageBytes.Length + _assetTail.Length];
            Buffer.BlockCopy(assetStorageBytes, 0, combined, 0, assetStorageBytes.Length);
            Buffer.BlockCopy(_assetTail, 0, combined, assetStorageBytes.Length, _assetTail.Length);
            _definition.SetBlob("assets", combined);
        }
    }

    private void ReadExpressions(byte[]? bytes)
    {
        if (bytes is null)
            return;
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        int count = BinaryData.ReadSafeCount(reader, 10_000);
        for (int index = 0; index < count; index++)
            Images.Add(new PlayerImageAsset(PlayerImageCategory.Expression, reader.ReadString(), index, BinaryData.ReadCountedBytes(reader)));
        BinaryData.RequireEnd(stream);
    }

    private void ReadFixedSprites(byte[]? bytes)
    {
        using var stream = new MemoryStream(bytes ?? throw new InvalidDataException("Missing sprites."), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        for (int index = 0; index < FixedSpriteKeys.Length; index++)
            Images.Add(new PlayerImageAsset(PlayerImageCategory.Fixed, FixedSpriteKeys[index], index, BinaryData.ReadCountedBytes(reader)));
        BinaryData.RequireEnd(stream);
    }

    private void ReadSmallNudes(byte[]? bytes)
    {
        if (bytes is null)
            return;
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        int count = BinaryData.ReadSafeCount(reader, 100);
        for (int index = 0; index < count; index++)
        {
            int sex = reader.ReadInt32();
            Images.Add(new PlayerImageAsset(PlayerImageCategory.SmallNude, $"Small nude ({SexName(sex)})", sex, BinaryData.ReadCountedBytes(reader)));
        }
        BinaryData.RequireEnd(stream);
    }

    private void ReadDialogNude(byte[]? bytes)
    {
        if (bytes is null)
            return;
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        bool exists = reader.ReadBoolean();
        if (exists)
            Images.Add(new PlayerImageAsset(PlayerImageCategory.DialogNude, "Dialog nude", 0, BinaryData.ReadCountedBytes(reader)));
        BinaryData.RequireEnd(stream);
    }

    internal static string SexName(int value) => value switch { 0 => "Male", 1 => "Female", 2 => "Intersex", _ => value.ToString() };
}

public enum PlayerImageCategory
{
    Fixed,
    Expression,
    SmallNude,
    DialogNude
}

public sealed class PlayerImageAsset(PlayerImageCategory category, string name, int numericKey, byte[] data)
{
    public PlayerImageCategory Category { get; } = category;
    public string Name { get; set; } = name;
    public int NumericKey { get; } = numericKey;
    public byte[] Data { get; set; } = data;
    public string DisplayName => Category == PlayerImageCategory.Expression ? $"Expression · {Name}" : Name;
    public override string ToString() => DisplayName;
}
