using System.Runtime.CompilerServices;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    /// <summary>
    /// A scored interaction candidate. Used in the nearby interactables list to expose
    /// both the interactable and its computed score for UI sorting and display.
    /// </summary>
    public readonly struct InteractionCandidate
    {
        /// <summary>The interactable object.</summary>
        public readonly IInteractable Interactable;

        /// <summary>Computed score from the scoring algorithm (higher = better).</summary>
        public readonly float Score;

        /// <summary>Squared distance from the detection origin.</summary>
        public readonly float DistanceSqr;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InteractionCandidate(IInteractable interactable, float score, float distanceSqr)
        {
            Interactable = interactable;
            Score = score;
            DistanceSqr = distanceSqr;
        }

        public bool IsValid => Interactable != null;
    }
}
