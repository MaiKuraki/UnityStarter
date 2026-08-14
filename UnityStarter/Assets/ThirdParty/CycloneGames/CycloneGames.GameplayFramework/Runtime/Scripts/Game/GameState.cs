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

        [SerializeField] private GameplayMatchState initialMatchState = GameplayMatchState.EnteringMap;

        private readonly List<PlayerState> playerStates = new List<PlayerState>(8);
        private ReadOnlyCollection<PlayerState> playerStateView;
        private IMatchClock matchClock;
        private MatchStateMachine matchStateMachine;
        private bool isChangingMatchState;

        public GameplayMatchState MatchState => matchStateMachine?.State ?? initialMatchState;
        public IReadOnlyList<PlayerState> PlayerArray => playerStateView ??= playerStates.AsReadOnly();

        public double ElapsedTimeSeconds
        {
            get
            {
                MatchTimestamp timestamp = GetMatchClock().CurrentTimestamp;
                return GetMatchStateMachine(in timestamp).GetElapsedSeconds(in timestamp);
            }
        }

        /// <summary>
        /// Selects the clock used by this GameState. Composition must configure the clock before
        /// the first transition, elapsed-time read, snapshot capture, or restore.
        /// </summary>
        public void ConfigureMatchClock(IMatchClock value)
        {
            World?.AssertOwnerThread();
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (matchStateMachine != null && !ReferenceEquals(matchClock, value))
            {
                throw new InvalidOperationException(
                    "Match clock cannot be replaced after match runtime state has been created.");
            }

            matchClock = value;
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
            if (isChangingMatchState)
            {
                error = "A match-state transition is already in progress.";
                return false;
            }

            MatchTimestamp timestamp = GetMatchClock().CurrentTimestamp;
            MatchStateMachine stateMachine = GetMatchStateMachine(in timestamp);
            GameplayMatchState oldState = stateMachine.State;
            MatchStateTransitionResult result = stateMachine.TryTransition(
                newState,
                in timestamp);
            if (result == MatchStateTransitionResult.Unchanged)
            {
                error = null;
                return true;
            }

            if (result != MatchStateTransitionResult.Success)
            {
                error = GetTransitionError(oldState, newState, result);
                return false;
            }

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

        public MatchStateSnapshot CaptureMatchStateSnapshot()
        {
            World?.AssertOwnerThread();
            MatchTimestamp timestamp = GetMatchClock().CurrentTimestamp;
            return GetMatchStateMachine(in timestamp).CaptureSnapshot(in timestamp);
        }

        public void RestoreMatchStateSnapshot(in MatchStateSnapshot snapshot)
        {
            if (!TryRestoreMatchStateSnapshot(in snapshot, out string error))
            {
                throw new InvalidOperationException(error);
            }
        }

        public bool TryRestoreMatchStateSnapshot(
            in MatchStateSnapshot snapshot,
            out string error)
        {
            World?.AssertOwnerThread();
            if (isChangingMatchState)
            {
                error = "A match-state transition is already in progress.";
                return false;
            }

            MatchTimestamp timestamp = GetMatchClock().CurrentTimestamp;
            MatchStateRestoreResult result = MatchStateMachine.TryRestore(
                in snapshot,
                in timestamp,
                out MatchStateMachine restoredStateMachine);
            if (result != MatchStateRestoreResult.Success)
            {
                error = GetRestoreError(result);
                return false;
            }

            GameplayMatchState oldState = MatchState;
            matchStateMachine = restoredStateMachine;
            if (oldState == restoredStateMachine.State)
            {
                error = null;
                return true;
            }

            isChangingMatchState = true;
            try
            {
                try
                {
                    OnMatchStateChanged(oldState, restoredStateMachine.State);
                }
                catch (Exception exception)
                {
                    Log.Error(
                        exception,
                        $"GameState '{name}' match-state observer failed while restoring '{restoredStateMachine.State}'.");
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

        protected override void OnDestroy()
        {
            playerStates.Clear();
            matchStateMachine = null;
            matchClock = null;
            base.OnDestroy();
        }

        private IMatchClock GetMatchClock()
        {
            return matchClock ?? UnityMatchClock.Scaled;
        }

        private MatchStateMachine GetMatchStateMachine(in MatchTimestamp timestamp)
        {
            return matchStateMachine ??=
                new MatchStateMachine(initialMatchState, in timestamp);
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
                case MatchStateTransitionResult.ClockEpochMismatch:
                    return "Match-state transition timestamp belongs to a different clock epoch.";
                default:
                    return $"Match-state transition failed: {oldState} -> {newState}.";
            }
        }

        private static string GetRestoreError(MatchStateRestoreResult result)
        {
            switch (result)
            {
                case MatchStateRestoreResult.InvalidSnapshot:
                    return "Match-state snapshot is invalid.";
                case MatchStateRestoreResult.InvalidTimestamp:
                    return "Current match-clock timestamp is invalid.";
                case MatchStateRestoreResult.ClockEpochMismatch:
                    return "Match-state snapshot belongs to a different clock epoch.";
                case MatchStateRestoreResult.RestoreTimestampPrecedesSnapshot:
                    return "Current match-clock timestamp precedes the snapshot timestamp.";
                default:
                    return "Match-state snapshot restore failed.";
            }
        }
    }
}
