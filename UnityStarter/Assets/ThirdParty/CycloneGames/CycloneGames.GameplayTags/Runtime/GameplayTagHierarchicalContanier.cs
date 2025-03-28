using System.Collections;
using System.Collections.Generic;

namespace CycloneGames.GameplayTags
{
   public class GameplayTagHierarchicalContainer : IGameplayTagCountContainer
   {
      public event OnTagCountChangedDelegate OnAnyTagCountChange
      {
         add => m_UnderlyingContainer.OnAnyTagCountChange += value;
         remove => m_UnderlyingContainer.OnAnyTagCountChange -= value;
      }

      public event OnTagCountChangedDelegate OnAnyTagNewOrRemove
      {
         add => m_UnderlyingContainer.OnAnyTagNewOrRemove += value;
         remove => m_UnderlyingContainer.OnAnyTagNewOrRemove -= value;
      }

      public bool IsEmpty => m_UnderlyingContainer.IsEmpty;
      public int ExplicitTagCount => m_UnderlyingContainer.ExplicitTagCount;
      public int TagCount => m_UnderlyingContainer.TagCount;
      public GameplayTagContainerIndices Indices => m_UnderlyingContainer.Indices;

      public IGameplayTagCountContainer Parent
      {
         get => m_ParentContainer;
         set
         {
            m_ParentContainer?.RemoveTags(this);
            m_ParentContainer = value;
            m_ParentContainer?.AddTags(this);
         }
      }

      private GameplayTagCountContainer m_UnderlyingContainer = new();
      private IGameplayTagCountContainer m_ParentContainer;


      public void AddTag(GameplayTag gameplayTag)
      {
         m_ParentContainer?.AddTag(gameplayTag);
         m_UnderlyingContainer.AddTag(gameplayTag);
      }

      public void AddTags<T>(in T other) where T : IGameplayTagContainer
      {
         m_ParentContainer?.AddTags(other);
         m_UnderlyingContainer.AddTags(other);
      }

      public void Clear()
      {
         foreach (GameplayTag tag in GetExplicitTags())
         {
            m_ParentContainer?.RemoveTag(tag);
         }

         m_UnderlyingContainer.Clear();
      }

      public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags)
      {
         m_UnderlyingContainer.GetChildTags(tag, childTags);
      }

      public IEnumerator<GameplayTag> GetEnumerator()
      {
         return m_UnderlyingContainer.GetEnumerator();
      }

      public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags)
      {
         m_UnderlyingContainer.GetExplicitChildTags(tag, childTags);
      }

      public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags)
      {
         m_UnderlyingContainer.GetExplicitParentTags(tag, parentTags);
      }

      public int GetExplicitTagCount(GameplayTag tag)
      {
         return m_UnderlyingContainer.GetExplicitTagCount(tag);
      }

      public GameplayTagEnumerator GetExplicitTags()
      {
         return m_UnderlyingContainer.GetExplicitTags();
      }

      public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags)
      {
         m_UnderlyingContainer.GetParentTags(tag, parentTags);
      }

      public int GetTagCount(GameplayTag tag)
      {
         return m_UnderlyingContainer.GetTagCount(tag);
      }

      public GameplayTagEnumerator GetTags()
      {
         return m_UnderlyingContainer.GetTags();
      }

      public void RegisterTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback)
      {
         m_UnderlyingContainer.RegisterTagEventCallback(tag, eventType, callback);
      }

      public void RemoveAllTagEventCallbacks()
      {
         m_UnderlyingContainer.RemoveAllTagEventCallbacks();
      }

      public void RemoveTag(GameplayTag gameplayTag)
      {
         m_ParentContainer?.RemoveTag(gameplayTag);
         m_UnderlyingContainer.RemoveTag(gameplayTag);
      }

      public void RemoveTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback)
      {
         m_UnderlyingContainer.RemoveTagEventCallback(tag, eventType, callback);
      }

      public void RemoveTags<T>(in T other) where T : IGameplayTagContainer
      {
         foreach (GameplayTag tag in GetExplicitTags())
         {
            m_ParentContainer?.RemoveTag(tag);
         }

         m_UnderlyingContainer.RemoveTags(other);
      }

      IEnumerator IEnumerable.GetEnumerator()
      {
         return GetEnumerator();
      }
   }
}