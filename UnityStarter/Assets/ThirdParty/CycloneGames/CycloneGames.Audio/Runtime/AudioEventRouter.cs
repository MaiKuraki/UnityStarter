using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    public class AudioEventRouter : MonoBehaviour
    {
        public AudioTrigger[] triggers;
        private readonly List<AudioHandle> activeOnEnabledEvents = new List<AudioHandle>(4);
        private CancellationTokenSource loopCancellation;
        private const float MIN_LOOP_TIME = 1.0f;
        private const float MAX_LOOP_TIME = 1000.0f;

        public void OnEnable()
        {
            CancelLoopingTriggers();

            if (triggers == null)
            {
                return;
            }

            for (int i = 0; i < triggers.Length; i++)
            {
                AudioTrigger tempTrigger = triggers[i];
                if (tempTrigger == null)
                {
                    continue;
                }

                if (tempTrigger.triggerOnEvent == UnityTrigger.OnEnable)
                {
                    if (tempTrigger.loopTrigger)
                    {
                        StartLoopingTrigger(i);
                    }
                    else
                    {
                        TrackEnabledEvent(PlayTraceableTrigger(i));
                    }
                }
            }
        }

        public void OnDisable()
        {
            CancelLoopingTriggers();

            if (triggers != null)
            {
                for (int i = 0; i < triggers.Length; i++)
                {
                    AudioTrigger tempTrigger = triggers[i];
                    if (tempTrigger != null && tempTrigger.triggerOnEvent == UnityTrigger.OnDisable)
                    {
                        PlayAudioTrigger(i);
                    }
                }
            }

            StopEnabledEvents();
        }

        private void OnDestroy()
        {
            CancelLoopingTriggers();
            StopEnabledEvents();
        }

        public void PlayAudioEvent(AudioEvent eventToPlay)
        {
            AudioManager.PlayEvent(eventToPlay, gameObject);
        }

        public void PlayAudioTrigger(int triggerNum)
        {
            PlayTraceableTrigger(triggerNum);
        }

        public void StartLoopingTrigger(int triggerNum)
        {
            if (!isActiveAndEnabled || !TryGetTrigger(triggerNum, out AudioTrigger trigger))
            {
                return;
            }

            loopCancellation ??= new CancellationTokenSource();
            PlayLoopingTriggerAsync(triggerNum, trigger, loopCancellation.Token).Forget();
        }

        public void StopEvents(AudioEvent eventToStop)
        {
            AudioManager.StopAll(eventToStop);
        }

        public void StopEvents(int groupNumber)
        {
            AudioManager.StopAll(groupNumber);
        }

        public void StopEnabledEvents()
        {
            for (int i = 0; i < activeOnEnabledEvents.Count; i++)
            {
                activeOnEnabledEvents[i].Stop();
            }

            activeOnEnabledEvents.Clear();
        }

        private AudioHandle PlayTraceableTrigger(int triggerNum)
        {
            if (!TryGetTrigger(triggerNum, out AudioTrigger tempTrigger) || tempTrigger.eventToTrigger == null)
            {
                return default;
            }

            ActiveEvent activeEvent;
            if (!tempTrigger.usePosition && tempTrigger.soundEmitter == null)
            {
                activeEvent = AudioManager.PlayEvent(tempTrigger.eventToTrigger, gameObject);
            }
            else if (tempTrigger.usePosition)
            {
                activeEvent = AudioManager.PlayEvent(tempTrigger.eventToTrigger, tempTrigger.soundPosition);
            }
            else
            {
                activeEvent = AudioManager.PlayEvent(tempTrigger.eventToTrigger, tempTrigger.soundEmitter);
            }

            return activeEvent != null ? activeEvent.Handle : default;
        }

        private async UniTask PlayLoopingTriggerAsync(
            int triggerNum,
            AudioTrigger loopTrigger,
            CancellationToken cancellationToken)
        {
            try
            {
                float minimumDelay = Mathf.Clamp(loopTrigger.loopTimeMin, MIN_LOOP_TIME, MAX_LOOP_TIME);
                float maximumDelay = Mathf.Clamp(loopTrigger.loopTimeMax, minimumDelay, MAX_LOOP_TIME);

                while (!cancellationToken.IsCancellationRequested && isActiveAndEnabled)
                {
                    if (!TryGetTrigger(triggerNum, out AudioTrigger currentTrigger) ||
                        !ReferenceEquals(currentTrigger, loopTrigger))
                    {
                        return;
                    }

                    TrackEnabledEvent(PlayTraceableTrigger(triggerNum));
                    float timeUntilNextLoop = UnityEngine.Random.Range(minimumDelay, maximumDelay);
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(timeUntilNextLoop),
                        DelayType.DeltaTime,
                        PlayerLoopTiming.Update,
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the normal shutdown path for a disabled or destroyed router.
            }
        }

        private void TrackEnabledEvent(AudioHandle handle)
        {
            for (int i = activeOnEnabledEvents.Count - 1; i >= 0; i--)
            {
                if (!activeOnEnabledEvents[i].IsValid)
                {
                    activeOnEnabledEvents.RemoveAt(i);
                }
            }

            if (handle.IsValid)
            {
                activeOnEnabledEvents.Add(handle);
            }
        }

        private bool TryGetTrigger(int triggerNum, out AudioTrigger trigger)
        {
            if (triggers == null || triggerNum < 0 || triggerNum >= triggers.Length)
            {
                trigger = null;
                return false;
            }

            trigger = triggers[triggerNum];
            return trigger != null;
        }

        private void CancelLoopingTriggers()
        {
            CancellationTokenSource cancellation = loopCancellation;
            loopCancellation = null;
            if (cancellation == null)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
            }
            finally
            {
                cancellation.Dispose();
            }
        }
    }

    [Serializable]
    public class AudioTrigger
    {
        public AudioEvent eventToTrigger = null;
        public bool usePosition = false;
        public Vector3 soundPosition = Vector3.zero;
        public GameObject soundEmitter = null;
        public UnityTrigger triggerOnEvent = UnityTrigger.None;
        public bool loopTrigger = false;
        public float loopTimeMin = 0f;
        public float loopTimeMax = 0f;
    }

    public enum UnityTrigger
    {
        None,
        OnEnable,
        OnDisable,
        OnSliderUpdate
    }
}
