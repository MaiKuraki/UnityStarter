using System;

namespace CycloneGames.GameplayTags.Core
{
   [Flags]
   public enum GameplayTagFlags
   {
      None = 0,
      HideInEditor = 1 << 0,
   }
}