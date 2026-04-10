using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// A pawn used when spectating. Provides free-look camera movement by default.
    /// Override GetViewRotation/Update for custom spectator behavior.
    /// </summary>
    public class SpectatorPawn : Pawn
    {
        [SerializeField] private float spectatorSpeed = 10f;

        public float SpectatorSpeed { get => spectatorSpeed; set => spectatorSpeed = value; }
    }
}