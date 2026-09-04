using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CycloneGames.GameplayTags.Core;
using UnityEngine;

namespace CycloneGames.GameplayTags.Unity.Runtime
{
    /// <summary>
    /// A <see cref="GameplayTagContainer"/> a <see cref="MonoBehaviour"/> or
    /// <see cref="ScriptableObject"/> can hold as a serialized field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Core is engine-neutral, so a container stores runtime indices and cannot be serialized by any
    /// engine directly. What crosses a persistence boundary is the names of the explicitly held tags,
    /// which this class keeps in a plain <c>string[]</c> Unity serializes natively, and which maps
    /// onto a Godot <c>[Export]</c> array unchanged.
    /// </para>
    /// <para>
    /// Implementing <see cref="ISerializationCallbackReceiver"/> means Unity drives the sync at the same
    /// two points it serializes everything else, so a holder never has to remember to flush. Before
    /// serialization the container's indices are converted to names; after deserialization the names are
    /// resolved back into indices.
    /// </para>
    /// <para>
    /// <b>Stale indices never overwrite durable names.</b> If the registry was rebuilt between the last
    /// deserialize and this serialize, the container's indices no longer map to the right names and
    /// <see cref="GameplayTagContainer.TryToPersisted"/> refuses. In that case the names already on disk
    /// are kept: they are the durable copy, and the container is about to be rebuilt from them anyway.
    /// Writing guessed names is what would corrupt a save.
    /// </para>
    /// <para>
    /// Names the registry no longer knows are skipped on load with a diagnostic, so a save made before a
    /// tag was removed still loads.
    /// </para>
    /// </remarks>
    [Serializable]
    public class SerializableGameplayTagContainer : IReadOnlyGameplayTagContainer, ISerializationCallbackReceiver
    {
        [SerializeField]
        private string[] explicitTagNames = Array.Empty<string>();

        [NonSerialized]
        private GameplayTagContainer container = new();

        // What the last deserialize handed the container. When the serialized array still matches this,
        // the inspector has not touched it since, so a runtime mutation of the container is the newer
        // side and is written back. When it differs, the array was edited and the array wins.
        [NonSerialized]
        private string[] m_LastDeserializedNames = Array.Empty<string>();

        // The registry instance and epoch the cached indices were resolved against. Names are the durable
        // truth and the container is a per-key cache: the play-mode transition resets the registry and
        // re-registers its sources AFTER the assets have deserialized, so a resolve-once design serves
        // indices from a registry that no longer exists.
        [NonSerialized]
        private int m_ResolvedRegistryId;
        [NonSerialized]
        private int m_ResolvedEpoch;

        /// <summary>The live container. Mutate this; serialization handles the rest.</summary>
        public GameplayTagContainer Container
        {
            get
            {
                EnsureResolved();
                return container;
            }
        }

        /// <summary>The names currently persisted for this container.</summary>
        public string[] ExplicitTagNames => explicitTagNames;

        /// <summary>
        /// Binds the container to a specific registry for dependency-injected setups. Without this the
        /// container resolves against the ambient registry.
        /// </summary>
        public SerializableGameplayTagContainer Bind(GameplayTagRegistry registry)
        {
            container = new GameplayTagContainer(registry ?? throw new ArgumentNullException(nameof(registry)));
            container.LoadPersisted(explicitTagNames);
            return this;
        }

        /// <summary>Replaces the container's contents with <paramref name="names"/>.</summary>
        public void LoadPersisted(string[] names)
        {
            explicitTagNames = names ?? Array.Empty<string>();
            ResolveFromNames();
        }

        /// <summary>True when the container holds no explicit tag.</summary>
        public bool IsEmpty
        {
            get
            {
                EnsureResolved();
                return container.IsEmpty;
            }
        }

        /// <summary>True when the container's indices still map onto the current registry epoch.</summary>
        public bool IsStale => container.IsStale;

        /// <summary>The number of explicitly held tags.</summary>
        public int ExplicitTagCount
        {
            get
            {
                EnsureResolved();
                return container.ExplicitTagCount;
            }
        }

        /// <summary>The number of tags including every ancestor of every explicit tag.</summary>
        public int TagCount
        {
            get
            {
                EnsureResolved();
                return container.TagCount;
            }
        }

        /// <summary>The expanded tag at <paramref name="index"/>, in ascending index order.</summary>
        public GameplayTag GetTag(int index)
        {
            if ((uint)index >= (uint)container.TagCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            return container.GetTag(index);
        }

        /// <summary>The explicit tag at <paramref name="index"/>, in ascending index order.</summary>
        public GameplayTag GetExplicitTag(int index)
        {
            if ((uint)index >= (uint)container.ExplicitTagCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            return container.GetExplicitTag(index);
        }

        /// <summary>True when the container holds <paramref name="runtimeIndex"/>.</summary>
        public bool ContainsRuntimeIndex(int runtimeIndex, bool explicitOnly)
            => container.ContainsRuntimeIndex(runtimeIndex, explicitOnly);

        /// <summary>Enumerates the expanded tag set.</summary>
        public GameplayTagEnumerator GetTags() => container.GetTags();

        /// <summary>Enumerates the expanded tag set.</summary>
        public GameplayTagEnumerator GetEnumerator() => container.GetTags();

        /// <summary>Enumerates the explicitly held tags.</summary>
        public GameplayTagEnumerator GetExplicitTags() => container.GetExplicitTags();

        /// <inheritdoc />
        public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags)
        {
            EnsureResolved();
            container.GetParentTags(tag, parentTags);
        }

        /// <inheritdoc />
        public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags)
        {
            EnsureResolved();
            container.GetChildTags(tag, childTags);
        }

        /// <inheritdoc />
        public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags)
        {
            EnsureResolved();
            container.GetExplicitParentTags(tag, parentTags);
        }

        /// <inheritdoc />
        public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags)
        {
            EnsureResolved();
            container.GetExplicitChildTags(tag, childTags);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        GameplayTagEnumerator IReadOnlyGameplayTagContainer.GetTags() => GetTags();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        GameplayTagEnumerator IReadOnlyGameplayTagContainer.GetExplicitTags() => GetExplicitTags();

        IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Converts to the live container. Definition-building code treats the result as read-only: it is
        /// the same instance the bridge syncs, and mutating it bypasses the serialized-name copy.
        /// </summary>
        public static implicit operator GameplayTagContainer(SerializableGameplayTagContainer bridge)
        {
            if (bridge == null)
                return new GameplayTagContainer();

            bridge.EnsureResolved();
            return bridge.container;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            EnsureResolved();
            if (!SerializedArraysUntouched())
            {
                // The serialized array was edited since the last deserialize, by the inspector, an undo,
                // or a prefab override. The array is the newer side: reload the container from it instead
                // of overwriting the edit with the container's own state.
                container.LoadPersisted(explicitTagNames);
                m_LastDeserializedNames = (string[])explicitTagNames.Clone();
                return;
            }

            // Only overwrite the durable copy when the indices are still meaningful. TryToPersisted
            // returns false exactly when they are not, which is the case where keeping what is on disk
            // is the correct behaviour.
            if (container.TryToPersisted(out string[] names))
                explicitTagNames = names ?? Array.Empty<string>();
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            ResolveFromNames();
            m_LastDeserializedNames = explicitTagNames.Length == 0
                ? Array.Empty<string>()
                : (string[])explicitTagNames.Clone();
        }

        /// <summary>
        /// Re-resolves the names against the current registry whenever the instance or epoch has changed.
        /// The play-mode transition resets the registry and re-registers its sources after these objects
        /// have already deserialized, so a resolve-once design serves indices from a registry that no
        /// longer exists - which is what emptied configured tags on entering play.
        /// </summary>
        private void EnsureResolved()
        {
            int registryId = GameplayTagManager.RegistryInstanceId;
            int epoch = GameplayTagManager.RuntimeIndexEpoch;
            if (m_ResolvedRegistryId == registryId && m_ResolvedEpoch == epoch)
                return;

            ResolveFromNames();
        }

        private void ResolveFromNames()
        {
            container.LoadPersisted(explicitTagNames);
            m_ResolvedRegistryId = GameplayTagManager.RegistryInstanceId;
            m_ResolvedEpoch = GameplayTagManager.RuntimeIndexEpoch;
        }

        private bool SerializedArraysUntouched()
        {
            if (explicitTagNames == null)
                return m_LastDeserializedNames.Length == 0;
            if (explicitTagNames.Length != m_LastDeserializedNames.Length)
                return false;

            for (int i = 0; i < explicitTagNames.Length; i++)
            {
                if (!string.Equals(explicitTagNames[i], m_LastDeserializedNames[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// A single gameplay tag a serialized field can hold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GameplayTag"/> is a bare registry index, so it is not serializable by design - a stale
    /// index would silently mean a different tag after a registry rebuild. The durable identity is the
    /// name, so that is what is stored, and <see cref="Tag"/> resolves it on demand.
    /// </para>
    /// <para>
    /// When the tag needs to exist regardless of the registry, declare a
    /// <see cref="NativeGameplayTag"/> instead; this type is for authoring data that points at whatever
    /// the registry happens to contain.
    /// </para>
    /// </remarks>
    [Serializable]
    public struct SerializableGameplayTag
    {
        [SerializeField]
        private string tagName;

        /// <summary>Creates a reference to a tag by name.</summary>
        public SerializableGameplayTag(string name)
        {
            tagName = name;
        }

        /// <summary>The stored name. This is the durable identity of the tag.</summary>
        public string TagName
        {
            get => tagName;
            set => tagName = value;
        }

        /// <summary>
        /// The resolved tag, looked up on every access: a registry that gains the tag later still
        /// resolves. Returns <see cref="GameplayTag.None"/> when nothing is registered under
        /// <see cref="TagName"/>, which is the honest answer rather than a guess.
        /// </summary>
        public GameplayTag Tag => string.IsNullOrEmpty(tagName)
            ? GameplayTag.None
            : GameplayTagManager.Request(tagName, logWarningIfNotFound: false);

        /// <summary>True when the registry currently knows this tag.</summary>
        public bool IsRegistered => !Tag.IsNone;

        /// <summary>
        /// Compares the stored names: two references name the same tag exactly when their names match,
        /// regardless of which registry (or epoch) each was resolved against.
        /// </summary>
        public bool Equals(SerializableGameplayTag other)
            => string.Equals(tagName, other.tagName, StringComparison.Ordinal);

        public override bool Equals(object obj)
            => obj is SerializableGameplayTag other && Equals(other);

        public override int GetHashCode()
            => tagName == null ? 0 : StringComparer.Ordinal.GetHashCode(tagName);

        public override string ToString() => tagName ?? "(None)";

        public static bool operator ==(SerializableGameplayTag left, SerializableGameplayTag right) => left.Equals(right);
        public static bool operator !=(SerializableGameplayTag left, SerializableGameplayTag right) => !left.Equals(right);
    }

    /// <summary>
    /// A required/forbidden tag pair a serialized field can hold.
    /// </summary>
    /// <remarks>
    /// Core's <see cref="GameplayTagRequirements"/> struct holds runtime containers, which do not
    /// serialize; authoring data goes through this type and converts on read. The <see cref="Matches"/>
    /// passthroughs exist so gameplay code that queries the pair does not have to know which form it is
    /// holding.
    /// </remarks>
    [Serializable]
    public class SerializableGameplayTagRequirements
    {
        [SerializeField]
        private SerializableGameplayTagContainer forbiddenTags = new SerializableGameplayTagContainer();

        [SerializeField]
        private SerializableGameplayTagContainer requiredTags = new SerializableGameplayTagContainer();

        /// <summary>Tags that must not be present.</summary>
        public SerializableGameplayTagContainer ForbiddenTags => forbiddenTags;

        /// <summary>Tags that must all be present.</summary>
        public SerializableGameplayTagContainer RequiredTags => requiredTags;

        /// <summary>True when neither list is configured.</summary>
        public bool IsEmpty => forbiddenTags.IsEmpty && requiredTags.IsEmpty;

        /// <summary>Builds the runtime form. The containers are referenced, not copied.</summary>
        public GameplayTagRequirements ToRequirements()
            => new GameplayTagRequirements(forbiddenTags.Container, requiredTags.Container);

        public bool Matches<T>(in T container) where T : IReadOnlyGameplayTagContainer
            => ToRequirements().Matches(container);

        public bool Matches<T, U>(in T staticContainer, in U dynamicContainer)
            where T : IReadOnlyGameplayTagContainer
            where U : IReadOnlyGameplayTagContainer
            => ToRequirements().Matches(staticContainer, dynamicContainer);

        public static implicit operator GameplayTagRequirements(SerializableGameplayTagRequirements bridge)
            => bridge?.ToRequirements() ?? new GameplayTagRequirements();
    }

    /// <summary>
    /// A <see cref="GameplayTagCountContainer"/> a serialized field can hold, for granted-tag state that
    /// must survive a save: two effects may both grant "Status.Burning", and the tag stays present until
    /// both are removed, so the counts have to persist with it.
    /// </summary>
    /// <remarks>
    /// Same contract as <see cref="SerializableGameplayTagContainer"/>, plus a parallel count array. The
    /// two arrays are kept index-aligned by construction: the names are written in ascending index order
    /// and the counts follow them.
    /// </remarks>
    [Serializable]
    public class SerializableGameplayTagCountContainer : ISerializationCallbackReceiver
    {
        [SerializeField]
        private string[] explicitTagNames = Array.Empty<string>();

        [SerializeField]
        private int[] explicitTagCounts = Array.Empty<int>();

        [NonSerialized]
        private GameplayTagCountContainer container = new();

        [NonSerialized]
        private string[] m_LastDeserializedNames = Array.Empty<string>();

        // The registry instance and epoch the cached indices were resolved against. Names are the durable
        // truth and the container is a per-key cache: the play-mode transition resets the registry and
        // re-registers its sources AFTER the assets have deserialized, so a resolve-once design serves
        // indices from a registry that no longer exists.
        [NonSerialized]
        private int m_ResolvedRegistryId;
        [NonSerialized]
        private int m_ResolvedEpoch;
        [NonSerialized]
        private int[] m_LastDeserializedCounts = Array.Empty<int>();

        /// <summary>The live count container. Mutate this; serialization handles the rest.</summary>
        public GameplayTagCountContainer Container => container;

        public string[] ExplicitTagNames => explicitTagNames;
        public int[] ExplicitTagCounts => explicitTagCounts;

        public SerializableGameplayTagCountContainer Bind(GameplayTagRegistry registry)
        {
            container = new GameplayTagCountContainer(registry ?? throw new ArgumentNullException(nameof(registry)));
            LoadFromArrays();
            return this;
        }

        /// <summary>Replaces the container's contents with the given name/count pairs.</summary>
        public void LoadPersisted(string[] names, int[] counts)
        {
            explicitTagNames = names ?? Array.Empty<string>();
            explicitTagCounts = counts ?? Array.Empty<int>();
            LoadFromArrays();
        }

        public bool IsStale => container.IsStale;
        public int ExplicitTagCount => container.ExplicitTagCount;
        public int TagCount => container.TagCount;

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            if (!CountArraysUntouched())
            {
                container.Clear();
                int edited = Math.Min(explicitTagNames.Length, explicitTagCounts.Length);
                for (int i = 0; i < edited; i++)
                {
                    if (string.IsNullOrEmpty(explicitTagNames[i]) || explicitTagCounts[i] <= 0)
                        continue;

                    if (GameplayTagManager.TryRequest(explicitTagNames[i], out GameplayTag tag))
                        container.AddTag(tag, explicitTagCounts[i]);
                }

                m_LastDeserializedNames = (string[])explicitTagNames.Clone();
                m_LastDeserializedCounts = (int[])explicitTagCounts.Clone();
                return;
            }

            // A count container has no index-only snapshot to be stale against; its counts are resolved
            // through the names it already holds, so converting is always safe.
            int count = container.ExplicitTagCount;
            string[] names = new string[count];
            int[] counts = new int[count];
            for (int i = 0; i < count; i++)
            {
                GameplayTag tag = container.GetExplicitTag(i);
                names[i] = GameplayTagManager.GetName(tag.RuntimeIndex);
                counts[i] = container.GetExplicitTagCount(tag);
            }

            explicitTagNames = names;
            explicitTagCounts = counts;
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            LoadFromArrays();
            m_LastDeserializedNames = (string[])explicitTagNames.Clone();
            m_LastDeserializedCounts = (int[])explicitTagCounts.Clone();
        }

        private bool CountArraysUntouched()
        {
            if (explicitTagNames.Length != m_LastDeserializedNames.Length ||
                explicitTagCounts.Length != m_LastDeserializedCounts.Length)
            {
                return false;
            }

            for (int i = 0; i < explicitTagNames.Length; i++)
            {
                if (!string.Equals(explicitTagNames[i], m_LastDeserializedNames[i], StringComparison.Ordinal))
                    return false;
            }

            for (int i = 0; i < explicitTagCounts.Length; i++)
            {
                if (explicitTagCounts[i] != m_LastDeserializedCounts[i])
                    return false;
            }

            return true;
        }

        private void LoadFromArrays()
        {
            container.Clear();

            int count = Math.Min(explicitTagNames.Length, explicitTagCounts.Length);
            for (int i = 0; i < count; i++)
            {
                string name = explicitTagNames[i];
                int repeats = explicitTagCounts[i];
                if (string.IsNullOrEmpty(name) || repeats <= 0)
                    continue;

                if (GameplayTagManager.TryRequest(name, out GameplayTag tag))
                    container.AddTag(tag, repeats);
            }
        }
    }
}
