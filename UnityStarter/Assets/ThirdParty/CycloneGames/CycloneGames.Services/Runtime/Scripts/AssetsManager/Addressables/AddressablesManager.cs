using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Concurrent;
using System.Threading;
using CycloneGames.Logger;

namespace CycloneGames.Service
{
    public class AddressablesManager : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[AddressablesManager]";

        // ConcurrentDictionary to ensure thread safety when accessing activeHandles.
        private readonly ConcurrentDictionary<string, AsyncOperationHandle> activeHandles =
            new ConcurrentDictionary<string, AsyncOperationHandle>();

        // Loads an asset asynchronously and returns a UniTask.
        public UniTask<TResultObject> LoadAssetAsync<TResultObject>(string key,
            CancellationToken cancellationToken = default) where TResultObject : UnityEngine.Object
        {
            var completionSource = new UniTaskCompletionSource<TResultObject>();
            var operationHandle = Addressables.LoadAssetAsync<TResultObject>(key);

            operationHandle.Completed += operation =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        HandleCancellation(key, operationHandle);
                        completionSource.TrySetCanceled();
                        return;
                    }

                    if (operation.Status == AsyncOperationStatus.Succeeded)
                    {
                        activeHandles[key] = operationHandle;
                        completionSource.TrySetResult(operation.Result);
                    }
                    else
                    {
                        HandleError(key, operation);
                        completionSource.TrySetException(new Exception($"Failed to load asset: {key}"));
                    }
                }
                catch (Exception ex)
                {
                    MLogger.LogError($"{DEBUG_FLAG} Exception occurred during asset load: {ex.Message}");
                    completionSource.TrySetException(ex);
                }
            };

            RegisterForCancellation(key, operationHandle, cancellationToken);

            return completionSource.Task;
        }

        // Method to release a handle by key
        public void ReleaseAsset(string key)
        {
            if (activeHandles.TryRemove(key, out AsyncOperationHandle handle))
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                else
                {
                    MLogger.LogWarning($"{DEBUG_FLAG} Attempted to release an invalid handle for key: {key}");
                }
            }
            else
            {
                MLogger.LogWarning($"{DEBUG_FLAG} No active handle found for key: {key}");
            }
        }

        // Cancels the asset load operation if the CancellationToken is invoked.
        private void RegisterForCancellation(string key, AsyncOperationHandle handle,
            CancellationToken cancellationToken)
        {
            // Register a callback with the cancellation token that will release the handle if the token is cancelled.
            var registration = cancellationToken.Register(() =>
            {
                HandleCancellation(key, handle);
            });

            // To prevent the callback from remaining registered after the task is complete, we unregister it upon completion.
            handle.Completed += _ => registration.Dispose();
        }

        // Helper method to handle the cancellation and release the handle
        private void HandleCancellation(string key, AsyncOperationHandle handle)
        {
            if (handle.IsValid() && !handle.IsDone)
            {
                Addressables.Release(handle);
                activeHandles.TryRemove(key, out _);
            }
        }

        // Helper method to handle errors during loading
        private void HandleError(string key, AsyncOperationHandle operation)
        {
            var errorMessage = $"Error loading asset with key {key}. Status: {operation.Status}";
            if (operation.OperationException != null)
            {
                errorMessage += $", Exception: {operation.OperationException.Message}";
            }

            MLogger.LogError($"{DEBUG_FLAG} {errorMessage}");
        }
    }
}
