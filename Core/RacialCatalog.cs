namespace SilverpineSilcEditor.Core;

internal sealed record RacialTrait(string Id, string Description);

internal static class RacialCatalog
{
    // Silverpine 1.7.3 Racial_*.cs; IDs follow MainMenuUI.GetRacialIDFromStatusEffectType.
    internal static readonly RacialTrait[] Traits =
    [
        new("Bioluminescence", "Your body emits light."),
        new("Cold Weakness", "Your body loses heat faster in cold weather."),
        new("Fur", "Insulation and +1 frost armor, but fur can get wet."),
        new("Hollow Bones", "Maximum HP is permanently reduced by 15."),
        new("Imposing", "NPCs pay extra attention to your character description."),
        new("Large Claws", "Unarmed attacks deal more damage and cost more energy."),
        new("Night Vision", "Your eyes are adapted to the dark."),
        new("Slow", "A 5% chance to skip a turn when moving."),
        new("Thick Skin", "+1 normal armor."),
        new("Unlucky", "Lady luck is not on your side."),
        new("Wings", "Large wings allow short bursts of flight.")
    ];
}
