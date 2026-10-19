namespace SilverpineSilcEditor.Core;

internal static class EditorPaths
{
    internal static string Settings => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Silverpine SILC Editor");
    internal static string? Read(string name)
    {
        try { string file = Path.Combine(Settings, name + ".txt"); return File.Exists(file) ? File.ReadAllText(file).Trim() : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
    internal static void Write(string name, string value)
    {
        Directory.CreateDirectory(Settings);
        string file = Path.Combine(Settings, name + ".txt"), temp = file + "." + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temp, value); File.Move(temp, file, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static string? FindDefinitions()
    {
        string? saved = Read("definitions-folder");
        if (Directory.Exists(saved)) return saved;
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string root = OperatingSystem.IsWindows() ? Path.Combine(profile, "AppData", "LocalLow", "Three Eyes Software", "Silverpine")
            : OperatingSystem.IsMacOS() ? Path.Combine(profile, "Library", "Application Support", "Three Eyes Software", "Silverpine")
            : Path.Combine(profile, ".config", "unity3d", "Three Eyes Software", "Silverpine");
        string definitions = Path.Combine(root, "Definitions");
        return Directory.Exists(definitions) ? definitions : null;
    }
    internal static string? GameData(string? folder)
    {
        if (folder is null) return null;
        foreach (string relative in new[] { "Silverpine_Data", Path.Combine("Contents", "Resources", "Data"), Path.Combine("Silverpine.app", "Contents", "Resources", "Data") })
        {
            string data = Path.Combine(folder, relative);
            if (File.Exists(Path.Combine(data, "globalgamemanagers"))) return data;
        }
        return null;
    }
    internal static string? FindGame()
    {
        if (GameData(AppContext.BaseDirectory) is not null) return AppContext.BaseDirectory;
        // A macOS editor bundle may itself sit alongside the game bundle.
        string parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        if (OperatingSystem.IsMacOS() && GameData(parent) is not null) return parent;
        string? saved = Read("game-folder");
        return GameData(saved) is not null ? saved : null;
    }
}
