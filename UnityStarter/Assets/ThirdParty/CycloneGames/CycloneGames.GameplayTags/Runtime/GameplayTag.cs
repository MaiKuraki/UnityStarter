using System;
using System.Diagnostics;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace CycloneGames.GameplayTags.Runtime
{
   [Serializable]
   [DebuggerDisplay("{m_Name,nq}")]
   public struct GameplayTag : IEquatable<GameplayTag>
#if UNITY_5_3_OR_NEWER
      , ISerializationCallbackReceiver
#endif
   {
      /// <summary>
      /// Represents an invalid tag.
      /// </summary>
      public static readonly GameplayTag None = new() { m_Definition = GameplayTagDefinition.NoneTagDefinition };

      public readonly bool IsNone => m_Definition == null || m_Definition == GameplayTagDefinition.NoneTagDefinition;

      public readonly bool IsValid => m_Definition != null && m_Definition.IsValid;

      public readonly bool IsLeaf => m_Definition != null && m_Definition.Children.Length == 0;

      internal readonly int RuntimeIndex => m_Definition.RuntimeIndex;

      internal readonly GameplayTagDefinition Definition
      {
         get
         {
            ValidateIsNotNone();
            return m_Definition ?? GameplayTagDefinition.NoneTagDefinition;
         }
      }

      /// <inheritdoc cref="GameplayTagDefinition.ParentTags" />
      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public readonly ReadOnlySpan<GameplayTag> ParentTags => Definition.ParentTags;

      /// <inheritdoc cref="GameplayTagDefinition.ChildTags" />
      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public readonly ReadOnlySpan<GameplayTag> ChildTags => Definition.ChildTags;

      /// <inheritdoc cref="GameplayTagDefinition.HierarchyTags" />
      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public readonly ReadOnlySpan<GameplayTag> HierarchyTags => Definition.HierarchyTags;

      /// <inheritdoc cref="GameplayTagDefinition.Label" />
      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public readonly string Label => Definition.Label;

      /// <inheritdoc cref="GameplayTagDefinition.HierarchyLevel" />
      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public readonly int HierarchyLevel => Definition.HierarchyLevel;

      /// <inheritdoc cref="GameplayTagDefinition.Description" />
      public readonly string Description => Definition.Description;

      /// <summary>
      /// The parent tag of this tag. If this tag is "A.B.C", the parent tag will be "A.B".
      /// </summary>
      public readonly GameplayTag ParentTag
      {
         get
         {
            GameplayTagDefinition parentDefinition = Definition.ParentTagDefinition;

            if (parentDefinition == null)
               return None;

            return parentDefinition.Tag;
         }
      }

      /// <inheritdoc cref="GameplayTagDefinition.Flags" />
      public readonly GameplayTagFlags Flags => Definition.Flags;

      public readonly string Name
      {
         get
         {
            ValidateIsNotNone();
            return m_Name;
         }
      }

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
#if UNITY_5_3_OR_NEWER
      [SerializeField]
#endif
      private string m_Name;

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      private GameplayTagDefinition m_Definition;

      internal GameplayTag(GameplayTagDefinition definition)
      {
         m_Definition = definition ?? GameplayTagDefinition.NoneTagDefinition;
         m_Name = m_Definition.TagName;
      }

      /// <inheritdoc cref="GameplayTagDefinition.IsChildOf(GameplayTag)"/>/>
      public readonly bool IsParentOf(in GameplayTag tag)
      {
         ValidateIsNotNone();
         return Definition.IsParentOf(tag);
      }

      /// <inheritdoc cref="GameplayTagDefinition.IsChildOf(GameplayTag)"/>/>
      public readonly bool IsChildOf(in GameplayTag parentTag)
      {
         ValidateIsNotNone();
         return Definition.IsChildOf(parentTag);
      }

      public readonly bool Equals(GameplayTag other)
      {
         return m_Definition == other.m_Definition;
      }

      public override readonly bool Equals(object obj)
      {
         if (obj is GameplayTag other)
            return m_Definition == other.m_Definition;

         if (obj is string otherStr)
            return m_Name == otherStr;

         return false;
      }

      public override readonly int GetHashCode()
      {
         return Definition.GetHashCode();
      }

      public override readonly string ToString()
      {
         if (IsNone)
            return "<None>";

         return m_Name;
      }

#if UNITY_5_3_OR_NEWER
      void ISerializationCallbackReceiver.OnAfterDeserialize()
      {
         if (string.IsNullOrEmpty(m_Name))
         {
            this = None;
            return;
         }

         this = GameplayTagManager.RequestTag(m_Name);
         if (!IsValid)
            UnityEngine.Debug.LogWarning($"No tag registered with name \"{m_Name}\".");
      }

      void ISerializationCallbackReceiver.OnBeforeSerialize()
      {
         if (IsNone)
         {
            m_Name = null;
            return;
         }

         m_Name = Definition.TagName;
      }
#endif

      [Conditional("DEBUG")]
      private readonly void ValidateIsNotNone()
      {
         if (IsNone)
            throw new InvalidOperationException("Cannot perform operation on GameplayTag.None.");
      }

      [Conditional("DEBUG")]
      internal readonly void ValidateIsValid()
      {
         if (IsNone)
            throw new InvalidOperationException("Cannot perform operation on GameplayTag.None.");

         if (!IsValid)
            throw new InvalidOperationException($"GameplayTag \"{m_Name}\" is not valid.");
      }

      public static implicit operator GameplayTag(string tagName)
      {
         return GameplayTagManager.RequestTag(tagName);
      }

      public static bool operator ==(in GameplayTag lhs, in GameplayTag rhs)
      {
         return lhs.Definition == rhs.Definition;
      }

      public static bool operator !=(in GameplayTag lhs, in GameplayTag rhs)
      {
         return lhs.Definition != rhs.Definition;
      }
   }
}
