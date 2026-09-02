using System;

namespace Build.Pipeline.Integrations.YooAsset3.Publication
{
    /// <summary>
    /// JSON boundary for the durable journal documents. The publication core is engine-free, so
    /// serialization is injected instead of referenced: the Unity host binds this to
    /// <c>JsonUtility</c> (see <see cref="UnityJournalSerializer"/>), the headless test
    /// harness binds its own double. Keeping the boundary an injected parameter is what lets the
    /// journal, the recovery engine and the relocation journal compile and run under a plain CLR —
    /// the failure mode this split exists to prevent is a "core" that cannot be read back without
    /// the Unity editor or the YooAsset package installed.
    /// </summary>
    internal interface IJournalSerializer
    {
        /// <summary>Serializes <paramref name="value"/> (pretty-printed, stable field order).</summary>
        string ToJson<T>(T value) where T : class;

        /// <summary>Deserializes <paramref name="json"/>; returns null for invalid input.</summary>
        T FromJson<T>(string json) where T : class;
    }
}
