using System;
using System.Collections.Generic;
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
        private OwnerThreadReadOnlyList<PlayerState> playerStateView;
        private IMatchClock matchClock;
        private MatchStateMachine matchStateMachine;
        private bool isChangingMatchState;

        public GameplayMatchState MatchState
        {
            get
            {
                AssertRuntimeOwnerThread();
                return matchStateMachine?.State ?? initialMatchState;
            }
        }

        public OwnerThreadReadOnlyList<PlayerState> PlayerArray
        {
            get
            {
                AssertRuntimeOwnerThread();
                return playerStateView ??= new OwnerThreadReadOnlyList<PlayerState>(
                    AssertRuntimeOwnerThread,
                    playerStates);
            }
        }

        public double ElapsedTimeSeconds
        {
            get
            {
                AssertRuntimeOwnerThread();
                MatchTimestamp timestamp = GetMatchClock().CurrentTimestamp;
                return GetMatchStateMachine().GetElapsedSeconds(in timestamp);
            }
        }

        /// <summary>
        /// Selects the clock used by this GameState. Composition must configure the clock before
        /// the first transition, elapsed-time read, snapshot capture, or restore.
        /// </summary>
        internal void ConfigureMatchClock(IMatchClock value)
        {
            AssertRuntimeOwnerThread();
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (matchStateMachine != null)
            {
                if (ReferenceEquals(matchClock, value))
                {
                    return;
                }

                throw new InvalidOperationException(
                    "Match clock cannot be replaced after match runtime state has been created.");
            }

            MatchTimestamp timestamp = value.CurrentTimestamp;
            MatchStateMachine stateMachine = new MatchStateMachine(initialMatchState, in timestamp);
            matchClock = value;
            matchStateMachine = stateMachine;
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
            AssertRuntimeOwnerThread();
            if (isChangingMatchState)
            {
                error = "A match-state transition is already in progress.";
                return false;
            }

            MatchTimestamp timestamp = GetMatchClock().CurrentTimestamp;
            MatchStateMachine stateMachine = GetMatchStateMachine();
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
                    OutOfMemoryException outOfMemory = FindTerminalOutOfMemory(exception);
                    if (outOfMemory != null)
                    {
                        throw outOfMemory;
                    }

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
            AssertRuntimeOwnerThread();
            MatchTimestamp timestamp = GetMatchClock().CurrentTimestamp;
            return GetMatchStateMachine().CaptureSnapshot(in timestamp);
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
            AssertRuntimeOwnerThread();
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
                    OutOfMemoryException outOfMemory = FindTerminalOutOfMemory(exception);
                    if (outOfMemory != null)
                    {
                        throw outOfMemory;
                    }

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
            AssertRuntimeOwnerThread();
            if (ReferenceEquals(playerState, null) ||
                IndexOfPlayerStateReference(playerState) >= 0)
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
            AssertRuntimeOwnerThread();
            int index = IndexOfPlayerStateReference(playerState);
            if (index < 0)
            {
                return false;
            }

            playerStates.RemoveAt(index);
            return true;
        }

        public int GetNumPlayers()
        {
            AssertRuntimeOwnerThread();
            return playerStates.Count;
        }

        protected override void OnDestroy()
        {
            ResetRuntimeState();
            base.OnDestroy();
        }

        protected override void OnWorldUnbound(EndPlayReason reason)
        {
            ResetRuntimeState();
            base.OnWorldUnbound(reason);
        }

        private void ResetRuntimeState()
        {
            playerStates.Clear();
            playerStateView = null;
            matchStateMachine = null;
            matchClock = null;
            isChangingMatchState = false;
        }

        private int IndexOfPlayerStateReference(PlayerState playerState)
        {
            if (ReferenceEquals(playerState, null))
            {
                return -1;
            }

            for (int index = 0; index < playerStates.Count; index++)
            {
                if (ReferenceEquals(playerStates[index], playerState))
                {
                    return index;
                }
            }

            return -1;
        }

        private IMatchClock GetMatchClock()
        {
            return matchClock ?? throw new InvalidOperationException(
                "Match clock must be configured before accessing match runtime state.");
        }

        private MatchStateMachine GetMatchStateMachine()
        {
            return matchStateMachine ?? throw new InvalidOperationException(
                "Match runtime state must be initialized by configuring its clock before use.");
        }

        private void AssertRuntimeOwnerThread()
        {
            World currentWorld = World ?? throw new InvalidOperationException(
                "GameState runtime APIs require registration with a World.");
            currentWorld.AssertOwnerThread();
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
