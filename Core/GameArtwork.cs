using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SilverpineSilcEditor.Core;

internal sealed record PrefabArtwork(string File, float Left, float Bottom, float Width, float Height);

internal sealed class GameArtwork : IDisposable
{
    private readonly string _directory;
    private readonly Dictionary<string, PrefabArtwork> _prefabs;
    private readonly Dictionary<string, Bitmap> _images = [];
    private readonly Queue<string> _lru = [];
    private long _imageBytes;
    internal int Count => _prefabs.Count;
    internal string GameFolder { get; }
    internal static readonly string SettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Silverpine SILC Editor");

    private GameArtwork(string gameFolder, string directory, Dictionary<string, PrefabArtwork> prefabs)
        => (GameFolder, _directory, _prefabs) = (gameFolder, directory, prefabs);

    internal static bool IsGameFolder(string? directory) => directory is not null && File.Exists(Path.Combine(directory, "Silverpine.exe"))
        && Directory.Exists(Path.Combine(directory, "Silverpine_Data"));

    internal static string? FindGameFolder()
    {
        if (IsGameFolder(AppContext.BaseDirectory)) return AppContext.BaseDirectory;
        try
        {
            string path = Path.Combine(SettingsDirectory, "game-folder.txt");
            string? saved = File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            return IsGameFolder(saved) ? saved : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    internal static void RememberGameFolder(string directory)
    {
        if (!IsGameFolder(directory)) throw new InvalidDataException("Choose the folder containing Silverpine.exe and Silverpine_Data.");
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(Path.Combine(SettingsDirectory, "game-folder.txt"), Path.GetFullPath(directory));
    }

    internal static async Task<GameArtwork> LoadAsync(string gameFolder, SavedTileMap map, CancellationToken cancellation,
        string? cacheRoot = null, string? helperOverride = null)
    {
        if (!IsGameFolder(gameFolder)) throw new InvalidDataException("The selected folder must contain Silverpine.exe and Silverpine_Data.");
        // Invalidate after a game update or a new prefab request; no user paths baked into the EXE.
        string[] requested = map.Cells.Values.SelectMany(cell => cell.Objects).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var identity = new StringBuilder("map-art-v1\n" + Path.GetFullPath(gameFolder));
        foreach (string path in Directory.EnumerateFiles(Path.Combine(gameFolder, "Silverpine_Data")).Order(StringComparer.Ordinal))
        {
            var info = new FileInfo(path);
            identity.Append('\n').Append(info.Name).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks);
        }
        identity.Append('\n').AppendJoin('\n', requested);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())))[..24];
        string directory = Path.Combine(cacheRoot ?? Path.Combine(SettingsDirectory, "Map artwork cache"), key);
        string manifest = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifest))
        {
            Directory.CreateDirectory(directory);
            string request = Path.Combine(directory, "request.json");
            await File.WriteAllTextAsync(request, JsonSerializer.Serialize(requested), cancellation);
            string helper;
            if (helperOverride is not null) helper = helperOverride;
            else
            {
                using Stream resource = typeof(GameArtwork).Assembly.GetManifestResourceStream("SilverpineSilcEditor.AssetReader.exe")
                    ?? throw new InvalidDataException("The bundled Unity asset reader is missing. Use the complete published editor build.");
                string helperDirectory = Path.Combine(SettingsDirectory, "Asset reader", "0.2.0");
                Directory.CreateDirectory(helperDirectory);
                helper = Path.Combine(helperDirectory, "SilverpineAssetReader.exe");
                // Content-check the cached executable; never execute an arbitrary path from the game's files.
                byte[] expectedHash = SHA256.HashData(resource);
                bool matches;
                using (var existing = File.Exists(helper) ? File.OpenRead(helper) : null)
                    matches = existing is not null && SHA256.HashData(existing).AsSpan().SequenceEqual(expectedHash);
                if (!matches)
                {
                    resource.Position = 0;
                    string pending = helper + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await resource.CopyToAsync(file, cancellation);
                    File.Move(pending, helper, true);
                }
            }
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add(Path.GetFullPath(gameFolder));
            start.ArgumentList.Add(request);
            start.ArgumentList.Add(directory);
            using Process process = Process.Start(start) ?? throw new IOException("Could not start the asset reader.");
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellation);
            Task<string> errors = process.StandardError.ReadToEndAsync(cancellation);
            try { await process.WaitForExitAsync(cancellation); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true); // Only this editor-owned extraction worker.
                throw;
            }
            _ = await output;
            string error = await errors;
            if (process.ExitCode != 0 || !File.Exists(manifest))
                throw new InvalidDataException("Unity artwork extraction failed. " + error[..Math.Min(error.Length, 1200)]);
        }
        using JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(manifest, cancellation));
        if (json.RootElement.GetProperty("format").GetInt32() != 1) throw new InvalidDataException("Unsupported artwork cache version.");
        var prefabs = new Dictionary<string, PrefabArtwork>(StringComparer.Ordinal);
        foreach (JsonProperty property in json.RootElement.GetProperty("prefabs").EnumerateObject())
        {
            JsonElement entry = property.Value;
            string file = entry.GetProperty("file").GetString()!;
            if (file != Path.GetFileName(file) || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid artwork cache path.");
            prefabs[property.Name] = new PrefabArtwork(file, entry.GetProperty("left").GetSingle(), entry.GetProperty("bottom").GetSingle(),
                entry.GetProperty("width").GetSingle(), entry.GetProperty("height").GetSingle());
        }
        if (prefabs.Count == 0) throw new InvalidDataException("No usable prefab sprites were found in that game installation.");
        return new GameArtwork(gameFolder, directory, prefabs);
    }

    internal (Bitmap Image, PrefabArtwork Art)? Get(string prefab)
    {
        if (!_prefabs.TryGetValue(prefab, out PrefabArtwork? art)) return null;
        if (!_images.TryGetValue(art.File, out Bitmap? image))
        {
            using var source = Image.FromFile(Path.Combine(_directory, art.File));
            if (source.Width > 2048 || source.Height > 2048) return null;
            long size = (long)source.Width * source.Height * 4;
            while (_imageBytes + size > 32 * 1024 * 1024 && _lru.TryDequeue(out string? oldest))
            {
                Bitmap old = _images[oldest];
                _imageBytes -= (long)old.Width * old.Height * 4;
                old.Dispose();
                _images.Remove(oldest);
            }
            image = new Bitmap(source);
            _images.Add(art.File, image);
            _lru.Enqueue(art.File);
            _imageBytes += size;
        }
        return (image, art);
    }

    public void Dispose()
    {
        foreach (Bitmap image in _images.Values) image.Dispose();
        _images.Clear(); _lru.Clear(); _imageBytes = 0;
    }
}
