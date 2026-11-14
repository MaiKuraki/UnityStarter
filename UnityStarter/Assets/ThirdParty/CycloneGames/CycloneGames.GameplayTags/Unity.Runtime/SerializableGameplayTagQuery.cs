using System;
using CycloneGames.GameplayTags.Core;
using UnityEngine;

namespace CycloneGames.GameplayTags.Unity.Runtime
{
   /// <summary>
   /// A <see cref="GameplayTagQuery"/> a <see cref="MonoBehaviour"/> or <see cref="ScriptableObject"/> can
   /// hold as a serialized field.
   /// </summary>
   /// <remarks>
   /// <para>
   /// Core keeps the query tree in runtime form (containers bound to a registry), which no engine can
   /// serialize. What crosses the persistence boundary is <see cref="GameplayTagQueryPersisted"/>: flat
   /// arrays of operators, child counts and durable tag names, which Unity serializes natively and a Godot
   /// host maps onto <c>[Export]</c> arrays unchanged.
   /// </para>
   /// <para>
   /// The arrays are flat rather than nested because Unity serializes nested non-reused types to a bounded
   /// depth while the query allows a depth of <see cref="GameplayTagQuery.MaxExpressionDepth"/>.
   /// </para>
   /// <para>
   /// <b>Stale state never overwrites the durable copy.</b> If the registry was rebuilt between the last
   /// deserialize and this serialize, the container's indices no longer name the tags that were authored
   /// and <see cref="GameplayTagQueryPersisted.TryCreate"/> refuses; the arrays already on disk are kept.
   /// Names the loading registry no longer knows are skipped with a diagnostic, so removing a tag from a
   /// catalog does not make an authored asset unloadable.
   /// </para>
   /// </remarks>
   [Serializable]
   public class SerializableGameplayTagQuery : ISerializationCallbackReceiver
   {
      [SerializeField]
      private int[] operators = Array.Empty<int>();

      [SerializeField]
      private int[] childCounts = Array.Empty<int>();

      [SerializeField]
      private int[] tagCounts = Array.Empty<int>();

      [SerializeField]
      private string[] tagNames = Array.Empty<string>();

      [NonSerialized]
      private GameplayTagQuery query;

      [NonSerialized]
      private GameplayTagRegistry registry;

      // Fingerprint of the arrays the last deserialize produced. When the arrays still match it, the
      // inspector has not edited them since, so a runtime mutation of the query is the newer side.
      [NonSerialized]
      private int m_DeserializedFingerprint;

      /// <summary>
      /// The live query, rebuilt on demand. Null when nothing is authored or the payload is invalid.
      /// </summary>
      /// <remarks>
      /// Authoring data: read this once and cache it, rather than going through the bridge per evaluation.
      /// </remarks>
      public GameplayTagQuery Query
      {
         get
         {
            if (query == null)
               query = BuildFromArrays();

            return query;
         }
      }

      /// <summary>True when no query is authored.</summary>
      public bool IsEmpty => operators == null || operators.Length == 0;

      /// <summary>Binds the query to a registry for dependency-injected setups.</summary>
      public SerializableGameplayTagQuery Bind(GameplayTagRegistry target)
      {
         registry = target ?? throw new ArgumentNullException(nameof(target));
         query = BuildFromArrays();
         return this;
      }

      /// <summary>Replaces the authored payload and rebuilds the query.</summary>
      public void LoadPersisted(in GameplayTagQueryPersisted persisted)
      {
         operators = persisted.Operators ?? Array.Empty<int>();
         childCounts = persisted.ChildCounts ?? Array.Empty<int>();
         tagCounts = persisted.TagCounts ?? Array.Empty<int>();
         tagNames = persisted.TagNames ?? Array.Empty<string>();
         m_DeserializedFingerprint = ComputeFingerprint();
         query = BuildFromArrays();
      }

      /// <summary>
      /// Captures <paramref name="source"/> into the serialized payload.
      /// </summary>
      /// <returns>False when the query cannot be faithfully captured; the existing payload is kept.</returns>
      public bool TrySetQuery(GameplayTagQuery source)
      {
         if (!GameplayTagQueryPersisted.TryCreate(source, out GameplayTagQueryPersisted persisted))
            return false;

         LoadPersisted(persisted);
         query = source;
         return true;
      }

      /// <summary>The authored payload as it currently stands.</summary>
      public GameplayTagQueryPersisted ToPersisted()
         => new GameplayTagQueryPersisted(operators, childCounts, tagCounts, tagNames);

      /// <summary>Evaluates the authored query. An unauthored or invalid payload never matches.</summary>
      public bool Matches<T>(in T container) where T : IReadOnlyGameplayTagContainer
      {
         GameplayTagQuery current = Query;
         return current != null && current.Matches(container);
      }

      void ISerializationCallbackReceiver.OnBeforeSerialize()
      {
         if (ArraysEdited())
         {
            // The inspector (or a merge) changed the payload: the arrays are the newer side.
            m_DeserializedFingerprint = ComputeFingerprint();
            query = BuildFromArrays();
            return;
         }

         if (query == null)
            return;

         // Never let an empty or unresolvable query erase what is on disk. It means "not resolved yet",
         // not "nothing was configured".
         if (IsEmpty && query.RootExpression == null)
            return;

         if (GameplayTagQueryPersisted.TryCreate(query, out GameplayTagQueryPersisted captured))
            LoadPersisted(captured);
      }

      void ISerializationCallbackReceiver.OnAfterDeserialize()
      {
         m_DeserializedFingerprint = ComputeFingerprint();
         query = BuildFromArrays();
      }

      private GameplayTagQuery BuildFromArrays()
      {
         if (IsEmpty)
            return null;

         var persisted = new GameplayTagQueryPersisted(operators, childCounts, tagCounts, tagNames);
         return GameplayTagQueryPersisted.TryBuild(persisted, registry, out GameplayTagQuery built)
            ? built
            : null;
      }

      private bool ArraysEdited() => ComputeFingerprint() != m_DeserializedFingerprint;

      private int ComputeFingerprint()
      {
         unchecked
         {
            int hash = 17;
            hash = (hash * 31) + Length(operators);
            hash = (hash * 31) + Length(childCounts);
            hash = (hash * 31) + Length(tagCounts);

            if (operators != null)
               for (int i = 0; i < operators.Length; i++)
                  hash = (hash * 31) + operators[i];

            if (childCounts != null)
               for (int i = 0; i < childCounts.Length; i++)
                  hash = (hash * 31) + childCounts[i];

            if (tagCounts != null)
               for (int i = 0; i < tagCounts.Length; i++)
                  hash = (hash * 31) + tagCounts[i];

            if (tagNames != null)
               for (int i = 0; i < tagNames.Length; i++)
                  hash = (hash * 31) + (tagNames[i] == null ? 0 : StringComparer.Ordinal.GetHashCode(tagNames[i]));

            return hash;
         }
      }

      private static int Length(Array array) => array?.Length ?? 0;
   }
}
