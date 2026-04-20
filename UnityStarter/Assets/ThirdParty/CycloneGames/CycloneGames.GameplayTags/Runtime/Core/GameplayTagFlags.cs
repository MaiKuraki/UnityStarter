using System;

namespace CycloneGames.GameplayTags.Runtime
{
   [Flags]
   public enum GameplayTagFlags
   {
      None = 0,
      HideInEditor = 1 << 0,
   }
}