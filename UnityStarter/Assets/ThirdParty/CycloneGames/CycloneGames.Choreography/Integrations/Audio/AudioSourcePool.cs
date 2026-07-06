using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.Choreography.Audio
{
    /// <summary>
    /// Small pool of <see cref="AudioSource"/> components parented under a single root object. Rented sources are
    /// configured by the caller and returned when a voice ends. The pool grows on demand and never destroys
    /// sources, so steady-state playback allocates nothing. Not thread-safe: use from the main thread only.
    /// </summary>
    internal sealed class AudioSourcePool
    {
        private readonly Transform _root;
        private readonly Stack<AudioSource> _free;

        public AudioSourcePool(Transform root, int initialCapacity)
        {
            _root = root;
            _free = new Stack<AudioSource>(initialCapacity < 1 ? 1 : initialCapacity);
            for (int i = 0; i < initialCapacity; i++)
            {
                _free.Push(Create());
            }
        }

        public AudioSource Rent()
        {
            AudioSource source = _free.Count > 0 ? _free.Pop() : Create();
            source.gameObject.SetActive(true);
            return source;
        }

        public void Return(AudioSource source)
        {
            if (source == null)
            {
                return;
            }
            source.Stop();
            source.clip = null;
            source.loop = false;
            source.gameObject.SetActive(false);
            _free.Push(source);
        }

        private AudioSource Create()
        {
            GameObject go = new GameObject("ChoreographyAudioVoice");
            go.transform.SetParent(_root, false);
            go.SetActive(false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }
    }
}
