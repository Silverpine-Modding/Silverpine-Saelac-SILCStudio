using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SilverpineSilcEditor.Core;

internal sealed record MapArtwork(string Path, float Left, float Bottom, float Width, float Height,
    int Layer, int Order, bool YSort, int YOffset, float LocalY, bool Shadow)
{
    // YSorter.SetSortingOrder: Unity Mathf.RoundToInt uses midpoint-to-even.
    internal double SortOrder(float worldY) => YSort ? MathF.Round(-(worldY + LocalY) * 10f) + (double)YOffset : Order;
}

// Platform-independent extraction and metadata. Image decoding belongs to the desktop renderer.
internal sealed class GameAssetSource
{
    internal Dictionary<string, MapArtwork[]> Prefabs { get; } = new(StringComparer.Ordinal);
    internal List<MapPlacement> StaticPlacements { get; } = [];
    internal int MissingCount { get; private set; }
    internal static async Task<GameAssetSource> LoadAsync(string folder, SavedTileMap map, CancellationToken cancellation, string? cacheRoot = null)
    {
        string data = EditorPaths.GameData(folder) ?? throw new InvalidDataException("Choose the Silverpine game folder containing Silverpine_Data (or the Silverpine.app bundle).");
        string[] names = map.Cells.Values.SelectMany(c => c.Objects).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        // Each renderer now has its own image and global Unity sorting metadata.
        var identity = new StringBuilder("art-v5\n" + Path.GetFullPath(data));
        foreach (string path in Directory.EnumerateFiles(data, "*", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
        {
            var info = new FileInfo(path);
            identity.Append('\n').Append(info.Name).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks);
        }
        identity.AppendJoin('\n', names);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())))[..24];
        string directory = Path.Combine(cacheRoot ?? Path.Combine(EditorPaths.Settings, "Map artwork cache"), key);
        string manifest = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifest))
        {
            Directory.CreateDirectory(directory);
            string request = Path.Combine(directory, "request.json");
            await File.WriteAllTextAsync(request, JsonSerializer.Serialize(names), cancellation);
            string? native = OperatingSystem.IsWindows() ? await ExtractResource("windows", ".exe", cancellation, cacheRoot) : null;
            var start = new ProcessStartInfo(native ?? "python3") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true, RedirectStandardOutput = true };
            if (native is null) start.ArgumentList.Add(await ExtractResource("python", ".py", cancellation, cacheRoot));
            start.ArgumentList.Add(Path.GetFullPath(folder)); start.ArgumentList.Add(request); start.ArgumentList.Add(directory);
            Process process;
            try { process = Process.Start(start) ?? throw new IOException("Could not start the asset reader."); }
            catch (System.ComponentModel.Win32Exception e) { throw new IOException("Artwork needs Python 3 with UnityPy and Pillow on macOS/Linux. See the source README. The coordinate map still works without artwork.", e); }
            using (process)
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> errors = process.StandardError.ReadToEndAsync();
                try { await process.WaitForExitAsync(cancellation); }
                catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); await Task.WhenAll(output, errors); throw; }
                await output;
                string error = await errors;
                if (process.ExitCode != 0 || !File.Exists(manifest)) throw new InvalidDataException("Artwork extraction failed: " + error[..Math.Min(1500, error.Length)]);
            }
        }
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifest, cancellation));
        if (json.RootElement.GetProperty("format").GetInt32() != 2) throw new InvalidDataException("Unsupported artwork cache.");
        var result = new GameAssetSource();
        foreach (var item in json.RootElement.GetProperty("prefabs").EnumerateObject())
        {
            var parts = new List<MapArtwork>();
            if (item.Value.GetArrayLength() > 512) throw new InvalidDataException("Oversized artwork prefab.");
            foreach (JsonElement e in item.Value.EnumerateArray())
            {
            string file = e.GetProperty("file").GetString()!;
            if (file != Path.GetFileName(file) || file.Contains('\\') || file.Contains('/') || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid artwork cache path.");
            float left = e.GetProperty("left").GetSingle(), bottom = e.GetProperty("bottom").GetSingle(), width = e.GetProperty("width").GetSingle(), height = e.GetProperty("height").GetSingle();
            if (!float.IsFinite(left + bottom + width + height) || width <= 0 || height <= 0 || width > 64 || height > 64 || Math.Abs(left) > 64 || Math.Abs(bottom) > 64) continue;
            float localY = e.GetProperty("localY").GetSingle();
            if (!float.IsFinite(localY) || Math.Abs(localY) > 64) continue;
            parts.Add(new(Path.Combine(directory, file), left, bottom, width, height, e.GetProperty("layer").GetInt32(), e.GetProperty("order").GetInt32(),
                e.GetProperty("ySort").GetBoolean(), e.GetProperty("yOffset").GetInt32(), localY, e.GetProperty("shadow").GetBoolean()));
            }
            if (parts.Count > 0) result.Prefabs[item.Name] = parts.ToArray();
        }
        foreach (JsonElement e in json.RootElement.GetProperty("staticObjects").EnumerateArray())
        {
            if (result.StaticPlacements.Count >= 10000) throw new InvalidDataException("Too many fixed scene objects.");
            string prefab = e.GetProperty("prefab").GetString()!; float x = e.GetProperty("x").GetSingle(), y = e.GetProperty("y").GetSingle();
            if (prefab != "prefab_tile_water_bathhouse" || !float.IsFinite(x) || !float.IsFinite(y) || Math.Abs(x) > 10_000_000 || Math.Abs(y) > 10_000_000) throw new InvalidDataException("Invalid fixed scene tile.");
            result.StaticPlacements.Add(new(prefab, x, y));
        }
        result.MissingCount = names.Count(name => !result.Prefabs.ContainsKey(name));
        if (result.Prefabs.Count == 0) throw new InvalidDataException("No sprites were extracted. The coordinate map is still available; check the asset-reader dependencies or game folder.");
        return result;
    }
    private static async Task<string> ExtractResource(string kind, string extension, CancellationToken cancellation, string? cacheRoot)
    {
        using Stream source = typeof(GameAssetSource).Assembly.GetManifestResourceStream("AssetReader." + kind)
            ?? throw new IOException("Asset reader is missing. Build it using AssetReader/build_reader.py before publishing Windows.");
        string hash = Convert.ToHexString(SHA256.HashData(source)); source.Position = 0;
        string directory = Path.Combine(cacheRoot ?? EditorPaths.Settings, "Asset reader", hash[..16]);
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "SilverpineAssetReader" + extension);
        bool valid = false;
        if (File.Exists(file)) { using var existing = File.OpenRead(file); valid = Convert.ToHexString(SHA256.HashData(existing)) == hash; }
        if (!valid)
        {
            string temp = file + "." + Guid.NewGuid().ToString("N");
            try { using (var target = File.Create(temp)) await source.CopyToAsync(target, cancellation); File.Move(temp, file, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        return file;
    }
}
