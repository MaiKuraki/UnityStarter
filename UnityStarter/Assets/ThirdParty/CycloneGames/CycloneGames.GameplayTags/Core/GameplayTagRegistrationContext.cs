using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// A terminal registration failure. Once recorded, every source must stop enumerating input.
   /// </summary>
   internal sealed class GameplayTagRegistrationError
   {
      internal string Message { get; }
      internal IGameplayTagSource Source { get; }
      internal string TagName { get; }

      internal GameplayTagRegistrationError(string message, IGameplayTagSource source, string tagName)
      {
         Message = message;
         Source = source;
         TagName = tagName;
      }
   }

   /// <summary>
   /// Accumulates tag declarations from sources and catalogs, then flattens them into one immutable
   /// registry snapshot.
   /// </summary>
   /// <remarks>
   /// <para>
   /// A context is a single-use build scratch space. It is not thread safe; the registry owns a lock for
   /// the duration of a build and no reference to the context escapes it.
   /// </para>
   /// <para>
   /// Tag ordering is fully determined by the comparison used in <see cref="Build"/>, which is a total
   /// order over unique names. Identical input in any order on any runtime therefore produces identical
   /// runtime indices, which is what makes indices safe to bake into build data and to replicate.
   /// </para>
   /// </remarks>
   public sealed class GameplayTagRegistrationContext
   {
      internal const int DefaultMaxRegistrationAttemptCount = GameplayTagUtility.MaxRegisteredTagCount * 2;
      internal const int DefaultMaxRetainedDiagnosticCount = 128;

      private const string NoneTagName = "<None>";

      private readonly List<string> m_Names = new();
      private readonly List<string> m_Descriptions = new();
      private readonly List<GameplayTagFlags> m_Flags = new();
      private readonly Dictionary<string, int> m_SlotByName = new(StringComparer.Ordinal);
      private readonly List<GameplayTagRegistrationError> m_RegistrationErrors = new();
      private readonly int m_MaxRegisteredTagCount;
      private readonly int m_MaxRegistrationAttemptCount;
      private readonly int m_MaxRetainedDiagnosticCount;

      private Dictionary<string, List<IGameplayTagSource>> m_SourcesByName;
      private GameplayTagRegistrationError m_TerminalRegistrationError;
      private int m_RegistrationAttemptCount;
      private int m_TotalRegistrationErrorCount;
      private int m_SuppressedRegistrationErrorCount;

      /// <summary>True after a terminal budget error. Sources must stop enumerating input.</summary>
      public bool IsRegistrationTerminated => m_TerminalRegistrationError != null;

      /// <summary>Tags accumulated so far, excluding implicit parents that have not been added yet.</summary>
      public int RegisteredTagCount => m_Names.Count;

      public GameplayTagRegistrationContext()
         : this(
            GameplayTagUtility.MaxRegisteredTagCount,
            DefaultMaxRegistrationAttemptCount,
            DefaultMaxRetainedDiagnosticCount)
      { }

      internal GameplayTagRegistrationContext(int maxRegisteredTagCount)
         : this(
            maxRegisteredTagCount,
            DefaultMaxRegistrationAttemptCount,
            DefaultMaxRetainedDiagnosticCount)
      { }

      internal GameplayTagRegistrationContext(
         int maxRegisteredTagCount,
         int maxRegistrationAttemptCount,
         int maxRetainedDiagnosticCount)
      {
         if (maxRegisteredTagCount <= 0 || maxRegisteredTagCount > GameplayTagUtility.MaxRegisteredTagCount)
            throw new ArgumentOutOfRangeException(nameof(maxRegisteredTagCount));
         if (maxRegistrationAttemptCount < maxRegisteredTagCount)
            throw new ArgumentOutOfRangeException(nameof(maxRegistrationAttemptCount));
         if (maxRetainedDiagnosticCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRetainedDiagnosticCount));

         m_MaxRegisteredTagCount = maxRegisteredTagCount;
         m_MaxRegistrationAttemptCount = maxRegistrationAttemptCount;
         m_MaxRetainedDiagnosticCount = maxRetainedDiagnosticCount;
      }

      /// <summary>
      /// Declares one tag. Redeclaration is not an error: the first declaration wins for flags, and a
      /// description is back-filled only when the existing one is empty.
      /// </summary>
      /// <returns>True when the tag is present in the context after the call.</returns>
      public bool RegisterTag(
         string name,
         string description = null,
         GameplayTagFlags flags = GameplayTagFlags.None,
         IGameplayTagSource source = null)
      {
         if (!TryBeginRegistrationAttempt(name, source))
            return false;

         if (!GameplayTagUtility.IsNameValid(name, out string errorMessage))
         {
            AddRegistrationError(errorMessage, source, name);
            return false;
         }

         if (m_SlotByName.TryGetValue(name, out int slot))
         {
            if (string.IsNullOrEmpty(m_Descriptions[slot]) && !string.IsNullOrEmpty(description))
               m_Descriptions[slot] = description;

            if (source != null)
               TrackSource(name, source);

            return true;
         }

         if (m_Names.Count >= m_MaxRegisteredTagCount)
         {
            TerminateRegistration($"Registry tag count cannot exceed {m_MaxRegisteredTagCount}.", source, name);
            return false;
         }

         m_SlotByName.Add(name, m_Names.Count);
         m_Names.Add(name);
         m_Descriptions.Add(description ?? string.Empty);
         m_Flags.Add(flags);

         if (source != null)
            TrackSource(name, source);

         return true;
      }

      /// <summary>
      /// Flattens the accumulated tags into the arrays a <see cref="TagDataSnapshot"/> is built from.
      /// </summary>
      /// <param name="preferredRuntimeIndices">
      /// Optional name-to-index hints used when a live registry is rebuilt and existing indices must be
      /// preserved. Names absent from the map are appended after every mapped name, in ordinal order.
      /// </param>
      /// <returns>Null when a terminal registration error occurred.</returns>
      internal GameplayTagBuildResult Build(IReadOnlyDictionary<string, int> preferredRuntimeIndices = null)
      {
         if (HasRegistrationErrors)
            return null;

         if (!RegisterMissingParents())
            return null;

         int count = m_Names.Count;
         int[] order = new int[count];
         for (int i = 0; i < count; i++)
            order[i] = i;

         Array.Sort(order, CreateOrderComparison(preferredRuntimeIndices));

         string[] names = new string[count + 1];
         string[] descriptions = new string[count + 1];
         GameplayTagFlags[] flags = new GameplayTagFlags[count + 1];
         int[] parents = new int[count + 1];

         names[0] = NoneTagName;
         descriptions[0] = string.Empty;
         flags[0] = GameplayTagFlags.None;
         parents[0] = 0;

         for (int i = 0; i < count; i++)
         {
            int slot = order[i];
            names[i + 1] = m_Names[slot];
            descriptions[i + 1] = m_Descriptions[slot];
            flags[i + 1] = m_Flags[slot];
         }

         Dictionary<string, int> indexByName = new(count, StringComparer.Ordinal);
         for (int i = 1; i <= count; i++)
            indexByName.Add(names[i], i);

         for (int i = 1; i <= count; i++)
         {
            string parentName = GameplayTagUtility.GetParentNameUnchecked(names[i]);
            parents[i] = parentName != null && indexByName.TryGetValue(parentName, out int parentIndex)
               ? parentIndex
               : 0;
         }

         return new GameplayTagBuildResult(names, descriptions, flags, parents);
      }

      /// <summary>
      /// Adds a name that a previous build already validated and indexed.
      /// </summary>
      /// <remarks>
      /// Name validation and the registration-attempt budget are skipped because the caller's data came
      /// from a published snapshot, where both have already been paid and enforced. This is what keeps a
      /// preserve-indices rebuild - an authoring refresh or a dynamic add - linear in the tag count
      /// instead of re-validating every name on every call.
      /// </remarks>
      internal void Adopt(string name, string description, GameplayTagFlags flags)
      {
         if (IsRegistrationTerminated || m_SlotByName.ContainsKey(name))
            return;

         if (m_Names.Count >= m_MaxRegisteredTagCount)
         {
            TerminateRegistration(
               $"Registry tag count cannot exceed {m_MaxRegisteredTagCount}.",
               null,
               name);
            return;
         }

         m_SlotByName.Add(name, m_Names.Count);
         m_Names.Add(name);
         m_Descriptions.Add(description ?? string.Empty);
         m_Flags.Add(flags);
      }

      /// <summary>
      /// The sources that declared a tag, in registration order. Returns an empty list when the tag has
      /// no tracked source. Editor authoring only; this is never on a runtime path.
      /// </summary>
      internal List<IGameplayTagSource> GetSources(string tagName)
      {
         if (m_SourcesByName != null && m_SourcesByName.TryGetValue(tagName, out List<IGameplayTagSource> sources))
            return sources;

         return EmptySources;
      }

      private static readonly List<IGameplayTagSource> EmptySources = new(0);

      /// <summary>
      /// The source map built by the most recent <see cref="Build"/>, or null when no source registered
      /// itself. Returned to the registry so authoring tooling can ask who declared a tag without Core
      /// paying for the map in builds that have no sources - a Player build built purely from build data.
      /// </summary>
      internal Dictionary<string, List<IGameplayTagSource>> CaptureSourceMap() => m_SourcesByName;

      /// <summary>
      /// Synthesizes every ancestor that no source declared, walking each name's prefixes shortest first
      /// so a synthesized ancestor is itself fully parented before it is visited.
      /// </summary>
      /// <remarks>
      /// An implicit parent carries <see cref="GameplayTagFlags.None"/> and no description. It exists
      /// solely to complete the hierarchy; no source declared it, so it has no flags and no author to
      /// inherit from. Propagating a descendant's flags upward would make a tag's flags depend on
      /// registration order, which is both wrong and nondeterministic.
      /// </remarks>
      private bool RegisterMissingParents()
      {
         // The list grows while this loop runs. Each appended ancestor is visited in turn, and because
         // prefixes are produced shortest-first, its own ancestors are already present by then.
         for (int i = 0; i < m_Names.Count; i++)
         {
            string name = m_Names[i];
            for (int c = 0; c < name.Length; c++)
            {
               if (name[c] != '.')
                  continue;

               string ancestor = name.Substring(0, c);
               if (m_SlotByName.ContainsKey(ancestor))
                  continue;

               if (m_Names.Count >= m_MaxRegisteredTagCount)
               {
                  TerminateRegistration(
                     $"Registry tag count including implicit parents cannot exceed {m_MaxRegisteredTagCount}.",
                     null,
                     name);
                  return false;
               }

               m_SlotByName.Add(ancestor, m_Names.Count);
               m_Names.Add(ancestor);
               m_Descriptions.Add(string.Empty);
               m_Flags.Add(GameplayTagFlags.None);
            }
         }

         return true;
      }

      private Comparison<int> CreateOrderComparison(IReadOnlyDictionary<string, int> preferredRuntimeIndices)
      {
         List<string> names = m_Names;

         if (preferredRuntimeIndices == null || preferredRuntimeIndices.Count == 0)
            return (a, b) => string.Compare(names[a], names[b], StringComparison.Ordinal);

         return (a, b) =>
         {
            bool hasA = preferredRuntimeIndices.TryGetValue(names[a], out int indexA);
            bool hasB = preferredRuntimeIndices.TryGetValue(names[b], out int indexB);
            if (hasA && hasB)
               return indexA.CompareTo(indexB);
            if (hasA)
               return -1;
            if (hasB)
               return 1;
            return string.Compare(names[a], names[b], StringComparison.Ordinal);
         };
      }

      private void TrackSource(string tagName, IGameplayTagSource source)
      {
         m_SourcesByName ??= new Dictionary<string, List<IGameplayTagSource>>(StringComparer.Ordinal);
         if (!m_SourcesByName.TryGetValue(tagName, out List<IGameplayTagSource> sources))
         {
            sources = new List<IGameplayTagSource>(1);
            m_SourcesByName.Add(tagName, sources);
         }

         if (!sources.Contains(source))
            sources.Add(source);
      }

      internal IEnumerable<GameplayTagRegistrationError> GetRegistrationErrors()
      {
         for (int i = 0; i < m_RegistrationErrors.Count; i++)
            yield return m_RegistrationErrors[i];

         if (m_TerminalRegistrationError != null)
            yield return m_TerminalRegistrationError;
      }

      internal bool HasRegistrationErrors => m_TotalRegistrationErrorCount != 0;
      internal int RegistrationErrorCount => m_TotalRegistrationErrorCount;
      internal int SuppressedRegistrationErrorCount => m_SuppressedRegistrationErrorCount;
      internal int RegistrationAttemptCount => m_RegistrationAttemptCount;

      private bool TryBeginRegistrationAttempt(string name, IGameplayTagSource source)
      {
         if (IsRegistrationTerminated)
            return false;

         if (m_RegistrationAttemptCount >= m_MaxRegistrationAttemptCount)
         {
            TerminateRegistration(
               $"Gameplay tag registration attempts cannot exceed {m_MaxRegistrationAttemptCount} per registry candidate.",
               source,
               name);
            return false;
         }

         m_RegistrationAttemptCount++;
         return true;
      }

      private void AddRegistrationError(string message, IGameplayTagSource source, string tagName)
      {
         m_TotalRegistrationErrorCount++;
         if (m_RegistrationErrors.Count < m_MaxRetainedDiagnosticCount)
            m_RegistrationErrors.Add(new GameplayTagRegistrationError(message, source, tagName));
         else
            m_SuppressedRegistrationErrorCount++;
      }

      private void TerminateRegistration(string message, IGameplayTagSource source, string tagName)
      {
         if (IsRegistrationTerminated)
            return;

         m_TerminalRegistrationError = new GameplayTagRegistrationError(message, source, tagName);
         m_TotalRegistrationErrorCount++;
      }
   }
}
