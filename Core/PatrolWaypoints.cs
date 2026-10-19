using System.Text;

namespace SilverpineSilcEditor.Core;

internal static class PatrolWaypoints
{
    internal static List<Int2> Read(VssContainer storage)
    {
        byte[]? bytes = storage.GetBlob("waypoints");
        if (bytes is null) return [];
        using var stream = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(stream);
        int count = BinaryData.ReadSafeCount(reader, 10000);
        if (stream.Length != 4L + count * 8L) throw new InvalidDataException("Patrol waypoint data has an unsupported layout; it was left unchanged.");
        var points = new List<Int2>(count);
        for (int i = 0; i < count; i++) points.Add(new Int2(reader.ReadInt32(), reader.ReadInt32()));
        return points;
    }

    internal static void Write(VssContainer storage, IReadOnlyList<Int2> points)
    {
        if (points.Count is < 1 or > 10000) throw new InvalidDataException("A patrol needs between 1 and 10,000 waypoints.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(points.Count);
        foreach (Int2 point in points) { writer.Write(point.X); writer.Write(point.Y); }
        storage.SetBlob("waypoints", stream.ToArray());
        storage.SetInt("currentIndex", 0);
        storage.SetInt("turnsLeftToWait", -1);
    }
}
