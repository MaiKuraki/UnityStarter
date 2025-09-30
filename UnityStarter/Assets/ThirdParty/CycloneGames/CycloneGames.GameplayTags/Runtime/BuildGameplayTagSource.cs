#if UNITY_ANDROID || UNITY_WEBGL
using UnityEngine.Networking;
#endif
using System;
using System.IO;
using UnityEngine;

namespace CycloneGames.GameplayTags.Runtime
{
   /// <summary>
   /// Loads gameplay tags from a binary file in the StreamingAssets folder.
   /// This source is intended for use in builds where tags are pre-compiled for performance.
   /// </summary>
   internal class BuildGameplayTagSource : IGameplayTagSource
   {
      public string Name => "Build";
      private const string TAG_FILE_NAME = "GameplayTags";
      private const int WEB_REQUEST_TIMEOUT_SECONDS = 5;

      public void RegisterTags(GameplayTagRegistrationContext context)
      {
         try
         {
            string path = Path.Combine(Application.streamingAssetsPath, TAG_FILE_NAME);
            byte[] data = LoadData(path);

            if (data == null || data.Length == 0)
            {
               return;
            }

            using MemoryStream memoryStream = new(data);
            using BinaryReader reader = new(memoryStream);

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
               string tagName = reader.ReadString();
               context.RegisterTag(tagName, string.Empty, GameplayTagFlags.None, this);
            }
         }
         catch (Exception e)
         {
            Debug.LogError($"[BuildGameplayTagSource] Failed to load gameplay tags from StreamingAssets. Exception: {e}");
         }
      }

      private byte[] LoadData(string dataPath)
      {
#if UNITY_ANDROID || UNITY_WEBGL
         // On Android and WebGL, StreamingAssets are not directly accessible via System.IO.
         // We must use UnityWebRequest to retrieve the data.
         return LoadDataWithUnityWebRequest(dataPath);
#else
         // On most other platforms (PC, Mac, Linux, iOS), we can use direct file access for better performance.
         return LoadDataFromFile(dataPath);
#endif
      }

#if UNITY_ANDROID || UNITY_WEBGL
        private byte[] LoadDataWithUnityWebRequest(string dataPath)
        {
            using UnityWebRequest request = UnityWebRequest.Get(dataPath);
            request.timeout = WEB_REQUEST_TIMEOUT_SECONDS;
            
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone) { }

#if UNITY_2020_2_OR_NEWER
            if (request.result != UnityWebRequest.Result.Success)
#else
            if (request.isNetworkError || request.isHttpError)
#endif
            {
                // A 404 is not a critical error; it just means no build-time tags were generated.
                // Other errors might indicate a problem with the build or device.
                if (request.responseCode != 404)
                {
                    Debug.LogError($"[BuildGameplayTagSource] Failed to load gameplay tags from '{dataPath}'. Error: {request.error}");
                }
                return null;
            }

            return request.downloadHandler.data;
        }
#endif

      private byte[] LoadDataFromFile(string path)
      {
         // Direct file access is faster and has less overhead than UnityWebRequest on supported platforms.
         if (!File.Exists(path))
         {
            return null;
         }

         try
         {
            return File.ReadAllBytes(path);
         }
         catch (Exception e)
         {
            Debug.LogError($"[BuildGameplayTagSource] Failed to read gameplay tags file at '{path}'. Exception: {e}");
            return null;
         }
      }
   }
}
