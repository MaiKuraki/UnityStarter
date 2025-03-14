using System;
using System.Threading;
using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.Service
{
    public class AddressablesService : IAssetLoader, IDisposable
    {
        private const string DEBUG_FLAG = "[AddressablesService]";
        private AddressablesManager addressablesManager;

        public AddressablesService()
        {
            Initialize();
        }

        public void Initialize()
        {
            // Check if an instance of AddressablesManager already exists to prevent multiple instances.
            addressablesManager = GameObject.FindFirstObjectByType<AddressablesManager>();
            if (addressablesManager == null)
            {
                GameObject addressablesManagerGO = new GameObject("AddressablesManager");
                addressablesManager = addressablesManagerGO.AddComponent<AddressablesManager>();
                UnityEngine.MonoBehaviour.DontDestroyOnLoad(addressablesManagerGO);
            }
            else
            {
                CLogger.LogWarning($"{DEBUG_FLAG} AddressablesManager is already initialized.");
            }
        }

        public void Dispose()
        {

        }

        public UniTask<TResultObject> LoadAssetAsync<TResultObject>(string key,
            CancellationToken cancellationToken = default) where TResultObject : UnityEngine.Object
        {
            if (addressablesManager == null)
            {
                throw new System.InvalidOperationException($"{DEBUG_FLAG} AddressablesManager is not initialized.");
            }

            return addressablesManager.LoadAssetAsync<TResultObject>(key, cancellationToken);
        }

        public void ReleaseAssetHandle(string key)
        {
            if (addressablesManager == null)
            {
                throw new System.InvalidOperationException($"{DEBUG_FLAG} AddressablesManager is not initialized.");
            }

            addressablesManager.ReleaseAsset(key);
        }

        public bool IsServiceReady()
        {
            return addressablesManager != null;
        }
    }
}