using System.Text.RegularExpressions;

namespace SilverpineSilcEditor.Core;

internal static partial class RoutinePresentation
{
    internal static string Name(string type) => type == "RoutineArgument_FacePoI" ? "Face Points of Interest"
        : Words(type.Replace("RoutineArgument_", "", StringComparison.Ordinal).Replace('_', ' '));
    internal static string Words(string name) => WordBoundary().Replace(name, "$1 $2");
    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex WordBoundary();

    // Wording and editable fields follow each argument's GetUITooltip/GetListUIItems.
    internal static string Description(string type) => type switch
    {
        "RoutineArgument_FaceDirection" => "The NPC will face the specified direction after arriving at the destination of the routine.",
        "RoutineArgument_FacePoI" => "The NPC will face points of interest, like tables, after arriving at the destination of the routine.",
        "RoutineArgument_TurnOnLights" => "The NPC will turn on light sources after arriving at the destination of the routine.",
        "RoutineArgument_Patrol" => "The NPC will patrol a list of waypoints after arriving at the destination of the routine.",
        "RoutineArgument_RandomPath" => "The NPC will start pathing randomly after arriving at the destination of the routine.",
        "RoutineArgument_LockAllDoors" => "All doors the NPC owns will become locked after arriving at the destination of the routine.",
        "RoutineArgument_LockOwnedDoors" => "Doors the NPC owns will become locked after pathing through them.",
        "RoutineArgument_BeVendor" => "The NPC will act as a vendor for the duration of the routine. Not implemented yet in the base game.",
        "RoutineArgument_RandomWalk" => "The NPC will start moving randomly after arriving at the destination of the routine.",
        "RoutineArgument_Sleep" => "The NPC will fall asleep after arriving at the destination of the routine.",
        "RoutineArgument_UnlockAllDoors" => "All doors the NPC owns will become unlocked after arriving at the destination of the routine.",
        _ => "Additional behavior for this routine. Unrecognized or runtime-only values remain available in advanced fields."
    };

    internal static string FieldName(string field) => field switch
    {
        "outsideOnly" => "Outdoors Only", "minWaitTurns" => "Min Wait Turns", "maxWaitTurns" => "Max Wait Turns",
        "darkOnly" => "Only if Dark", "heatSourcesOnly" => "Heat Sources Only", "targetTurns" => "Minutes",
        "randomPathInterval" => "Interval", "direction" => "Direction", "waypoints" => "Waypoints",
        _ => Words(char.ToUpperInvariant(field[0]) + field[1..])
    };
    internal static string FieldHelp(string field) => field switch
    {
        "shuffle" => "The list will be shuffled after having visited each waypoint.",
        "outsideOnly" => "The NPC will avoid checkpoints that aren't outdoors.",
        "minWaitTurns" => "The minimum number of turns to wait after reaching a waypoint.",
        "maxWaitTurns" => "The maximum number of turns to wait after reaching a waypoint.",
        "targetTurns" => "The number of in-game minutes the light sources will stay lit for. The game stores two turns per minute.",
        "darkOnly" => "The NPC will only turn on the light sources if the light level is low enough.",
        "heatSourcesOnly" => "The NPC will only turn on heat sources like stoves.",
        "randomPathInterval" => "The number of turns between random pathings.",
        "interval" => "The number of turns between random movements.",
        _ => ""
    };
    internal static string[]? EditableFields(string type) => type switch
    {
        "RoutineArgument_FaceDirection" => ["direction"],
        "RoutineArgument_TurnOnLights" => ["targetTurns", "darkOnly", "heatSourcesOnly"],
        "RoutineArgument_Patrol" => ["shuffle", "outsideOnly", "minWaitTurns", "maxWaitTurns"],
        "RoutineArgument_RandomPath" => ["randomPathInterval"],
        "RoutineArgument_RandomWalk" => ["interval"],
        "RoutineArgument_Sleep" => [],
        _ => null
    };
}
