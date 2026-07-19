using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CycloneGames.GameplayTags.Core
{
   [DebuggerDisplay("{TagName,nq}")]
   public sealed class GameplayTagDefinition
   {
      public static GameplayTagDefinition NoneTagDefinition { get; } = new();

      public GameplayTag Tag => new(this);

      public bool IsValid => RuntimeIndex >= 0;

      public int SourceCount => m_Sources.Count;

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public ReadOnlySpan<GameplayTagDefinition> Children => new(m_Children);

      /// <summary>
      /// The parent tags of this tag. If this tag is "A.B.C", the parent tags
      /// will be ["A", "A.B", "A.B.C"]
      /// </summary>
      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public ReadOnlySpan<GameplayTag> ParentTags
      {
         get
         {
            if (m_ParentTags != null)
               return new ReadOnlySpan<GameplayTag>(m_ParentTags);
            return m_OwningSnapshot != null
               ? m_OwningSnapshot.GetParentTagsSpan(RuntimeIndex)
               : ReadOnlySpan<GameplayTag>.Empty;
         }
      }

      /// <summary>
      /// The child tags of this tag. If this tag is "A.B.C", the child tags
      /// will be ["A.B.C.D", "A.B.C.E"]
      /// </summary>
      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public ReadOnlySpan<GameplayTag> ChildTags
      {
         get
         {
            if (m_ChildTags != null)
               return new ReadOnlySpan<GameplayTag>(m_ChildTags);
            return m_OwningSnapshot != null
               ? m_OwningSnapshot.GetChildTagsSpan(RuntimeIndex)
               : ReadOnlySpan<GameplayTag>.Empty;
         }
      }

      /// <summary>
      /// The tags in the hierarchy of this tag. If this tag is "A.B.C", the
      /// hierarchy tags will be ["A", "A.B", "A.B.C"]
      /// </summary>
      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public ReadOnlySpan<GameplayTag> HierarchyTags
      {
         get
         {
            if (m_HierarchyTags != null)
               return new ReadOnlySpan<GameplayTag>(m_HierarchyTags);
            return m_OwningSnapshot != null
               ? m_OwningSnapshot.GetHierarchyTagsSpan(RuntimeIndex)
               : ReadOnlySpan<GameplayTag>.Empty;
         }
      }

      /// <summary>
      /// The name of the tag. This is the full tag name, including the parent tags.
      /// </summary>
      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public string TagName { get; }

      public ulong StableId { get; }

      /// <summary>
      /// The description of the tag. This is to provide more information about the tag during development.
      /// </summary>
      public string Description { get; internal set; }

      /// <summary>
      /// The flags of the tag.
      /// </summary>
      public GameplayTagFlags Flags { get; }

      /// <summary>
      /// The label of the tag. This is the tag name without the parent tags.
      /// </summary>
      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public string Label { get; }

      /// <summary>
      /// The hierarchy level of the tag. This is the number of parent tags.
      /// </summary>
      public int HierarchyLevel { get; }
      public int RuntimeIndex { get; internal set; }
      public GameplayTagDefinition ParentTagDefinition { get; private set; }


      private GameplayTag[] m_ParentTags = Array.Empty<GameplayTag>();
      private GameplayTag[] m_ChildTags = Array.Empty<GameplayTag>();
      private GameplayTag[] m_HierarchyTags = Array.Empty<GameplayTag>();
      private GameplayTagDefinition[] m_Children = Array.Empty<GameplayTagDefinition>();
      private List<IGameplayTagSource> m_Sources = new();
      private int m_NameHash;
      private TagDataSnapshot m_OwningSnapshot;

      /// <summary>
      /// Default constructor to create a "None" tag definition.
      /// </summary>
      private GameplayTagDefinition()
      {
         TagName = "<None>";
         Description = string.Empty;
         Label = "None";
         HierarchyLevel = 0;
         RuntimeIndex = 0;
         ParentTagDefinition = null;
         m_ParentTags = Array.Empty<GameplayTag>();
         m_ChildTags = Array.Empty<GameplayTag>();
         m_HierarchyTags = Array.Empty<GameplayTag>();
         m_Children = Array.Empty<GameplayTagDefinition>();
         m_NameHash = StringComparer.Ordinal.GetHashCode(TagName);
         StableId = 0;
      }

      private GameplayTagDefinition(string name, string description, int runtimeIndex)
      {
         TagName = name ?? string.Empty;
         Description = description;
         Flags = GameplayTagFlags.None;
         StableId = string.IsNullOrEmpty(TagName) ? 0 : GameplayTagUtility.ComputeStableIdUnchecked(TagName);
         Label = TagName;
         HierarchyLevel = 0;
         RuntimeIndex = runtimeIndex;
         ParentTagDefinition = null;
         m_NameHash = StringComparer.Ordinal.GetHashCode(TagName);
      }

      internal GameplayTagDefinition(string name, string description, GameplayTagFlags flags = GameplayTagFlags.None)
      {
         GameplayTagUtility.ValidateName(name);

         TagName = name;
         Description = description;
         Flags = flags;
         m_NameHash = StringComparer.Ordinal.GetHashCode(name);
         StableId = GameplayTagUtility.ComputeStableIdUnchecked(name);

         Label = GameplayTagUtility.GetLabel(name);
         HierarchyLevel = GameplayTagUtility.GetHierarchyLevelFromName(name);
      }

      internal static GameplayTagDefinition CreateInvalidDefinition(string name)
      {
         return new GameplayTagDefinition(name, "Invalid Tag", -1);
      }

      /// <summary>
      /// Returns true if this tag is a child of the given tag.
      /// </summary>
      /// <param name="tag">The tag to check if this tag is a child of.</param>
      public bool IsChildOf(GameplayTag tag)
      {
         ulong tagStableId = string.IsNullOrEmpty(tag.m_Name)
            ? tag.StableId
            : GameplayTagUtility.ComputeStableIdUnchecked(tag.m_Name);
         if (m_OwningSnapshot != null &&
             m_OwningSnapshot.TryGetRuntimeIndex(tagStableId, out int tagRuntimeIndex))
         {
            return m_OwningSnapshot.IsChildOf(RuntimeIndex, tagRuntimeIndex);
         }

         if (tag.RuntimeIndex <= 0)
            return false;

         for (int i = 0; i < m_ParentTags.Length; i++)
         {
            if (m_ParentTags[i] == tag)
               return true;
         }

         return false;
      }

      /// <summary>
      /// Returns true if this tag is a parent of the given tag.
      /// </summary>
      /// <param name="tag">The tag to check if this tag is a parent of.</param>
      public bool IsParentOf(GameplayTag tag)
      {
         ulong tagStableId = string.IsNullOrEmpty(tag.m_Name)
            ? tag.StableId
            : GameplayTagUtility.ComputeStableIdUnchecked(tag.m_Name);
         if (m_OwningSnapshot != null &&
             m_OwningSnapshot.TryGetRuntimeIndex(tagStableId, out int tagRuntimeIndex))
         {
            return m_OwningSnapshot.IsParentOf(RuntimeIndex, tagRuntimeIndex);
         }

         if (tag.RuntimeIndex <= 0)
            return false;

         for (int i = 0; i < m_ChildTags.Length; i++)
         {
            if (m_ChildTags[i] == tag)
               return true;
         }

         return false;
      }

      internal void SetParent(GameplayTagDefinition parent)
      {
         ParentTagDefinition = parent;

         // Count ancestors to pre-allocate exact array size (avoids List + Reverse + ToArray)
         int count = 0;
         GameplayTagDefinition current = parent;
         while (current != null)
         {
            count++;
            current = current.ParentTagDefinition;
         }

         m_ParentTags = new GameplayTag[count];
         current = parent;
         for (int i = count - 1; i >= 0; i--)
         {
            m_ParentTags[i] = current.Tag;
            current = current.ParentTagDefinition;
         }
      }

      internal void SetChildren(List<GameplayTagDefinition> children)
      {
         m_Children = children.ToArray();
         m_ChildTags = new GameplayTag[children.Count];
         for (int i = 0; i < children.Count; i++)
            m_ChildTags[i] = children[i].Tag;
      }

      internal void SetHierarchyTags(GameplayTag[] hierarchyTags)
      {
         m_HierarchyTags = hierarchyTags;
      }

      internal void OptimizeRuntimeStorage(TagDataSnapshot owningSnapshot)
      {
         if (RuntimeIndex <= 0)
            return;

         m_OwningSnapshot = owningSnapshot ?? throw new ArgumentNullException(nameof(owningSnapshot));
         m_ParentTags = null;
         m_ChildTags = null;
         m_HierarchyTags = null;
      }

      internal void SetRuntimeIndex(int index)
      {
         RuntimeIndex = index;
      }

      internal void AddSource(IGameplayTagSource source)
      {
         if (!m_Sources.Contains(source))
            m_Sources.Add(source);
      }

      public bool IsNone()
      {
         return this == NoneTagDefinition;
      }

      public override int GetHashCode()
      {
         return m_NameHash;
      }

      public IGameplayTagSource GetSource(int index)
      {
         if (index < 0 || index >= m_Sources.Count)
            throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range.");

         return m_Sources[index];
      }

   }
}
