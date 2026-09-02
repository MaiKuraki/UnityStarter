using UnityEngine;

namespace Build.Pipeline.Integrations.YooAsset3.Publication
{
    /// <summary>
    /// Unity-side binding of <see cref="IJournalSerializer"/>: delegates to
    /// <c>JsonUtility</c>, the same serializer the journals were always written with, so the wire
    /// format of existing durable journals is unchanged. This file and
    /// <see cref="PublicationRecoveryCoordinator"/> are the only Unity touchpoints in the core
    /// assembly; the headless verification harness excludes both, which is what proves every other
    /// core file is engine-free.
    /// </summary>
    internal sealed class UnityJournalSerializer : IJournalSerializer
    {
        internal static readonly UnityJournalSerializer Instance =
            new UnityJournalSerializer();

        private UnityJournalSerializer()
        {
        }

        public string ToJson<T>(T value) where T : class
        {
            return JsonUtility.ToJson(value, true);
        }

        public T FromJson<T>(string json) where T : class
        {
            return JsonUtility.FromJson<T>(json);
        }
    }
}
