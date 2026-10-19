using System.IO.Compression;
using System.Text;

namespace SilverpineSilcEditor.Core;

internal sealed class MapCell
{
    internal List<string> Objects { get; } = [];
    internal List<MapPlacement> Placements { get; } = [];
    internal string? Zone { get; set; }
    internal string Terrain => Objects.LastOrDefault(name => name.StartsWith("prefab_tile_", StringComparison.Ordinal)
        && !name.Contains("teleporter", StringComparison.Ordinal)) ?? "";
    internal string? Feature => Objects.LastOrDefault(name => name.StartsWith("prefab_wall_", StringComparison.Ordinal)
        || name.Contains("door", StringComparison.Ordinal) || name.StartsWith("prefab_furniture_", StringComparison.Ordinal)
        || name.StartsWith("prefab_container_", StringComparison.Ordinal) || name.StartsWith("prefab_tree_", StringComparison.Ordinal)
        || name.StartsWith("prefab_boulder_", StringComparison.Ordinal));
}

internal sealed record MapPlacement(string Prefab, float X, float Y);

internal sealed record MapRegion(string Name, Int2 Center)
{
    public override string ToString() => $"{Name} ({Center.X}, {Center.Y})";
}

internal sealed class SavedTileMap
{
    internal Dictionary<Int2, MapCell> Cells { get; } = [];
    internal List<MapRegion> Regions { get; } = [];
    internal List<string> Warnings { get; } = [];
    internal string SourcePath { get; private set; } = "";
    internal int ObjectCount { get; private set; }
    private long _expandedBytes;
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private CancellationToken _cancellation;

    // Read-only implementation of SerializationManager and DungeonGenerationManager,
    // never loads game assemblies or executes serializers from a save.
    internal static SavedTileMap Load(string path, CancellationToken cancellation = default)
    {
        var map = new SavedTileMap { SourcePath = Path.GetFullPath(path), _cancellation = cancellation };
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 128 * 1024 * 1024) throw new InvalidDataException("Save is larger than the map reader's 128 MiB safety limit.");
        using var reader = new BinaryReader(file);
        byte[] persistentObjects = Blob(reader);
        byte[] managers = Blob(reader);
        BinaryData.RequireEnd(file);
        map.ReadObjects(persistentObjects);
        using var memory = new MemoryStream(map.Expand(managers), false);
        using var persistent = new BinaryReader(memory);
        int count = Count(persistent, 10000);
        for (int i = 0; i < count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            string name = Text(persistent);
            int size = Length(persistent);
            long end = memory.Position + size;
            if (name == "DungeonGenerationManager")
            {
                byte[] data = persistent.ReadBytes(size);
                var storage = VssContainer.Parse(data);
                if (storage.GetBlob("worldTiles") is { } world) map.ReadWorld(world);
            }
            memory.Position = end;
        }
        BinaryData.RequireEnd(memory);
        if (map.Cells.Count == 0) throw new InvalidDataException("This save contains no readable tile or object positions.");
        if (map.Regions.Count == 0) map.Regions.Add(new MapRegion("Silverpine / origin", new Int2(0, 0)));
        map.Regions.Sort((a, b) => a == b ? 0 : a.Center == new Int2(0, 0) ? -1 : b.Center == new Int2(0, 0) ? 1 : string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        map._names.Clear();
        return map;
    }

    private byte[] Expand(byte[] bytes)
    {
        using var input = new MemoryStream(bytes, false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        byte[] buffer = new byte[65536];
        int read;
        while ((read = brotli.Read(buffer)) > 0)
        {
            _cancellation.ThrowIfCancellationRequested();
            _expandedBytes += read;
            if (output.Length + read > 128 * 1024 * 1024 || _expandedBytes > 384 * 1024 * 1024)
                throw new InvalidDataException("Save exceeds the map reader's decompression safety limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private void ReadObjects(byte[] compressed)
    {
        using var stream = new MemoryStream(Expand(compressed), false);
        using var reader = new BinaryReader(stream);
        int count = Count(reader, 2_000_000);
        if ((long)ObjectCount + count > 2_000_000) throw new InvalidDataException("Save exceeds two million map objects.");
        ObjectCount += count;
        for (int i = 0; i < count; i++)
        {
            _cancellation.ThrowIfCancellationRequested();
            string prefab = Text(reader);
            if (!_names.TryGetValue(prefab, out string? shared)) _names.Add(prefab, shared = prefab);
            float x = reader.ReadSingle(), y = reader.ReadSingle();
            _ = reader.ReadSingle(); // Rendering Z is not a separate routine coordinate.
            int length = Length(reader);
            long end = stream.Position + length;
            if (!float.IsFinite(x) || !float.IsFinite(y) || Math.Abs(x) > 10_000_000 || Math.Abs(y) > 10_000_000)
                throw new InvalidDataException("Invalid map-object coordinate.");
            // Unity Mathf.RoundToInt uses midpoint-to-even, matching integer turf registration.
            var point = new Int2((int)MathF.Round(x), (int)MathF.Round(y));
            if (!Cells.TryGetValue(point, out MapCell? cell)) Cells.Add(point, cell = new MapCell());
            if (!cell.Objects.Contains(shared)) cell.Objects.Add(shared);
            var placement = new MapPlacement(shared, x, y);
            if (!cell.Placements.Contains(placement)) cell.Placements.Add(placement);
            int components = Count(reader, 512);
            for (int component = 0; component < components; component++)
            {
                string type = Text(reader);
                if (!reader.ReadBoolean()) continue;
                int size = Length(reader);
                long componentEnd = stream.Position + size;
                if (componentEnd > end) throw new InvalidDataException("Object component extends beyond its saved record.");
                if (type == "MapZone") cell.Zone = Text(reader);
                // Skip all other serialized contents, including NPC memories and inventories.
                if (stream.Position > componentEnd) throw new InvalidDataException("Malformed map-zone component.");
                stream.Position = componentEnd;
            }
            if (stream.Position != end) throw new InvalidDataException("Object record has unexpected trailing data.");
        }
        BinaryData.RequireEnd(stream);
    }

    private void ReadWorld(byte[] data)
    {
        using var stream = new MemoryStream(data, false);
        using var reader = new BinaryReader(stream);
        int count = Count(reader, 10000);
        for (int i = 0; i < count; i++)
        {
            _cancellation.ThrowIfCancellationRequested();
            var key = new Int2(reader.ReadInt32(), reader.ReadInt32());
            if (Text(reader) != "WorldTile")
            {
                Warnings.Add("Some world chunks use an unsupported modded type; only the preceding chunks and persistent objects are shown.");
                return; // Unknown polymorphic formats have no reliable length to skip.
            }
            string title = Text(reader);
            _ = Text(reader); // sprite name
            string generator = Text(reader);
            string[] known = ["ForestTileGenerator", "DeepForestTileGenerator", "MountainTileGenerator", "HighMountainTileGenerator",
                "SwampTileGenerator", "DungeonTileGenerator", "FloodedCavernsTileGenerator", "FrozenFortressTileGenerator", "SwampTempleTileGenerator"];
            if (!known.Contains(generator, StringComparer.Ordinal))
            {
                Warnings.Add($"World generator '{generator}' is unsupported; remaining chunks were not guessed.");
                return;
            }
            _ = reader.ReadBoolean(); _ = Text(reader); // WorldTileGenerator spawnEnemies / centralPrefabName
            _ = reader.ReadBoolean(); _ = reader.ReadBoolean(); // river / road
            _ = reader.ReadInt32(); _ = reader.ReadInt32();
            _ = reader.ReadBoolean(); _ = Text(reader); // discovered / structure sprite
            if (reader.ReadBoolean())
            {
                ReadObjects(Blob(reader));
                Regions.Add(new MapRegion(title, new Int2(checked(key.X * 100), checked(key.Y * 100))));
            }
        }
        BinaryData.RequireEnd(stream);
    }

    private static int Count(BinaryReader reader, int maximum)
    {
        int value = reader.ReadInt32();
        if (value < 0 || value > maximum) throw new InvalidDataException("Invalid save record count.");
        return value;
    }
    private static int Length(BinaryReader reader)
    {
        int length = Count(reader, 128 * 1024 * 1024);
        if (length > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Truncated save record.");
        return length;
    }
    private static byte[] Blob(BinaryReader reader) => reader.ReadBytes(Length(reader));
    private static string Text(BinaryReader reader)
    {
        int length = reader.Read7BitEncodedInt();
        if (length < 0 || length > 1024 * 1024 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid save string length.");
        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }
}
