using System;

namespace CycloneGames.GameplayTags.Runtime
{
   /// <summary>
   /// An attribute to define gameplay tags at the assembly level.
   /// </summary>
   /// <example>
   /// The <see cref="GameplayTagAttribute"/> can be used to annotate an assembly with gameplay tags.
   /// This is useful for categorizing and identifying various conditions and states in the game.
   /// 
   /// Example usage:
   /// <code>
   /// [assembly: GameplayTag("Character.Invincible", "Indicates that a character is invincible.")]
   /// [assembly: GameplayTag("Weapon.Reloading", "Indicates that a weapon is in the process of reloading.")]
   /// [assembly: GameplayTag("Interaction.Locked", "Indicates that an interaction is currently locked.")]
   /// </code>
   /// </example>
   [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
   public class GameplayTagAttribute : Attribute
   {
      /// <summary>
      /// Gets or sets the name of the gameplay tag.
      /// This is used to uniquely identify a tag within the game.
      /// Example values could be "Player.Health.Low", "Enemy.Alerted", "Item.Collected".
      /// </summary>
      public string TagName { get; set; }

      /// <summary>
      /// Gets or sets the description of the gameplay tag.
      /// This description is displayed in the editor to provide more context about the tag.
      /// </summary>
      public string Description { get; set; }

      /// <summary>
      /// Gets or sets the flags associated with the gameplay tag.
      /// </summary>
      public GameplayTagFlags Flags { get; set; }

      /// <summary>
      /// Initializes a new instance of the <see cref="GameplayTagAttribute"/> class.
      /// </summary>
      /// <param name="tagName">The name of the gameplay tag.</param>
      /// <param name="description">The description of the gameplay tag (optional).</param>
      /// <param name="flags">The flags associated with the gameplay tag (optional).</param>
      public GameplayTagAttribute(string tagName, string description = null, GameplayTagFlags flags = GameplayTagFlags.None)
      {
         TagName = tagName;
         Description = description;
         Flags = flags;
      }
   }
}