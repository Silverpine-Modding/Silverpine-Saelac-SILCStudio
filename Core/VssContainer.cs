using System.Globalization;
using System.Text;

namespace SilverpineSilcEditor.Core;

public enum VssValueKind : byte
{
    Bool = 0,
    Float = 1,
    Int = 2,
    String = 3,
    Color = 4,
    Vector2Int = 5,
    Vector2 = 6,
    Blob = 7
}

public readonly record struct Rgba(float R, float G, float B, float A);
public readonly record struct Int2(int X, int Y);
public readonly record struct Float2(float X, float Y);

public sealed class VssEntry
{
    public required string Name { get; init; }
    public required VssValueKind Kind { get; init; }
    public required object Value { get; set; }

    public string DisplayValue => Kind switch
    {
        VssValueKind.Bool => ((bool)Value).ToString(),
        VssValueKind.Float => ((float)Value).ToString("R", CultureInfo.InvariantCulture),
        VssValueKind.Int => ((int)Value).ToString(CultureInfo.InvariantCulture),
        VssValueKind.String => (string)Value,
        VssValueKind.Color => Format((Rgba)Value),
        VssValueKind.Vector2Int => Format((Int2)Value),
        VssValueKind.Vector2 => Format((Float2)Value),
        VssValueKind.Blob => $"{((byte[])Value).Length:N0} bytes",
        _ => ""
    };

    private static string Format(Rgba value) => string.Join(", ",
        value.R.ToString("R", CultureInfo.InvariantCulture),
        value.G.ToString("R", CultureInfo.InvariantCulture),
        value.B.ToString("R", CultureInfo.InvariantCulture),
        value.A.ToString("R", CultureInfo.InvariantCulture));

    private static string Format(Int2 value) => $"{value.X}, {value.Y}";

    private static string Format(Float2 value) => string.Join(", ",
        value.X.ToString("R", CultureInfo.InvariantCulture),
        value.Y.ToString("R", CultureInfo.InvariantCulture));
}

public sealed class VssContainer
{
    private const int MaximumBlobLength = 512 * 1024 * 1024;
    private readonly List<VssEntry> _entries = [];

    public IReadOnlyList<VssEntry> Entries => _entries;

    public static VssContainer Parse(byte[] bytes, bool requireEnd = true)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        VssContainer result = Read(reader);
        if (requireEnd && stream.Position != stream.Length)
            throw new InvalidDataException($"VersionSafeStorage has {stream.Length - stream.Position:N0} trailing bytes.");
        return result;
    }

    public static VssContainer Read(BinaryReader reader)
    {
        var result = new VssContainer();
        int count = reader.ReadByte();
        for (int index = 0; index < count; index++)
        {
            string name = reader.ReadString();
            VssValueKind kind = (VssValueKind)reader.ReadByte();
            object value = kind switch
            {
                VssValueKind.Bool => reader.ReadBoolean(),
                VssValueKind.Float => reader.ReadSingle(),
                VssValueKind.Int => reader.ReadInt32(),
                VssValueKind.String => reader.ReadString(),
                VssValueKind.Color => new Rgba(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                VssValueKind.Vector2Int => new Int2(reader.ReadInt32(), reader.ReadInt32()),
                VssValueKind.Vector2 => new Float2(reader.ReadSingle(), reader.ReadSingle()),
                VssValueKind.Blob => BinaryData.ReadCountedBytes(reader, MaximumBlobLength),
                _ => throw new InvalidDataException($"Unknown VersionSafeStorage value type {(byte)kind} for '{name}'.")
            };
            result._entries.Add(new VssEntry { Name = name, Kind = kind, Value = value });
        }
        return result;
    }

    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        Write(writer);
        return stream.ToArray();
    }

    public void Write(BinaryWriter writer)
    {
        if (_entries.Count > byte.MaxValue)
            throw new InvalidDataException("A VersionSafeStorage container cannot contain more than 255 entries.");

        writer.Write((byte)_entries.Count);
        foreach (VssEntry entry in _entries)
        {
            writer.Write(entry.Name);
            writer.Write((byte)entry.Kind);
            switch (entry.Kind)
            {
                case VssValueKind.Bool:
                    writer.Write((bool)entry.Value);
                    break;
                case VssValueKind.Float:
                    writer.Write((float)entry.Value);
                    break;
                case VssValueKind.Int:
                    writer.Write((int)entry.Value);
                    break;
                case VssValueKind.String:
                    writer.Write((string)entry.Value);
                    break;
                case VssValueKind.Color:
                    Rgba color = (Rgba)entry.Value;
                    writer.Write(color.R);
                    writer.Write(color.G);
                    writer.Write(color.B);
                    writer.Write(color.A);
                    break;
                case VssValueKind.Vector2Int:
                    Int2 intVector = (Int2)entry.Value;
                    writer.Write(intVector.X);
                    writer.Write(intVector.Y);
                    break;
                case VssValueKind.Vector2:
                    Float2 vector = (Float2)entry.Value;
                    writer.Write(vector.X);
                    writer.Write(vector.Y);
                    break;
                case VssValueKind.Blob:
                    BinaryData.WriteCountedBytes(writer, (byte[])entry.Value);
                    break;
                default:
                    throw new InvalidDataException($"Cannot write unknown value type {(byte)entry.Kind}.");
            }
        }
    }

    public VssEntry? Find(string name) =>
        _entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal));

    public bool Contains(string name) => Find(name) is not null;

    public string GetString(string name, string fallback = "") =>
        Find(name) is { Kind: VssValueKind.String, Value: string value } ? value : fallback;

    public int GetInt(string name, int fallback = 0) =>
        Find(name) is { Kind: VssValueKind.Int, Value: int value } ? value : fallback;

    public float GetFloat(string name, float fallback = 0) =>
        Find(name) is { Kind: VssValueKind.Float, Value: float value } ? value : fallback;

    public bool GetBool(string name, bool fallback = false) =>
        Find(name) is { Kind: VssValueKind.Bool, Value: bool value } ? value : fallback;

    public Int2 GetInt2(string name, Int2 fallback = default) =>
        Find(name) is { Kind: VssValueKind.Vector2Int, Value: Int2 value } ? value : fallback;

    public byte[]? GetBlob(string name) =>
        Find(name) is { Kind: VssValueKind.Blob, Value: byte[] value } ? value : null;

    public void SetString(string name, string value) => Set(name, VssValueKind.String, value);
    public void SetInt(string name, int value) => Set(name, VssValueKind.Int, value);
    public void SetFloat(string name, float value) => Set(name, VssValueKind.Float, value);
    public void SetBool(string name, bool value) => Set(name, VssValueKind.Bool, value);
    public void SetInt2(string name, Int2 value) => Set(name, VssValueKind.Vector2Int, value);
    public void SetBlob(string name, byte[] value) => Set(name, VssValueKind.Blob, value);

    public void Add(string name, VssValueKind kind, object value)
    {
        if (Contains(name))
            throw new InvalidOperationException($"The field '{name}' already exists.");
        _entries.Add(new VssEntry { Name = name, Kind = kind, Value = value });
    }

    public bool TrySetDisplayValue(VssEntry entry, string text, out string? error)
    {
        error = null;
        try
        {
            entry.Value = entry.Kind switch
            {
                VssValueKind.Bool => bool.Parse(text),
                VssValueKind.Float => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
                VssValueKind.Int => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
                VssValueKind.String => text,
                VssValueKind.Color => ParseRgba(text),
                VssValueKind.Vector2Int => ParseInt2(text),
                VssValueKind.Vector2 => ParseFloat2(text),
                VssValueKind.Blob => throw new InvalidOperationException("Binary blobs are edited through their dedicated editor."),
                _ => throw new InvalidOperationException("Unknown field type.")
            };
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private void Set(string name, VssValueKind kind, object value)
    {
        VssEntry? existing = Find(name);
        if (existing is null)
        {
            Add(name, kind, value);
            return;
        }
        if (existing.Kind != kind)
            throw new InvalidDataException($"Field '{name}' is {existing.Kind}, not {kind}.");
        existing.Value = value;
    }

    private static string[] Parts(string text, int expected)
    {
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != expected)
            throw new FormatException($"Expected {expected} comma-separated values.");
        return parts;
    }

    private static Rgba ParseRgba(string text)
    {
        string[] parts = Parts(text, 4);
        return new Rgba(ParseFloat(parts[0]), ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3]));
    }

    private static Int2 ParseInt2(string text)
    {
        string[] parts = Parts(text, 2);
        return new Int2(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static Float2 ParseFloat2(string text)
    {
        string[] parts = Parts(text, 2);
        return new Float2(ParseFloat(parts[0]), ParseFloat(parts[1]));
    }

    private static float ParseFloat(string text) =>
        float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}

public static class BinaryData
{
    public static byte[] ReadCountedBytes(BinaryReader reader, int maximumLength = 512 * 1024 * 1024)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > maximumLength)
            throw new InvalidDataException($"Invalid binary field length: {length:N0} bytes.");
        long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
        if (length > remaining)
            throw new EndOfStreamException($"Binary field requests {length:N0} bytes but only {remaining:N0} remain.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException();
        return bytes;
    }

    public static void WriteCountedBytes(BinaryWriter writer, byte[] bytes)
    {
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    public static List<string> ReadStringList(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        int count = ReadSafeCount(reader);
        var result = new List<string>(count);
        for (int index = 0; index < count; index++)
            result.Add(reader.ReadString());
        RequireEnd(stream);
        return result;
    }

    public static byte[] WriteStringList(IEnumerable<string> values)
    {
        string[] items = values.ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(items.Length);
        foreach (string item in items)
            writer.Write(item);
        return stream.ToArray();
    }

    public static List<int> ReadIntList(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        int count = ReadSafeCount(reader);
        var result = new List<int>(count);
        for (int index = 0; index < count; index++)
            result.Add(reader.ReadInt32());
        RequireEnd(stream);
        return result;
    }

    public static byte[] WriteIntList(IEnumerable<int> values)
    {
        int[] items = values.ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(items.Length);
        foreach (int item in items)
            writer.Write(item);
        return stream.ToArray();
    }

    public static int ReadSafeCount(BinaryReader reader, int maximum = 1_000_000)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > maximum)
            throw new InvalidDataException($"Invalid collection count: {count:N0}.");
        return count;
    }

    public static void RequireEnd(Stream stream)
    {
        if (stream.Position != stream.Length)
            throw new InvalidDataException($"Unexpected {stream.Length - stream.Position:N0} trailing bytes.");
    }
}
