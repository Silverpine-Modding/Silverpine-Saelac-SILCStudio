using System.Globalization;
using System.Text.RegularExpressions;

namespace SilverpineSilcEditor.Core;

// Advisory UI limits from Silverpine 1.7.3, NOT serialization limits.
// Never normalize, truncate, clamp, or modify the document while inspecting it.
internal sealed record GameEditorWarning(string Field, string Detail)
{
    public override string ToString() => Field + ": " + Detail;
}

internal static class GameEditorGuidance
{
    internal const int Name = 25, Description = 400, PrivateDescription = 600,
        Personality = 150, KnowledgeName = 30, KnowledgeLore = 200,
        RoutineId = 25, Activity = 50, VisibleActivity = 25, Waypoints = 100,
        Expressions = 16, ImportantRelationships = 2;
    internal const string SaveAnyway = "I understand — save anyway";
    internal const string InGameTextLossWarning = "TEXT LOSS WARNING: Saving this character SILC through the unmodified in-game editor WILL cut off text that exceeds its character limits. To preserve over-limit text when saving in-game, you must have a mod installed and enabled that disables those limits.";

    internal static string Counter(int count, int? limit, string unit = "characters") => limit is int n
        ? $"{count:N0} / {n:N0} {unit} · base game{(count > n ? " — exceeded (allowed)" : "")}" : $"{count:N0} {unit}";

    internal static string WaypointText(IEnumerable<Int2> points) => "[" + string.Join(",", points.Select(p => $"[{p.X.ToString(CultureInfo.InvariantCulture)},{p.Y.ToString(CultureInfo.InvariantCulture)}]")) + "]";

    internal static IReadOnlyList<GameEditorWarning> Collect(SilcDocument document, IEnumerable<string> knownNpcs)
    {
        var result = new List<GameEditorWarning>();
        if (document.Definition is not { } d) return result;
        void Text(string label, string text, int limit, string? pattern = null, string? format = null)
        {
            if (text.Length > limit) result.Add(new(label, $"{text.Length:N0} characters; base-game editor limit is {limit:N0}. The complete text will be saved."));
            if (pattern is not null && Regex.IsMatch(text, pattern)) result.Add(new(label, "The base-game editor filters this field to " + format + ". Your text will be saved unchanged."));
        }
        void Number(string label, decimal value, decimal minimum, decimal maximum, bool whole = true)
        {
            if (value < minimum || value > maximum || (whole && value != decimal.Truncate(value)))
                result.Add(new(label, $"{value.ToString(CultureInfo.InvariantCulture)}; base-game editor uses {(whole ? "whole numbers " : "")}{minimum}–{maximum}."));
        }
        void Choice(string label, int value, params int[] allowed)
        {
            if (!allowed.Contains(value)) result.Add(new(label, $"Value {value} is not offered by the base-game editor."));
        }
        const string names = "[^a-zA-Z -]", npcNames = "[^a-zA-Z-]", prose = "[^a-zA-Z0-9.,!?%() _'\"-]", activity = "[^a-zA-Z% _'\"-]";
        if (document.Kind == SilcDefinitionKind.PlayerCharacter)
        {
            Text("Character name (filename)", document.Name, Name, names, "Latin letters, spaces, and hyphens");
            if (document.Name.Length < 3) result.Add(new("Character name", "The base-game editor requires at least 3 characters."));
            Text("Character description", d.GetString("description"), Description, prose, "Latin letters, numbers, spaces, and its supported punctuation");
            var model = new PlayerDefinitionModel(d);
            if (!model.CanEdit) return result;
            if (model.AllowedSexes.Count != 1) result.Add(new("Allowed sexes", $"{model.AllowedSexes.Count} selected; the base-game character editor selects one sex."));
            foreach (int sex in model.AllowedSexes) Choice("Allowed sex", sex, 0, 1, 2);
            string[] unknown = model.Racials.Where(r => !RacialCatalog.Traits.Any(t => t.Id == r)).Distinct().ToArray();
            if (unknown.Length > 0) result.Add(new("Racial traits", $"{unknown.Length} custom/unrecognized names are not offered in the base-game editor; their effects require game/mod support."));
            var expressions = model.Images.Where(i => i.Category == PlayerImageCategory.Expression).ToArray();
            if (expressions.Length > Expressions) result.Add(new("Expressions", $"{expressions.Length} expressions; the base-game editor allows adding up to {Expressions}."));
            foreach (var image in expressions) Text("Expression “" + image.Name + "”", image.Name, Name, names, "Latin letters, spaces, and hyphens");
            void Portrait(string label, float value, float min, float max)
            {
                if (!float.IsFinite(value) || value < min || value > max) result.Add(new(label, $"{value.ToString("R", CultureInfo.InvariantCulture)}; base-game adjustment range is {min}–{max}."));
            }
            Portrait("Portrait X offset", model.OffsetX, -100, 300); Portrait("Portrait Y offset", model.OffsetY, -300, 300); Portrait("Portrait scale", model.Scale, .9f, 1.6f);
            foreach (var image in model.Images.Where(i => i.Category == PlayerImageCategory.SmallNude || (i.Category == PlayerImageCategory.Fixed && i.Name == "Small")))
                if (image.Data.Length >= 24 && image.Data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(image.Data.AsSpan(20, 4)) is int height && height > 32)
                    result.Add(new(image.Name, $"{height} pixels high; the base-game editor downsamples this sprite to 32 pixels. This editor preserves your PNG."));
            return result;
        }
        if (document.Kind != SilcDefinitionKind.Npc) return result;
        Text("NPC name (filename)", document.Name, Name, npcNames, "Latin letters and hyphens, without spaces");
        if (document.Name.Length < 3) result.Add(new("NPC name", "The base-game editor requires at least 3 characters."));
        if (document.Name == "Acacia") result.Add(new("NPC name", "The unmodified base-game editor does not permit editing Acacia."));
        if (document.Name == "Alexia") result.Add(new("NPC name", "The game recreates Alexia's definition at startup and may replace these edits."));
        bool overriding = d.GetBool("overrideExisting");
        if (overriding != NpcDefinitionModel.BaseGameNpcNames.Contains(document.Name, StringComparer.Ordinal)) result.Add(new("Override existing NPC", "The base-game editor derives this mode from an exact match to a default NPC name."));
        Text("Character definition", d.GetString("customPlayerCharacterDefinitionName"), Name, names, "Latin letters, spaces, and hyphens");
        Text("Public description", d.GetString("publicCharacterDescription"), Description, prose, "Latin letters, numbers, spaces, and its supported punctuation");
        Text("Private description", d.GetString("privateCharacterDescription"), PrivateDescription, prose, "Latin letters, numbers, spaces, and its supported punctuation");
        Text("Personality traits", d.GetString("personalityTraits"), Personality, "[^a-zA-Z0-9,() '\"-]", "Latin letters, numbers, commas, parentheses, spaces, quotes, and hyphens");
        Text("Species", d.GetString("species"), Name, names, "Latin letters, spaces, and hyphens");
        Text("Occupation", d.GetString("occupation"), Name, names, "Latin letters, spaces, and hyphens");
        Choice("Walk-up chance", d.GetInt("walkUpChance", 2), 0, 1, 2, 3);
        if (!overriding)
        {
            Choice("Faction", d.GetInt("faction", 2), 1, 2, 3);
            Choice("Initial player relationship", d.GetInt("initialRelationshipLevel"), 0, 14, 49, 79, 100);
            Number("Known player for days", d.GetInt("knownPlayerForDays"), 0, 99999);
        }
        var npc = new NpcDefinitionModel(d);
        foreach (var k in npc.Knowledge.Items)
        {
            Text("Knowledge “" + k.Name + "” name", k.Name, KnowledgeName, "[^a-zA-Z '\"%-]", "Latin letters, spaces, quotes, percent signs, and hyphens");
            Text("Knowledge “" + k.Name + "” lore", k.Lore, KnowledgeLore, prose, "Latin letters, numbers, spaces, and its supported punctuation");
        }
        int summaryCount = npc.Knowledge.Items.Count(k => k.Name.Contains("%NAME%", StringComparison.Ordinal) || k.Lore.Contains("%NAME%", StringComparison.Ordinal));
        if (summaryCount > 16) result.Add(new("Condensed knowledge", $"{summaryCount} entries contain %NAME%; the game's summary builder uses only the first 16 qualifying entries. This is a runtime rule, not an editor cap."));
        var known = knownNpcs.Concat(NpcDefinitionModel.BaseGameNpcNames).ToHashSet(StringComparer.Ordinal);
        int important = npc.Relationships.Items.Count(r => r.Relationship == 3);
        if (!overriding && important > ImportantRelationships) result.Add(new("Important relationships", $"{important} configured; the base-game editor limit of {ImportantRelationships} is intentionally bypassed."));
        foreach (var r in npc.Relationships.Items)
        {
            Text("Relationship target “" + r.TargetName + "”", r.TargetName, Name, npcNames, "Latin letters and hyphens, without spaces");
            Text(r.TargetName + " — target role", r.TargetRole, Name, names, "Latin letters, spaces, and hyphens");
            Text(r.TargetName + " — self role", r.SelfRole, Name, names, "Latin letters, spaces, and hyphens");
            Choice(r.TargetName + " — relationship", r.Relationship, 0, 1, 2, 3);
            if (!known.Contains(r.TargetName)) result.Add(new("Relationship “" + r.TargetName + "”", "This NPC is not in the loaded definitions or base-game list. The existence check is intentionally bypassed; the game may ignore the relationship until the NPC exists."));
        }
        if (d.GetBool("isDynamicRoutine") && npc.Routines.Items.Count(r => r.Id.Length > 0) > 16) result.Add(new("Dynamic routines", "The game's generated routine selector receives only the first 16 named routines. Extra routines remain saved, but this runtime selection rule is not bypassed."));
        foreach (var r in npc.Routines.Items)
        {
            string label = "Routine “" + r.Id + "”";
            Text(label + " ID", r.Id, RoutineId, "[^a-zA-Z_-]", "Latin letters, underscores, and hyphens (no digits)");
            Text(label + " pathing activity", r.PathingActivity, Activity, activity, "Latin letters, spaces, %, _, quotes, and hyphens");
            Text(label + " activity", r.Activity, Activity, activity, "Latin letters, spaces, %, _, quotes, and hyphens");
            Text(label + " visible activity", r.VisibleActivity, VisibleActivity, activity, "Latin letters, spaces, %, _, quotes, and hyphens");
            foreach (var a in r.Arguments.Items)
            {
                if (a.Storage is not { } s) continue;
                if (a.ShortName == "FaceDirection") Choice(label + " direction", s.GetInt("direction"), 0, 1, 2, 3);
                if (a.ShortName == "TurnOnLights") Number(label + " lighting minutes", s.GetInt("targetTurns") / 2m, 0, 999);
                if (a.ShortName == "RandomWalk") Number(label + " random-walk interval", s.GetInt("interval"), 0, 9);
                if (a.ShortName == "RandomPath") Number(label + " random-path interval", s.GetInt("randomPathInterval"), 0, 99);
                if (a.ShortName == "Patrol")
                {
                    Number(label + " minimum wait", s.GetInt("minWaitTurns"), 0, 999); Number(label + " maximum wait", s.GetInt("maxWaitTurns"), 0, 999);
                    try { Text(label + " serialized waypoints", WaypointText(PatrolWaypoints.Read(s)), Waypoints); }
                    catch (Exception e) when (e is InvalidDataException or EndOfStreamException) { result.Add(new(label + " waypoints", "Unrecognized waypoint data will be preserved; base-game compatibility cannot be verified.")); }
                }
            }
        }
        return result;
    }

    internal static string SaveWarning(IReadOnlyList<GameEditorWarning> warnings, bool backup) =>
        "You are bypassing base-game editor limits or restrictions.\n\n" +
        InGameTextLossWarning + "\n\nSILC Studio saves the complete text. This warning concerns saving the SILC through the game's editor, not simply loading it in-game.\n\n" +
        string.Join("\n\n", warnings.Select(w => "• " + w)) +
        "\n\nThese values may work, be ignored, be altered by the in-game editor, or cause game/AI issues. Saving does not remove runtime selection or context limits.\n\n" +
        (backup ? "An exact backup of the previous file will be created before saving." : "Automatic backups are OFF. Keep your own backup before continuing.") +
        "\n\nChoose ‘I understand — save anyway’ only if you knowingly accept these compatibility risks. Cancel leaves the file on disk unchanged.";
}
