using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using UnityEngine.Pool;

namespace CycloneGames.GameplayTags
{
   public delegate void OnTagCountChangedDelegate(GameplayTag gameplayTag, int newCount);

   public enum GameplayTagEventType
   {
      NewOrRemoved,
      AnyCountChange
   }

   internal struct DeferredTagChangedDelegate
   {
      public GameplayTag GameplayTag;
      public int NewCount;
      public OnTagCountChangedDelegate Delegate;

      public DeferredTagChangedDelegate(GameplayTag gameplayTag, int newCount, OnTagCountChangedDelegate @delegate)
      {
         GameplayTag = gameplayTag;
         NewCount = newCount;
         Delegate = @delegate;
      }

      public readonly void Execute()
      {
         Delegate(GameplayTag, NewCount);
      }
   }

   internal struct GameplayTagDelegateInfo
   {
      public OnTagCountChangedDelegate OnAnyChange;
      public OnTagCountChangedDelegate OnNewOrRemove;
   }

   public interface IGameplayTagCountContainer : IGameplayTagContainer
   {
      /// <summary>
      /// Event that is called when the count of any tag changes.
      /// </summary>
      event OnTagCountChangedDelegate OnAnyTagCountChange;

      /// <summary>
      /// Eve that is called when any tag is added or removed.
      /// </summary>
      event OnTagCountChangedDelegate OnAnyTagNewOrRemove;

      /// <summary>
      /// Gets the count of a specific tag (the number of times it has been explicitly added).
      /// </summary>
      int GetExplicitTagCount(GameplayTag tag);

      /// <summary>
      /// Gets the count of a specific tag.
      /// </summary>
      int GetTagCount(GameplayTag tag);

      /// <summary>
      /// Registers a callback for a tag event.
      /// </summary>
      /// <param name="callback">The callback to register.</param>
      /// <param name="tag">The gameplay tag.</param>
      /// <param name="eventType">The type of event.</param>
      void RegisterTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback);

      /// <summary>
      /// Removes a callback for a tag event.
      /// </summary>
      /// <param name="callback">The callback to remove.</param>
      /// <param name="tag">The gameplay tag.</param>
      /// <param name="eventType">The type of event.</param>
      void RemoveTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback);

      /// <summary>
      /// Removes all callbacks for tag events.
      /// </summary>
      void RemoveAllTagEventCallbacks();
   }

   [DebuggerDisplay("{DebuggerDisplay,nq}")]
   [DebuggerTypeProxy(typeof(GameplayTagContainerDebugView))]
   public class GameplayTagCountContainer : IGameplayTagCountContainer
   {
      /// <inheritdoc />
      public bool IsEmpty => m_Indices.Explicit.Count == 0;

      /// <inheritdoc />
      public int ExplicitTagCount => m_Indices.Explicit.Count;

      /// <inheritdoc />
      public int TagCount => m_Indices.Implicit.Count;

      /// <inheritdoc />
      public GameplayTagContainerIndices Indices => m_Indices;

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "It's used for debugging")]
      private string DebuggerDisplay => $"Count (Explicit, Total) = ({ExplicitTagCount}, {TagCount})";

      public event OnTagCountChangedDelegate OnAnyTagNewOrRemove;
      public event OnTagCountChangedDelegate OnAnyTagCountChange;

      private Dictionary<GameplayTag, GameplayTagDelegateInfo> m_TagDelegateInfoMap = new();
      private Dictionary<GameplayTag, int> m_TagCountMap = new();
      private Dictionary<GameplayTag, int> m_ExplicitTagCountMap = new();
      private GameplayTagContainerIndices m_Indices = GameplayTagContainerIndices.Create();

      /// <inheritdoc />
      public GameplayTagEnumerator GetExplicitTags()
      {
         return new GameplayTagEnumerator(m_Indices.Explicit);
      }

      /// <inheritdoc />
      public GameplayTagEnumerator GetTags()
      {
         return new GameplayTagEnumerator(m_Indices.Implicit);
      }

      /// <inheritdoc />
      public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags)
      {
         GameplayTagContainerUtility.GetParentTags(m_Indices.Implicit, tag, parentTags);
      }

      /// <inheritdoc />
      public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags)
      {
         GameplayTagContainerUtility.GetChildTags(m_Indices.Implicit, tag, childTags);
      }

      /// <inheritdoc />
      public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags)
      {
         GameplayTagContainerUtility.GetParentTags(m_Indices.Explicit, tag, parentTags);
      }

      /// <inheritdoc />
      public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags)
      {
         GameplayTagContainerUtility.GetChildTags(m_Indices.Explicit, tag, childTags);
      }

      /// <inheritdoc />
      public int GetTagCount(GameplayTag tag)
      {
         m_TagCountMap.TryGetValue(tag, out int count);
         return count;
      }

      /// <inheritdoc />
      public int GetExplicitTagCount(GameplayTag tag)
      {
         m_ExplicitTagCountMap.TryGetValue(tag, out int count);
         return count;
      }

      /// <inheritdoc />
      public void RegisterTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback)
      {
         m_TagDelegateInfoMap.TryGetValue(tag, out GameplayTagDelegateInfo delegateInfo);
         GetEventDelegate(ref delegateInfo, eventType) += callback;
         m_TagDelegateInfoMap[tag] = delegateInfo;
      }

      /// <inheritdoc />
      public void RemoveTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback)
      {
         if (m_TagDelegateInfoMap.TryGetValue(tag, out GameplayTagDelegateInfo delegateInfo))
         {
            GetEventDelegate(ref delegateInfo, eventType) -= callback;
            m_TagDelegateInfoMap[tag] = delegateInfo;
         }
      }

      /// <inheritdoc />
      public void RemoveAllTagEventCallbacks()
      {
         m_TagDelegateInfoMap.Clear();
      }

      private static ref OnTagCountChangedDelegate GetEventDelegate(ref GameplayTagDelegateInfo delegateInfo, GameplayTagEventType eventType)
      {
         switch (eventType)
         {
            case GameplayTagEventType.AnyCountChange:
               return ref delegateInfo.OnAnyChange;

            case GameplayTagEventType.NewOrRemoved:
               return ref delegateInfo.OnNewOrRemove;
         }

         throw new ArgumentException(nameof(eventType));
      }

      /// <inheritdoc />
      public void AddTag(GameplayTag tag)
      {
         using (ListPool<DeferredTagChangedDelegate>.Get(out List<DeferredTagChangedDelegate> delegates))
         {
            AddTagInternal(tag, delegates);

            foreach (DeferredTagChangedDelegate del in delegates)
            {
               del.Execute();
            }
         }
      }

      /// <inheritdoc />
      public void AddTags<T>(in T other) where T : IGameplayTagContainer
      {
         using (ListPool<DeferredTagChangedDelegate>.Get(out List<DeferredTagChangedDelegate> delegates))
         {
            foreach (GameplayTag gameplayTag in other.GetExplicitTags())
            {
               AddTagInternal(gameplayTag, delegates);
            }

            foreach (DeferredTagChangedDelegate del in delegates)
            {
               del.Execute();
            }
         }
      }

      private void AddTagInternal(GameplayTag tag, List<DeferredTagChangedDelegate> tagChangeDelegates)
      {
         m_ExplicitTagCountMap.TryGetValue(tag, out int previousExplictTagCount);
         m_ExplicitTagCountMap[tag] = previousExplictTagCount + 1;

         if (previousExplictTagCount == 0)
         {
            int index = ~BinarySearchUtility.Search(m_Indices.Explicit, tag.RuntimeIndex);
            m_Indices.Explicit.Insert(index, tag.RuntimeIndex);
         }

         foreach (GameplayTag tagInHeirarchy in tag.HierarchyTags)
         {
            m_TagDelegateInfoMap.TryGetValue(tagInHeirarchy, out GameplayTagDelegateInfo delegateInfo);
            m_TagCountMap.TryGetValue(tagInHeirarchy, out int previousTagCount);
            m_TagCountMap[tagInHeirarchy] = previousTagCount + 1;

            if (previousTagCount == 0)
            {
               int index = ~BinarySearchUtility.Search(m_Indices.Implicit, tagInHeirarchy.RuntimeIndex);
               m_Indices.Implicit.Insert(index, tagInHeirarchy.RuntimeIndex);

               if (delegateInfo.OnNewOrRemove != null)
               {
                  tagChangeDelegates.Add(new(tagInHeirarchy, 1, delegateInfo.OnNewOrRemove));
               }

               if (OnAnyTagNewOrRemove != null)
               {
                  tagChangeDelegates.Add(new(tagInHeirarchy, 1, OnAnyTagNewOrRemove));
               }
            }

            if (delegateInfo.OnAnyChange != null)
            {
               tagChangeDelegates.Add(new(tagInHeirarchy, previousTagCount + 1, delegateInfo.OnAnyChange));
            }

            if (OnAnyTagCountChange != null)
            {
               tagChangeDelegates.Add(new(tagInHeirarchy, previousTagCount + 1, OnAnyTagCountChange));
            }
         }
      }

      /// <inheritdoc />
      public void RemoveTag(GameplayTag tag)
      {
         using (ListPool<DeferredTagChangedDelegate>.Get(out List<DeferredTagChangedDelegate> tagChangeDelegates))
         {
            RemoveTagInternal(tag, tagChangeDelegates);

            for (int i = 0; i < tagChangeDelegates.Count; i++)
            {
               tagChangeDelegates[i].Execute();
            }
         }
      }

      /// <inheritdoc />
      public void RemoveTags<T>(in T other) where T : IGameplayTagContainer
      {
         using (ListPool<DeferredTagChangedDelegate>.Get(out List<DeferredTagChangedDelegate> tagChangeDelegates))
         {
            foreach (GameplayTag gameplayTag in other.GetExplicitTags())
            {
               RemoveTagInternal(gameplayTag, tagChangeDelegates);
            }

            for (int i = 0; i < tagChangeDelegates.Count; i++)
            {
               tagChangeDelegates[i].Execute();
            }
         }
      }

      private void RemoveTagInternal(GameplayTag tag, List<DeferredTagChangedDelegate> tagChangeDelegates)
      {
         if (!m_ExplicitTagCountMap.TryGetValue(tag, out int explictTagCount))
         {
            GameplayTagUtility.WarnNotExplictlyAddedTagRemoval(tag);
            return;
         }

         if (explictTagCount == 1)
         {
            int index = BinarySearchUtility.Search(m_Indices.Explicit, tag.RuntimeIndex);
            m_Indices.Explicit.RemoveAt(index);
            m_ExplicitTagCountMap.Remove(tag);
         }
         else
         {
            m_ExplicitTagCountMap[tag] = explictTagCount - 1;
         }

         foreach (GameplayTag tagInHierarchy in tag.HierarchyTags)
         {
            m_TagDelegateInfoMap.TryGetValue(tagInHierarchy, out GameplayTagDelegateInfo delegateInfo);
            if (!m_TagCountMap.TryGetValue(tagInHierarchy, out int tagCount))
            {
               break;
            }

            if (tagCount == 1)
            {
               int index = BinarySearchUtility.Search(m_Indices.Implicit, tagInHierarchy.RuntimeIndex);
               m_Indices.Implicit.RemoveAt(index);
               m_TagCountMap.Remove(tagInHierarchy);

               if (delegateInfo.OnNewOrRemove != null)
               {
                  tagChangeDelegates.Add(new DeferredTagChangedDelegate(tagInHierarchy, 0, delegateInfo.OnNewOrRemove));
               }

               if (OnAnyTagNewOrRemove != null)
               {
                  tagChangeDelegates.Add(new DeferredTagChangedDelegate(tagInHierarchy, 0, OnAnyTagNewOrRemove));
               }

               if (delegateInfo.OnAnyChange != null)
               {
                  tagChangeDelegates.Add(new DeferredTagChangedDelegate(tagInHierarchy, 0, delegateInfo.OnAnyChange));
               }

               if (OnAnyTagCountChange != null)
               {
                  tagChangeDelegates.Add(new DeferredTagChangedDelegate(tagInHierarchy, 0, OnAnyTagNewOrRemove));
               }

               continue;
            }

            m_TagCountMap[tagInHierarchy] = tagCount - 1;

            if (delegateInfo.OnAnyChange != null)
            {
               tagChangeDelegates.Add(new DeferredTagChangedDelegate(tagInHierarchy, tagCount - 1, delegateInfo.OnAnyChange));
            }

            if (OnAnyTagCountChange != null)
            {
               tagChangeDelegates.Add(new DeferredTagChangedDelegate(tagInHierarchy, tagCount - 1, OnAnyTagNewOrRemove));
            }
         }
      }

      /// <inheritdoc />
      public void Clear()
      {
         using (ListPool<DeferredTagChangedDelegate>.Get(out List<DeferredTagChangedDelegate> tagChangeDelegates))
         {
            foreach (GameplayTag tag in GetTags())
            {
               m_TagDelegateInfoMap.TryGetValue(tag, out GameplayTagDelegateInfo delegateInfo);

               if (delegateInfo.OnNewOrRemove != null)
               {
                  tagChangeDelegates.Add(new DeferredTagChangedDelegate(tag, 0, delegateInfo.OnNewOrRemove));
               }

               if (OnAnyTagNewOrRemove != null)
               {
                  tagChangeDelegates.Add(new DeferredTagChangedDelegate(tag, 0, OnAnyTagNewOrRemove));
               }
            }

            m_ExplicitTagCountMap.Clear();
            m_TagCountMap.Clear();
            m_Indices.Clear();

            foreach (DeferredTagChangedDelegate del in tagChangeDelegates)
            {
               del.Execute();
            }
         }
      }

      public GameplayTagEnumerator GetEnumerator()
      {
         return new GameplayTagEnumerator(m_Indices.Implicit);
      }

      IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator()
      {
         return GetEnumerator();
      }

      IEnumerator IEnumerable.GetEnumerator()
      {
         return GetEnumerator();
      }
   }
}