using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("CycloneGames.GameplayTags.Editor")]

namespace CycloneGames.GameplayTags.Runtime
{
   internal class FileGameplayTagSource : IGameplayTagSource, IDeleteTagHandler
   {
      private struct TagInFile
      {
         public string Name;
         public string Comment;
      }

      public static readonly string DirectoryPath = Path.GetFullPath(
         Path.Combine(Application.dataPath, "..", "ProjectSettings", "GameplayTags"));

      public string Name { get; private set; }
      public string FilePath { get; private set; }
      public bool IsReadOnly => false;

      private JObject m_Root;

      public FileGameplayTagSource(string filePath)
      {
         FilePath = filePath;
         Name = Path.GetFileName(filePath);
      }

      public bool TryLoad()
      {
         try
         {
            if (!File.Exists(FilePath))
            {
               m_Root = new JObject();
               return true;
            }

            m_Root = LoadRoot();
            return true;
         }
         catch (Exception ex)
         {
            Debug.LogError($"Failed to load tags from file '{Name}': {ex.Message}");
            return false;
         }
      }

      public static IEnumerable<FileGameplayTagSource> GetAllFileSources()
      {
         if (!Directory.Exists(DirectoryPath))
            yield break;

         foreach (string filePath in Directory.EnumerateFiles(DirectoryPath, "*.json"))
         {
            FileGameplayTagSource source = new(filePath);

            if (source.TryLoad())
               yield return source;
         }
      }

      public void RegisterTags(GameplayTagRegistrationContext context)
      {
         try
         {
            foreach (TagInFile tag in GetAllTags())
               context.RegisterTag(tag.Name, tag.Comment, GameplayTagFlags.None, this);
         }
         catch (Exception ex)
         {
            Debug.LogError($"Failed to fetch tags from file '{FilePath}': {ex.Message}");
         }
      }

      private IEnumerable<TagInFile> GetAllTags()
      {
         foreach (JProperty property in m_Root.Properties())
         {
            JToken commentToken = property.Value["Comment"];
            string comment = commentToken?.ToString();

            yield return new TagInFile { Name = property.Name, Comment = comment };
         }
      }

      public void AddTag(string tagName, string comment)
      {
         bool isAlreadyRegistered = GetAllTags().Any(t => t.Name == tagName);
         if (isAlreadyRegistered)
            throw new InvalidOperationException($"Tag '{tagName}' is already registered in file '{FilePath}'.");

         JObject newTagObject = new();

         if (!string.IsNullOrEmpty(comment))
            newTagObject["Comment"] = comment;

         m_Root.Add(tagName, newTagObject);

         SaveFile();
      }

      private JObject LoadRoot()
      {
         string fileContent = File.ReadAllText(FilePath);
         return JObject.Parse(fileContent);
      }

      private void SaveFile()
      {
         string fileContent = m_Root.ToString();
         File.WriteAllText(FilePath, fileContent);
      }

      public void DeleteTag(string tagName)
      {
         m_Root.Remove(tagName);
         SaveFile();
      }
   }
}
