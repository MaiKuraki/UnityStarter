using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Factory asset for a per-modifier custom magnitude calculation.
    /// Derive this asset plus a <see cref="GameplayModMagnitudeCalculation"/>; the asset is what a
    /// modifier references, the calculation is created when the effect definition is built. Projects
    /// add their own types without touching the framework. Attribute reads must be declared in
    /// <see cref="GameplayModMagnitudeCalculation.GetAttributeCaptureDefinitions"/>.
    /// </summary>
    public abstract class GameplayModMagnitudeCalculationSO : ScriptableObject
    {
        public abstract GameplayModMagnitudeCalculation CreateCalculation();
    }
}
