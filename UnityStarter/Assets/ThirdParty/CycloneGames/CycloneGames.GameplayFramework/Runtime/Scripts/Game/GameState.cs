using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Committed world state visible to participants. GameMode owns policy; GameState exposes
    /// legal match transitions, elapsed time, and the participant state array.
    /// </summary>
    public class GameState : Actor
    {
        public enum EMatchState : byte
        {
            EnteringMap = 0,
            WaitingToStart = 1,
            InProgress = 2,
            WaitingPostMatch = 3,
            LeavingMap = 4,
            Aborted = 5,
        }

        [SerializeField] private EMatchState matchState = EMatchState.EnteringMap;

        private readonly List<PlayerState> playerStates = new List<PlayerState>(8);
        private ReadOnlyCollection<PlayerState> playerStateView;
        private double accumulatedMatchSeconds;
        private double activeSince;
        private bool matchClockRunning;
        private bool isChangingMatchState;

        public EMatchState MatchState => matchState;
        public IReadOnlyList<PlayerState> PlayerArray => playerStateView ??= playerStates.AsReadOnly();

        public float ElapsedTime
        {
            get
            {
                double seconds = accumulatedMatchSeconds;
                if (matchClockRunning)
                {
                    seconds += Time.timeAsDouble - activeSince;
                }

                return seconds >= float.MaxValue ? float.MaxValue : (float)Math.Max(0d, seconds);
            }
        }

        public virtual void SetMatchState(EMatchState newState)
        {
            if (!TrySetMatchState(newState, out string error))
            {
                throw new InvalidOperationException(error);
            }
        }

        public bool TrySetMatchState(EMatchState newState, out string error)
        {
            World?.AssertOwnerThread();
            if (matchState == newState)
            {
                error = null;
                return true;
            }

            if (isChangingMatchState)
            {
                error = "A match-state transition is already in progress.";
                return false;
            }

            if (!IsLegalTransition(matchState, newState))
            {
                error = $"Illegal match-state transition: {matchState} -> {newState}.";
                return false;
            }

            isChangingMatchState = true;
            EMatchState oldState = matchState;
            try
            {
                UpdateMatchClockBeforeTransition(oldState, newState);
                matchState = newState;
                try
                {
                    OnMatchStateChanged(oldState, newState);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
            finally
            {
                isChangingMatchState = false;
            }

            error = null;
            return true;
        }

        protected virtual void OnMatchStateChanged(EMatchState oldState, EMatchState newState) { }

        public virtual bool AddPlayerState(PlayerState playerState)
        {
            World?.AssertOwnerThread();
            if (playerState == null || playerStates.Contains(playerState))
            {
                return false;
            }

            if (World != null && !ReferenceEquals(playerState.World, World))
            {
                throw new InvalidOperationException("PlayerState must belong to the same World as GameState.");
            }

            playerStates.Add(playerState);
            return true;
        }

        public virtual bool RemovePlayerState(PlayerState playerState)
        {
            World?.AssertOwnerThread();
            return playerState != null && playerStates.Remove(playerState);
        }

        public int GetNumPlayers() => playerStates.Count;

        protected override void Awake()
        {
            base.Awake();
            if (matchState == EMatchState.InProgress)
            {
                matchClockRunning = true;
                activeSince = Time.timeAsDouble;
            }
        }

        protected override void OnDestroy()
        {
            playerStates.Clear();
            matchClockRunning = false;
            base.OnDestroy();
        }

        private void UpdateMatchClockBeforeTransition(EMatchState oldState, EMatchState newState)
        {
            if (oldState == EMatchState.InProgress && matchClockRunning)
            {
                accumulatedMatchSeconds += Math.Max(0d, Time.timeAsDouble - activeSince);
                matchClockRunning = false;
            }

            if (newState == EMatchState.WaitingToStart && oldState == EMatchState.WaitingPostMatch)
            {
                accumulatedMatchSeconds = 0d;
            }

            if (newState == EMatchState.InProgress)
            {
                activeSince = Time.timeAsDouble;
                matchClockRunning = true;
            }
        }

        private static bool IsLegalTransition(EMatchState current, EMatchState next)
        {
            switch (current)
            {
                case EMatchState.EnteringMap:
                    return next == EMatchState.WaitingToStart ||
                           next == EMatchState.LeavingMap ||
                           next == EMatchState.Aborted;
                case EMatchState.WaitingToStart:
                    return next == EMatchState.InProgress ||
                           next == EMatchState.LeavingMap ||
                           next == EMatchState.Aborted;
                case EMatchState.InProgress:
                    return next == EMatchState.WaitingPostMatch ||
                           next == EMatchState.LeavingMap ||
                           next == EMatchState.Aborted;
                case EMatchState.WaitingPostMatch:
                    return next == EMatchState.WaitingToStart ||
                           next == EMatchState.LeavingMap ||
                           next == EMatchState.Aborted;
                case EMatchState.LeavingMap:
                case EMatchState.Aborted:
                default:
                    return false;
            }
        }
    }
}
