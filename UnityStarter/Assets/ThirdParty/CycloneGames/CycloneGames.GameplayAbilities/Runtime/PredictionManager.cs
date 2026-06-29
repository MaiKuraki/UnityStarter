using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Owns prediction windows, pending predicted effects, and closed prediction transaction records.
    /// </summary>
    public sealed class PredictionManager
    {
        public const int DefaultTransactionRecordCapacity = 64;

        private readonly List<GASPredictionWindowData> _windows;
        private readonly Dictionary<GASPredictionKey, int> _indexByKey;
        private readonly List<ActiveGameplayEffect> _pendingPredictedEffects;

        public PredictionManager(int windowCapacity = 16, int transactionRecordCapacity = DefaultTransactionRecordCapacity)
        {
            _windows = new List<GASPredictionWindowData>(windowCapacity);
            _indexByKey = new Dictionary<GASPredictionKey, int>(windowCapacity);
            _pendingPredictedEffects = new List<ActiveGameplayEffect>(windowCapacity);
            TransactionRecords = new GASPredictionTransactionRecord[Math.Max(0, transactionRecordCapacity)];
        }

        public GASPredictionKey CurrentPredictionKey;
        public GASPredictionTransactionRecord[] TransactionRecords;
        public int TransactionRecordCursor;
        public int TransactionRecordCount;
        public int LocalPredictionInputSequence;
        public long TotalWindowsOpened;
        public long TotalWindowsConfirmed;
        public long TotalWindowsRejected;
        public long TotalWindowsTimedOut;
        public long StalePredictionConfirmCount;
        public long StalePredictionRejectCount;

        public IReadOnlyList<GASPredictionWindowData> Windows => _windows;
        public IReadOnlyList<ActiveGameplayEffect> PendingPredictedEffects => _pendingPredictedEffects;
        public int WindowCount => _windows.Count;
        public int IndexCount => _indexByKey.Count;

        public void Reserve(int windowCapacity, int transactionRecordCapacity)
        {
            if (windowCapacity > _windows.Capacity)
            {
                _windows.Capacity = windowCapacity;
            }

            if (windowCapacity > _pendingPredictedEffects.Capacity)
            {
                _pendingPredictedEffects.Capacity = windowCapacity;
            }

            if (windowCapacity > 0)
            {
                _indexByKey.EnsureCapacity(windowCapacity);
            }

            if (transactionRecordCapacity > TransactionRecords.Length)
            {
                Array.Resize(ref TransactionRecords, transactionRecordCapacity);
            }
        }

        public GASPredictionKey CreatePredictionKey(GASEntityId owner)
        {
            int inputSequence = unchecked(++LocalPredictionInputSequence);
            if (inputSequence == 0)
            {
                inputSequence = unchecked(++LocalPredictionInputSequence);
            }

            return GASPredictionKey.NewKey(owner, inputSequence);
        }

        public bool RegisterWindow(GASPredictionWindowData window)
        {
            if (!window.PredictionKey.IsValid || FindWindowIndex(window.PredictionKey) >= 0)
            {
                return false;
            }

            _indexByKey[window.PredictionKey] = _windows.Count;
            _windows.Add(window);
            TotalWindowsOpened++;
            return true;
        }

        public bool HasOpenWindow(GASPredictionKey predictionKey)
        {
            return FindWindowIndex(predictionKey) >= 0;
        }

        public bool TryGetWindow(GASPredictionKey predictionKey, out GASPredictionWindowData window)
        {
            int index = FindWindowIndex(predictionKey);
            if (index >= 0)
            {
                window = _windows[index];
                return true;
            }

            window = default;
            return false;
        }

        public int FindWindowIndex(GASPredictionKey predictionKey)
        {
            if (!predictionKey.IsValid)
            {
                return -1;
            }

            if (_indexByKey.TryGetValue(predictionKey, out int index) &&
                index >= 0 &&
                index < _windows.Count &&
                _windows[index].PredictionKey.Equals(predictionKey))
            {
                return index;
            }

            return -1;
        }

        public bool TryRemoveWindow(GASPredictionKey predictionKey, out GASPredictionWindowData window)
        {
            int index = FindWindowIndex(predictionKey);
            if (index < 0)
            {
                window = default;
                return false;
            }

            window = _windows[index];
            int lastIndex = _windows.Count - 1;
            if (index != lastIndex)
            {
                var movedWindow = _windows[lastIndex];
                _windows[index] = movedWindow;
                _indexByKey[movedWindow.PredictionKey] = index;
            }

            _windows.RemoveAt(lastIndex);
            _indexByKey.Remove(predictionKey);
            return true;
        }

        public bool TryFindDependentWindow(GASPredictionKey parentPredictionKey, out GASPredictionKey childPredictionKey)
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                var window = _windows[i];
                if (window.ParentPredictionKey.Equals(parentPredictionKey))
                {
                    childPredictionKey = window.PredictionKey;
                    return true;
                }
            }

            childPredictionKey = default;
            return false;
        }

        public bool TryGetTimedOutWindow(int currentFrame, out GASPredictionWindowData window)
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                window = _windows[i];
                if (window.Status == GASPredictionWindowStatus.Open &&
                    window.TimeoutFrame > 0 &&
                    currentFrame >= window.TimeoutFrame)
                {
                    return true;
                }
            }

            window = default;
            return false;
        }

        public void IncrementPredictedEffectCount(GASPredictionKey predictionKey)
        {
            IncrementWindowCounter(predictionKey, WindowCounterKind.PredictedEffect, 1);
        }

        public void IncrementPredictedAttributeSnapshotCount(GASPredictionKey predictionKey)
        {
            IncrementWindowCounter(predictionKey, WindowCounterKind.PredictedAttributeSnapshot, 1);
        }

        public void IncrementPredictedGameplayCueCount(GASPredictionKey predictionKey, int count = 1)
        {
            IncrementWindowCounter(predictionKey, WindowCounterKind.PredictedGameplayCue, count);
        }

        public void IncrementPredictedAbilityTaskCount(GASPredictionKey predictionKey)
        {
            IncrementWindowCounter(predictionKey, WindowCounterKind.PredictedAbilityTask, 1);
        }

        public GASPredictionWindowStats GetStats()
        {
            int parentLinkedCount = 0;
            int expirableCount = 0;
            int earliestTimeoutFrame = 0;
            int predictedEffectCount = 0;
            int predictedAttributeSnapshotCount = 0;
            int predictedGameplayCueCount = 0;
            int predictedAbilityTaskCount = 0;

            for (int i = 0; i < _windows.Count; i++)
            {
                var window = _windows[i];
                predictedEffectCount += window.PredictedEffectCount;
                predictedAttributeSnapshotCount += window.PredictedAttributeSnapshotCount;
                predictedGameplayCueCount += window.PredictedGameplayCueCount;
                predictedAbilityTaskCount += window.PredictedAbilityTaskCount;
                if (window.ParentPredictionKey.IsValid)
                {
                    parentLinkedCount++;
                }

                if (window.TimeoutFrame > 0)
                {
                    expirableCount++;
                    if (earliestTimeoutFrame == 0 || window.TimeoutFrame < earliestTimeoutFrame)
                    {
                        earliestTimeoutFrame = window.TimeoutFrame;
                    }
                }
            }

            return new GASPredictionWindowStats(
                _windows.Count,
                parentLinkedCount,
                expirableCount,
                earliestTimeoutFrame,
                predictedEffectCount,
                predictedAttributeSnapshotCount,
                predictedGameplayCueCount,
                predictedAbilityTaskCount,
                TotalWindowsOpened,
                TotalWindowsConfirmed,
                TotalWindowsRejected,
                TotalWindowsTimedOut,
                StalePredictionConfirmCount,
                StalePredictionRejectCount,
                TransactionRecordCount,
                TransactionRecords?.Length ?? 0);
        }

        public bool TryGetClosedTransactionRecord(int recentIndex, out GASPredictionTransactionRecord record)
        {
            if (recentIndex < 0 ||
                recentIndex >= TransactionRecordCount ||
                TransactionRecords == null ||
                TransactionRecords.Length == 0)
            {
                record = default;
                return false;
            }

            int index = TransactionRecordCursor - 1 - recentIndex;
            if (index < 0)
            {
                index += TransactionRecords.Length;
            }

            record = TransactionRecords[index];
            return true;
        }

        public int CopyClosedTransactionRecordsNonAlloc(GASPredictionTransactionRecord[] destination, int destinationIndex = 0, int maxCount = int.MaxValue)
        {
            if (destination == null ||
                destinationIndex < 0 ||
                destinationIndex >= destination.Length ||
                maxCount <= 0 ||
                TransactionRecordCount == 0)
            {
                return 0;
            }

            int count = Math.Min(TransactionRecordCount, Math.Min(maxCount, destination.Length - destinationIndex));
            for (int i = 0; i < count; i++)
            {
                TryGetClosedTransactionRecord(i, out destination[destinationIndex + i]);
            }

            return count;
        }

        public void EnsureTransactionRecordCapacity(int capacity)
        {
            if (capacity < 0)
            {
                capacity = 0;
            }

            if (TransactionRecords != null && TransactionRecords.Length >= capacity)
            {
                return;
            }

            int recordsToCopy = Math.Min(TransactionRecordCount, capacity);
            var newRecords = new GASPredictionTransactionRecord[capacity];
            for (int i = recordsToCopy - 1; i >= 0; i--)
            {
                if (TryGetClosedTransactionRecord(i, out var record))
                {
                    int targetIndex = recordsToCopy - 1 - i;
                    newRecords[targetIndex] = record;
                }
            }

            TransactionRecords = newRecords;
            TransactionRecordCount = recordsToCopy;
            TransactionRecordCursor = recordsToCopy % Math.Max(capacity, 1);
        }

        public void RecordTransaction(
            GASPredictionWindowData window,
            GASPredictionWindowStatus status,
            GASPredictionRollbackFlags rollbackFlags,
            int closeFrame)
        {
            if (TransactionRecords == null || TransactionRecords.Length == 0)
            {
                return;
            }

            TransactionRecords[TransactionRecordCursor] = new GASPredictionTransactionRecord(window, status, rollbackFlags, closeFrame);
            TransactionRecordCursor++;
            if (TransactionRecordCursor >= TransactionRecords.Length)
            {
                TransactionRecordCursor = 0;
            }

            if (TransactionRecordCount < TransactionRecords.Length)
            {
                TransactionRecordCount++;
            }
        }

        public void RecordStaleTransaction(GASPredictionKey predictionKey, GASPredictionWindowStatus status, int closeFrame)
        {
            if (!predictionKey.IsValid)
            {
                return;
            }

            if (status == GASPredictionWindowStatus.Confirmed)
            {
                StalePredictionConfirmCount++;
            }
            else if (status == GASPredictionWindowStatus.Rejected)
            {
                StalePredictionRejectCount++;
            }

            var window = new GASPredictionWindowData(predictionKey, default, default, 0, 0, 0);
            RecordTransaction(window, status, GASPredictionRollbackFlags.StaleMessage, closeFrame);
        }

        public void IncrementClosedWindowCount(GASPredictionWindowStatus status)
        {
            switch (status)
            {
                case GASPredictionWindowStatus.Confirmed:
                    TotalWindowsConfirmed++;
                    break;
                case GASPredictionWindowStatus.Rejected:
                    TotalWindowsRejected++;
                    break;
                case GASPredictionWindowStatus.TimedOut:
                    TotalWindowsTimedOut++;
                    break;
            }
        }

        public void AddPendingPredictedEffect(ActiveGameplayEffect effect)
        {
            if (effect != null)
            {
                _pendingPredictedEffects.Add(effect);
            }
        }

        public int RemovePendingPredictedEffects(GASPredictionKey predictionKey)
        {
            if (!predictionKey.IsValid)
            {
                return 0;
            }

            int removedCount = 0;
            for (int i = _pendingPredictedEffects.Count - 1; i >= 0; i--)
            {
                var predicted = _pendingPredictedEffects[i];
                if (IsPendingPredictedEffectMatch(predicted, predictionKey))
                {
                    RemovePendingPredictedEffectAt(i);
                    removedCount++;
                }
            }

            return removedCount;
        }

        public bool TryTakePendingPredictedEffect(GASPredictionKey predictionKey, out ActiveGameplayEffect effect)
        {
            if (!predictionKey.IsValid)
            {
                effect = null;
                return false;
            }

            for (int i = _pendingPredictedEffects.Count - 1; i >= 0; i--)
            {
                effect = _pendingPredictedEffects[i];
                if (!IsPendingPredictedEffectMatch(effect, predictionKey))
                {
                    continue;
                }

                RemovePendingPredictedEffectAt(i);
                return true;
            }

            effect = null;
            return false;
        }

        public ActiveGameplayEffect FindPendingPredictedEffectForReconcile(
            GameplayEffect effectDef,
            AbilitySystemComponent source,
            GASPredictionKey predictionKey)
        {
            if (effectDef == null)
            {
                return null;
            }

            for (int i = _pendingPredictedEffects.Count - 1; i >= 0; i--)
            {
                var effect = _pendingPredictedEffects[i];
                if (effect == null ||
                    effect.IsExpired ||
                    effect.NetworkId != 0 ||
                    effect.Spec?.Def != effectDef)
                {
                    continue;
                }

                if (predictionKey.IsValid)
                {
                    if (effect.Spec.Context == null || !effect.Spec.Context.PredictionKey.Equals(predictionKey))
                    {
                        continue;
                    }
                }
                else if (source != null && effect.Spec.Source != source)
                {
                    continue;
                }

                return effect;
            }

            return null;
        }

        public bool ValidateIndexes()
        {
            if (_indexByKey.Count != _windows.Count)
            {
                return false;
            }

            for (int i = 0; i < _windows.Count; i++)
            {
                var window = _windows[i];
                if (!window.PredictionKey.IsValid ||
                    !_indexByKey.TryGetValue(window.PredictionKey, out int index) ||
                    index != i)
                {
                    return false;
                }
            }

            return true;
        }

        public void Reset()
        {
            _pendingPredictedEffects.Clear();
            _windows.Clear();
            _indexByKey.Clear();
            if (TransactionRecords != null)
            {
                Array.Clear(TransactionRecords, 0, TransactionRecords.Length);
            }

            TransactionRecordCursor = 0;
            TransactionRecordCount = 0;
            CurrentPredictionKey = default;
            LocalPredictionInputSequence = 0;
            TotalWindowsOpened = 0;
            TotalWindowsConfirmed = 0;
            TotalWindowsRejected = 0;
            TotalWindowsTimedOut = 0;
            StalePredictionConfirmCount = 0;
            StalePredictionRejectCount = 0;
        }

        private void IncrementWindowCounter(GASPredictionKey predictionKey, WindowCounterKind kind, int count)
        {
            int index = FindWindowIndex(predictionKey);
            if (index < 0 || count <= 0)
            {
                return;
            }

            var window = _windows[index];
            switch (kind)
            {
                case WindowCounterKind.PredictedEffect:
                    window.PredictedEffectCount += count;
                    break;
                case WindowCounterKind.PredictedAttributeSnapshot:
                    window.PredictedAttributeSnapshotCount += count;
                    break;
                case WindowCounterKind.PredictedGameplayCue:
                    window.PredictedGameplayCueCount += count;
                    break;
                case WindowCounterKind.PredictedAbilityTask:
                    window.PredictedAbilityTaskCount += count;
                    break;
            }

            _windows[index] = window;
        }

        private void RemovePendingPredictedEffectAt(int index)
        {
            int lastIndex = _pendingPredictedEffects.Count - 1;
            if (index != lastIndex)
            {
                _pendingPredictedEffects[index] = _pendingPredictedEffects[lastIndex];
            }

            _pendingPredictedEffects.RemoveAt(lastIndex);
        }

        private static bool IsPendingPredictedEffectMatch(ActiveGameplayEffect effect, GASPredictionKey predictionKey)
        {
            return effect?.Spec?.Context != null && effect.Spec.Context.PredictionKey.Equals(predictionKey);
        }

        private enum WindowCounterKind
        {
            PredictedEffect,
            PredictedAttributeSnapshot,
            PredictedGameplayCue,
            PredictedAbilityTask
        }
    }
}
