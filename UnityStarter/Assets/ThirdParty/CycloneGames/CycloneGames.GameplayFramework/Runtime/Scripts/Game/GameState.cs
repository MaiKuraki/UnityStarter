using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.Logging;
using UnityEngine;
using GameplayMatchState = CycloneGames.GameplayFramework.Core.MatchState;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Committed world state visible to participants. GameMode owns policy; this Unity facade
    /// delegates match transitions and elapsed-clock state to MatchStateMachine.
    /// </summary>
    public class GameState : Actor
    {
        private static readonly LogChannel Log = GameplayFrameworkLog.Channel;

        [SerializeField] private GameplayMatchState matchState = GameplayMatchState.EnteringMap;

        private readonly List<PlayerState> playerStates = new List<PlayerState>(8);
        private ReadOnlyCollection<PlayerState> playerStateView;
        private MatchStateMachine matchStateMachine;
        private bool isChangingMatchState;

        public GameplayMatchState MatchState => matchStateMachine?.State ?? matchState;
        public IReadOnlyList<PlayerState> PlayerArray => playerStateView ??= playerStates.AsReadOnly();

        public float ElapsedTime
        {
            get
            {
                double seconds = GetMatchStateMachine().GetElapsedSeconds(Time.timeAsDouble);
                return seconds >= float.MaxValue ? float.MaxValue : (float)Math.Max(0d, seconds);
            }
        }

        public void SetMatchState(GameplayMatchState newState)
        {
            if (!TrySetMatchState(newState, out string error))
            {
                throw new InvalidOperationException(error);
            }
        }

        public bool TrySetMatchState(GameplayMatchState newState, out string error)
        {
            World?.AssertOwnerThread();
            MatchStateMachine stateMachine = GetMatchStateMachine();
            if (stateMachine.State == newState)
            {
                error = null;
                return true;
            }

            if (isChangingMatchState)
            {
                error = "A match-state transition is already in progress.";
                return false;
            }

            GameplayMatchState oldState = stateMachine.State;
            MatchStateTransitionResult result = stateMachine.TryTransition(
                newState,
                Time.timeAsDouble);
            if (result != MatchStateTransitionResult.Success)
            {
                error = GetTransitionError(oldState, newState, result);
                return false;
            }

            matchState = newState;
            isChangingMatchState = true;
            try
            {
                try
                {
                    OnMatchStateChanged(oldState, newState);
                }
                catch (Exception exception)
                {
                    Log.Error(
                        exception,
                        $"GameState '{name}' match-state observer failed after transition from '{oldState}' to '{newState}'.");
                }
            }
            finally
            {
                isChangingMatchState = false;
            }

            error = null;
            return true;
        }

        protected virtual void OnMatchStateChanged(
            GameplayMatchState oldState,
            GameplayMatchState newState) { }

        public bool AddPlayerState(PlayerState playerState)
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

        public bool RemovePlayerState(PlayerState playerState)
        {
            World?.AssertOwnerThread();
            return playerState != null && playerStates.Remove(playerState);
        }

        public int GetNumPlayers() => playerStates.Count;

        protected override void Awake()
        {
            base.Awake();
            matchStateMachine = new MatchStateMachine(matchState, Time.timeAsDouble);
        }

        protected override void OnDestroy()
        {
            playerStates.Clear();
            matchStateMachine = null;
            base.OnDestroy();
        }

        private MatchStateMachine GetMatchStateMachine()
        {
            return matchStateMachine ??= new MatchStateMachine(matchState, Time.timeAsDouble);
        }

        private static string GetTransitionError(
            GameplayMatchState oldState,
            GameplayMatchState newState,
            MatchStateTransitionResult result)
        {
            switch (result)
            {
                case MatchStateTransitionResult.IllegalTransition:
                    return $"Illegal match-state transition: {oldState} -> {newState}.";
                case MatchStateTransitionResult.InvalidState:
                    return $"Match state '{newState}' is invalid.";
                case MatchStateTransitionResult.InvalidTimestamp:
                    return "Match-state transition timestamp is invalid or moved backwards.";
                default:
                    return $"Match-state transition failed: {oldState} -> {newState}.";
            }
        }
    }
}
