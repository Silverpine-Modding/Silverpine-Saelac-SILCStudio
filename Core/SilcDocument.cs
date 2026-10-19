using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace SilverpineSilcEditor.Core;

public enum SilcDefinitionKind
{
    Npc,
    PlayerCharacter,
    ModelDefinition,
    Unknown
}

public sealed partial class SilcDocument
{
    private byte[] _rawDefinition = [];
    private byte[] _definitionTail = [];
    private byte[] _envelopeTail = [];

    private SilcDocument()
    {
    }

    public required string FilePath { get; init; }
    public required VssContainer Envelope { get; init; }
    public VssContainer? Definition { get; private set; }
    public string DefinitionType { get; private set; } = "Unknown";
    public string Version => Envelope.GetString("version", "Unknown");
    public bool IsLegacy { get; private set; }
    public bool UsedCompressionFallback { get; private set; }
    public string? DefinitionParseError { get; private set; }

    public string Name
    {
        get
        {
            string stem = Path.GetFileNameWithoutExtension(FilePath);
            int separator = stem.IndexOf('_');
            return separator >= 0 && separator + 1 < stem.Length ? stem[(separator + 1)..] : stem;
        }
    }

    public SilcDefinitionKind Kind => DefinitionType switch
    {
        "CustomContentDefinition_NPC" => SilcDefinitionKind.Npc,
        "CustomContentDefinition_PlayerCharacter" => SilcDefinitionKind.PlayerCharacter,
        "CustomContentDefinition_ModelDefinition" => SilcDefinitionKind.ModelDefinition,
        _ => SilcDefinitionKind.Unknown
    };

    public static SilcDocument Load(string path) => FromBytes(path, File.ReadAllBytes(path));

    public static SilcDocument FromBytes(string path, byte[] bytes)
    {
        VssContainer envelope;
        byte[] envelopeTail;
        using (var envelopeStream = new MemoryStream(bytes, writable: false))
        using (var envelopeReader = new BinaryReader(envelopeStream, Encoding.UTF8, leaveOpen: true))
        {
            envelope = VssContainer.Read(envelopeReader);
            int tailLength = checked((int)(envelopeStream.Length - envelopeStream.Position));
            envelopeTail = tailLength == 0 ? [] : envelopeReader.ReadBytes(tailLength);
        }
        byte[] definitionBlob = envelope.GetBlob("definition")
            ?? throw new InvalidDataException("The SILC envelope does not contain a definition blob.");

        string version = envelope.GetString("version", "");
        bool legacy = IsVersionOlderThan(version, 1, 7, 0);
        bool fallback = false;
        byte[] rawDefinition;
        try
        {
            rawDefinition = legacy ? definitionBlob : Decompress(definitionBlob);
        }
        catch (InvalidDataException)
        {
            rawDefinition = legacy ? Decompress(definitionBlob) : definitionBlob;
            legacy = !legacy;
            fallback = true;
        }

        var document = new SilcDocument
        {
            FilePath = Path.GetFullPath(path),
            Envelope = envelope,
            IsLegacy = legacy,
            UsedCompressionFallback = fallback,
            _rawDefinition = rawDefinition,
            _envelopeTail = envelopeTail
        };
        document.ParseDefinition();
        return document;
    }

    public byte[] BuildBytes()
    {
        byte[] rawDefinition = Definition is null ? _rawDefinition : BuildRawDefinition();
        byte[] storedDefinition = IsLegacy ? rawDefinition : Compress(rawDefinition, Kind == SilcDefinitionKind.Npc);
        Envelope.SetBlob("definition", storedDefinition);
        byte[] envelopeBytes = Envelope.ToBytes();
        if (_envelopeTail.Length == 0)
            return envelopeBytes;
        var combined = new byte[envelopeBytes.Length + _envelopeTail.Length];
        Buffer.BlockCopy(envelopeBytes, 0, combined, 0, envelopeBytes.Length);
        Buffer.BlockCopy(_envelopeTail, 0, combined, envelopeBytes.Length, _envelopeTail.Length);
        return combined;
    }

    public string? Save(bool createBackup)
    {
        string directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("The selected file has no parent directory.");
        string? backupPath = null;
        if (createBackup)
        {
            string backupDirectory = Path.Combine(directory, "SILC Editor Backups", "Per-file saves");
            Directory.CreateDirectory(backupDirectory);
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            backupPath = Path.Combine(backupDirectory, $"{Path.GetFileName(FilePath)}.{timestamp}.bak");
            File.Copy(FilePath, backupPath, overwrite: false);
        }

        byte[] result = BuildBytes();
        _ = FromBytes(FilePath, result);

        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, result);
            if (File.Exists(FilePath))
                File.Replace(temporaryPath, FilePath, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, FilePath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        return backupPath;
    }

    public void SetVersionForTesting(string version, bool legacy)
    {
        Envelope.SetString("version", version);
        IsLegacy = legacy;
    }

    private void ParseDefinition()
    {
        try
        {
            using var stream = new MemoryStream(_rawDefinition, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            DefinitionType = reader.ReadString();
            Definition = VssContainer.Read(reader);
            int tailLength = checked((int)(stream.Length - stream.Position));
            _definitionTail = tailLength == 0 ? [] : reader.ReadBytes(tailLength);
        }
        catch (Exception exception)
        {
            Definition = null;
            DefinitionParseError = exception.Message;
        }
    }

    private byte[] BuildRawDefinition()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(DefinitionType);
        Definition!.Write(writer);
        writer.Write(_definitionTail);
        return stream.ToArray();
    }

    private static byte[] Compress(byte[] bytes, bool optimal)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(
                   output,
                   optimal ? CompressionLevel.Optimal : CompressionLevel.NoCompression,
                   leaveOpen: true))
        {
            brotli.Write(bytes);
        }
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            brotli.CopyTo(output);
            return output.ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            throw new InvalidDataException("The SILC definition is not a valid Brotli stream.", exception);
        }
    }

    private static bool IsVersionOlderThan(string value, int major, int minor, int patch)
    {
        Match match = VersionPattern().Match(value);
        if (!match.Success)
            return false;
        var actual = new Version(
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value));
        return actual < new Version(major, minor, patch);
    }

    [GeneratedRegex(@"(\d+)\.(\d+)\.(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
