using System;

namespace CycloneGames.GameplayTags.Runtime
{
   internal class GameplayTagUtility
   {
      internal static void WarnNotExplictlyAddedTagRemoval(GameplayTag gameplayTag)
      {
         GameplayTagLogger.LogWarning($"Attempted to remove tag {gameplayTag} from tag count container, but it is not explicitly added to the container.");
      }

      internal static void WarnNotExplicitTagsRemoval(GameplayTagEnumerator tags)
      {
         foreach (GameplayTag tag in tags)
         {
            WarnNotExplictlyAddedTagRemoval(tag);
         }
      }

      /// <summary>
      /// Return the name of every tag in the hierarchy of the given tag. For
      /// example, if the tag is "A.B.C", the result will be ["A", "A.B",
      /// "A.B.C"]
      /// </summary>
      public static string[] GetHeirarchyNames(string tagName)
      {
         ValidateName(tagName);

         int level = GetHeirarchyLevelFromName(tagName);
         string[] names = new string[level];
         names[--level] = tagName;

         for (int i = tagName.Length - 1; i >= 0; i--)
         {
            if (tagName[i] == '.')
            {
               string name = tagName[..i];
               names[--level] = name;

               if (level == -1)
               {
                  break;
               }
            }
         }

         return names;
      }

      public static string GetParentName(string name)
      {
         ValidateName(name);
         
         int lastDot = name.LastIndexOf('.');
         if (lastDot == -1)
         {
            return null;
         }

         return name.Substring(0, lastDot);
      }

      public static int GetHeirarchyLevelFromName(string name)
      {
         ValidateName(name);

         int level = 1;
         for (int i = 0; i < name.Length; i++)
         {
            if (name[i] == '.')
            {
               level++;
            }
         }

         return level;
      }

      public static string GetLabel(string name)
      {
         ValidateName(name);

         int indexOfPoint = name.LastIndexOf('.');
         if (indexOfPoint == -1)
         {
            return name;
         }

         return name[(indexOfPoint + 1)..];
      }

      public static bool IsNameValid(string name, out string errorMessage)
      {
         static bool IsValidLabelCharacter(char c)
         {
            return char.IsLetterOrDigit(c) || c == '_';
         }

         static bool AcceptLabel(string name, ref int position)
         {
            if (position >= name.Length || !IsValidLabelCharacter(name[position]))
               return false;

            position++;
            while (position < name.Length && IsValidLabelCharacter(name[position]))
            {
               position++;
            }

            return true;
         }

         if (string.IsNullOrEmpty(name))
         {
            errorMessage = "Tag name cannot be null or empty.";
            return false;
         }

         int position = 0;
         if (AcceptLabel(name, ref position))
         {
            while (position < name.Length && name[position] == '.')
            {
               position++;
               if (!AcceptLabel(name, ref position))
               {
                  errorMessage = $"Invalid tag name '{name}'. Unexpected character at position {position}.";
                  return false;
               }
            }
         }

         if (position == name.Length)
         {
            errorMessage = null;
            return true;
         }

         errorMessage = $"Invalid tag name '{name}'. Unexpected character at position {position}.";
         return false;
      }

      public static void ValidateName(string name)
      {
         if (!IsNameValid(name, out string errorMessage))
            throw new ArgumentException(errorMessage);
      }
   }
}
