using System;

namespace CycloneGames.GameplayTags.Core
{
   public class GameplayTagContainerPool
   {
      public readonly struct PooledContainer : IDisposable
      {
         public GameplayTagContainer Container => m_Container;

         private readonly GameplayTagContainer m_Container;

         public PooledContainer(GameplayTagContainer container)
         {
            m_Container = container;
         }

         public readonly void Dispose()
         {
            Release(m_Container);
         }
      }

      private static readonly CustomObjectPool<GameplayTagContainer> s_Instance = new(onRelease: OnReleaseContainer);

      public static GameplayTagContainer Get()
      {
         return s_Instance.Get();
      }

      public static void Release(GameplayTagContainer container)
      {
         s_Instance.Release(container);
      }

      public static PooledContainer Get(out GameplayTagContainer container)
      {
         container = Get();
         return new(container);
      }

      private static void OnReleaseContainer(GameplayTagContainer container)
      {
         container.Clear();
      }
   }
}
