using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Replicated game state visible to all players.
    /// Tracks match state, elapsed time, and the authoritative list of PlayerStates.
    /// Subclass to add game-specific replicated state (scores, rounds, etc.)
    /// </summary>
    public class GameState : Actor
    {
        public enum EMatchState : byte
        {
            EnteringMap,
            WaitingToStart,
            InProgress,
            WaitingPostMatch,
            LeavingMap,
            Aborted
        }

        [SerializeField] private EMatchState matchState = EMatchState.EnteringMap;
        private readonly List<PlayerState> playerStates = new List<PlayerState>(8);
        private float elapsedTime;

        public EMatchState MatchState => matchState;
        public float ElapsedTime => elapsedTime;
        public IReadOnlyList<PlayerState> PlayerArray => playerStates;

        public virtual void SetMatchState(EMatchState NewState)
        {
            if (matchState == NewState) return;
            EMatchState oldState = matchState;
            matchState = NewState;
            OnMatchStateChanged(oldState, NewState);
        }

        protected virtual void OnMatchStateChanged(EMatchState OldState, EMatchState NewState) { }

        public virtual void AddPlayerState(PlayerState PS)
        {
            if (PS != null && !playerStates.Contains(PS))
            {
                playerStates.Add(PS);
            }
        }

        public virtual void RemovePlayerState(PlayerState PS)
        {
            playerStates.Remove(PS);
        }

        public int GetNumPlayers() => playerStates.Count;

        protected override void Update()
        {
            base.Update();
            if (matchState == EMatchState.InProgress)
            {
                elapsedTime += Time.deltaTime;
            }
        }
    }
}
