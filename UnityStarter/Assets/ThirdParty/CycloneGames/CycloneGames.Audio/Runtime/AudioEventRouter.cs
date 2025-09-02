using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CycloneGames.Audio.Runtime;

public class AudioEventRouter : MonoBehaviour
{
    public AudioTrigger[] triggers;
    private List<ActiveEvent> activeOnEnabledEvents = new List<ActiveEvent>();
    private const float MIN_LOOP_TIME = 1.0f;
    private const float MAX_LOOP_TIME = 1000.0f;

    public void OnEnable()
    {
        for (int i = 0; i < this.triggers.Length; i++)
        {
            AudioTrigger tempTrigger = this.triggers[i];
            if (tempTrigger.triggerOnEvent == UnityTrigger.OnEnable)
            {
                if (tempTrigger.loopTrigger)
                {
                    StartLoopingTrigger(i);
                }
                else
                {
                    this.activeOnEnabledEvents.Add(PlayTraceableTrigger(i));
                }
            }
        }
    }

    public void OnDisable()
    {
        for (int i = 0; i < this.triggers.Length; i++)
        {
            AudioTrigger tempTrigger = this.triggers[i];
            if (tempTrigger.triggerOnEvent == UnityTrigger.OnDisable)
            {
                PlayAudioTrigger(i);
            }

            StopEnabledEvents();
        }
    }

    public void PlayAudioEvent(AudioEvent eventToPlay)
    {
        AudioManager.PlayEvent(eventToPlay, this.gameObject);
    }

    public void PlayAudioTrigger(int triggerNum)
    {
        PlayTraceableTrigger(triggerNum);
    }

    public void StartLoopingTrigger(int triggerNum)
    {
        StartCoroutine(PlayLoopingTrigger(triggerNum));
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
        for (int i = 0; i < this.activeOnEnabledEvents.Count; i++)
        {
            ActiveEvent tempEvent = this.activeOnEnabledEvents[i];
            if (tempEvent != null)
            {
                tempEvent.Stop();
            }
        }

        this.activeOnEnabledEvents.Clear();
    }

    private ActiveEvent PlayTraceableTrigger(int triggerNum)
    {
        AudioTrigger tempTrigger = triggers[triggerNum];
        if (!tempTrigger.usePosition && tempTrigger.soundEmitter == null)
        {
            return AudioManager.PlayEvent(tempTrigger.eventToTrigger, this.gameObject);
        }
        else if (tempTrigger.usePosition)
        {
            return AudioManager.PlayEvent(tempTrigger.eventToTrigger, tempTrigger.soundPosition);
        }
        else
        {
            return AudioManager.PlayEvent(tempTrigger.eventToTrigger, tempTrigger.soundEmitter);
        }
    }

    private IEnumerator PlayLoopingTrigger(int triggerNum)
    {
        AudioTrigger loopTrigger = triggers[triggerNum];
        loopTrigger.loopTimeMin = Mathf.Clamp(loopTrigger.loopTimeMin, MIN_LOOP_TIME, MAX_LOOP_TIME);
        loopTrigger.loopTimeMax = Mathf.Clamp(loopTrigger.loopTimeMax, MIN_LOOP_TIME, MAX_LOOP_TIME);
        while (this.enabled)
        {
            PlayAudioTrigger(triggerNum);
            float timeUntilNextLoop = Random.Range(loopTrigger.loopTimeMin, loopTrigger.loopTimeMax);
            yield return new WaitForSeconds(timeUntilNextLoop);
        }
    }
}

[System.Serializable]
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