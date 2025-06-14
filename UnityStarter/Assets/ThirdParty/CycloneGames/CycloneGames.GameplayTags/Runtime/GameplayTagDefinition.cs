using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CycloneGames.GameplayTags.Runtime
{
   [DebuggerDisplay("{TagName,nq}")]
   internal class GameplayTagDefinition
   {
      public GameplayTag Tag => new(TagName, RuntimeIndex);

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public ReadOnlySpan<GameplayTagDefinition> Children => new(m_Children);

      /// <summary>
      /// The parent tags of this tag. If this tag is "A.B.C", the parent tags
      /// will be ["A", "A.B", "A.B.C"]
      /// </summary>
      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public ReadOnlySpan<GameplayTag> ParentTags => new(m_ParentTags);

      /// <summary>
      /// The child tags of this tag. If this tag is "A.B.C", the child tags
      /// will be ["A.B.C.D", "A.B.C.E"]
      /// </summary>
      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public ReadOnlySpan<GameplayTag> ChildTags => new(m_ChildTags);

      /// <summary>
      /// The tags in the hierarchy of this tag. If this tag is "A.B.C", the
      /// hierarchy tags will be ["A", "A.B", "A.B.C"]
      /// </summary>
      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public ReadOnlySpan<GameplayTag> HierarchyTags => new(m_HierarchyTags);

      /// <summary>
      /// The name of the tag. This is the full tag name, including the parent tags.
      /// </summary>
      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public string TagName { get; }

      /// <summary>
      /// The description of the tag. This is to provide more information about the tag during development.
      /// </summary>
      public string Description { get; }

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

      private GameplayTag[] m_ChildTags;
      private GameplayTag[] m_HierarchyTags;
      private GameplayTag[] m_ParentTags;
      private GameplayTagDefinition[] m_Children;

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
      }

      public GameplayTagDefinition(string name, string description, GameplayTagFlags flags = GameplayTagFlags.None)
      {
         TagName = name;
         Description = description;
         Flags = flags;

         Label = GameplayTagUtility.GetLabel(name);
         HierarchyLevel = GameplayTagUtility.GetHeirarchyLevelFromName(name);
      }

      public static GameplayTagDefinition CreateNoneTagDefinition()
      {
         return new GameplayTagDefinition();
      }

      /// <summary>
      /// Returns true if this tag is a child of the given tag.
      /// </summary>
      /// <param name="tag">The tag to check if this tag is a child of.</param>
      public bool IsChildOf(GameplayTag tag)
      {
         if (RuntimeIndex <= tag.RuntimeIndex)
         {
            return false;
         }

         if (m_ParentTags.Length > 1 && tag.RuntimeIndex < m_ParentTags[0].RuntimeIndex)
         {
            return false;
         }

         for (int i = 0; i < m_ParentTags.Length; i++)
         {
            if (m_ParentTags[i] == tag)
            {
               return true;
            }
         }

         return false;
      }

      /// <summary>
      /// Returns true if this tag is a parent of the given tag.
      /// </summary>
      /// <param name="tag">The tag to check if this tag is a parent of.</param>
      public bool IsParentOf(GameplayTag tag)
      {
         if (RuntimeIndex >= tag.RuntimeIndex)
         {
            return false;
         }

         if (m_ChildTags.Length > 1 && tag.RuntimeIndex > m_ChildTags[^1].RuntimeIndex)
         {
            return false;
         }

         for (int i = 0; i < m_ChildTags.Length; i++)
         {
            if (m_ChildTags[i] == tag)
            {
               return true;
            }
         }

         return false;
      }

      public void SetParent(GameplayTagDefinition parent)
      {
         ParentTagDefinition = parent;
         List<GameplayTag> tags = new();

         GameplayTagDefinition current = parent;
         while (current != null)
         {
            tags.Add(current.Tag);
            current = current.ParentTagDefinition;
         }

         tags.Reverse();
         m_ParentTags = tags.ToArray();
      }

      public void SetChildren(List<GameplayTagDefinition> children)
      {
         m_Children = children.ToArray();
         m_ChildTags = children.Select(c => c.Tag).ToArray();
      }

      public void SetHierarchyTags(GameplayTag[] hierarchyTags)
      {
         m_HierarchyTags = hierarchyTags;
      }

      public void SetRuntimeIndex(int index)
      {
         RuntimeIndex = index;
      }
   }
}