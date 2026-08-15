using System;
using CycloneGames.GameplayFramework.Core;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>Unity login result that binds a framework status to a spawned controller.</summary>
    public readonly struct PlayerLoginResult
    {
        private PlayerLoginResult(
            PlayerLoginStatus status,
            PlayerController playerController,
            string error)
        {
            Status = status;
            PlayerController = playerController;
            Error = error;
        }

        public PlayerLoginStatus Status { get; }
        public PlayerController PlayerController { get; }
        public string Error { get; }
        public bool Succeeded => Status == PlayerLoginStatus.Success;

        public static PlayerLoginResult Success(PlayerController playerController)
        {
            if (playerController == null)
            {
                throw new ArgumentNullException(nameof(playerController));
            }

            return new PlayerLoginResult(PlayerLoginStatus.Success, playerController, null);
        }

        public static PlayerLoginResult Failure(PlayerLoginStatus status, string error)
        {
            if (status == PlayerLoginStatus.Success)
            {
                throw new ArgumentException("A failure result cannot use Success status.", nameof(status));
            }

            return new PlayerLoginResult(status, null, error);
        }
    }
}
