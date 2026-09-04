#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using CycloneGames.GameplayTags.Core;
using CycloneGames.Logging;
using UnityEngine;
using UnityEngine.Networking;

namespace CycloneGames.GameplayTags.Unity.Runtime
{
    /// <summary>
    /// The WebGL host platform: the baked manifest arrives asynchronously and the registry builds after
    /// it does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WebGL has no synchronous file IO. A <c>Resources.Load</c> works there, but it pulls the manifest
    /// into the data file and blocks startup on it, which is exactly the cost a WebGL build is trying to
    /// shave. The standard pattern is a <see cref="UnityWebRequest"/> against
    /// <c>StreamingAssets</c>, and that is asynchronous by nature.
    /// </para>
    /// <para>
    /// Core needs no changes to support this: the registry builds lazily on the first tag resolution, so
    /// it can build from catalogs alone and then rebuild once the manifest lands. The rebuild advances the
    /// runtime-index epoch, which invalidates index caches - so the contract a WebGL game must respect is
    /// <b>do not resolve a tag until <see cref="IsLoaded"/> is true</b>. Gate gameplay initialisation on
    /// <see cref="ManifestLoaded"/>, exactly as you would gate it on any other async startup step.
    /// </para>
    /// <para>
    /// Use <see cref="GameplayTagWebGLManifestLoader"/> to drive the fetch, or call
    /// <see cref="SetBuildData"/> from your own loader.
    /// </para>
    /// </remarks>
    public sealed class GameplayTagWebGLPlatform : GameplayTagHostPlatformBase
    {
        private byte[] m_BuildData;
        private bool m_HasBuildData;
        private bool m_IsLoaded;

        /// <summary>Raised once the manifest has been supplied and the registry is safe to read.</summary>
        public event Action ManifestLoaded;

        public override string Name => "Unity.WebGL";

        /// <summary>True once the manifest has arrived. Gate tag resolution on this.</summary>
        public bool IsLoaded => m_IsLoaded;

        public override bool TryLoadBuildTagData(out byte[] data)
        {
            data = m_HasBuildData ? m_BuildData : null;
            return m_HasBuildData;
        }

        /// <summary>
        /// Supplies the manifest and raises <see cref="ManifestLoaded"/>. Call this from your loader, then
        /// reload the registry so the manifest's tags are included.
        /// </summary>
        public void SetBuildData(byte[] data)
        {
            m_BuildData = data;
            m_HasBuildData = data != null && data.Length > 0;
            m_IsLoaded = true;
            ManifestLoaded?.Invoke();
        }
    }

    /// <summary>
    /// Fetches the baked manifest from <c>StreamingAssets</c> and hands it to a
    /// <see cref="GameplayTagWebGLPlatform"/>.
    /// </summary>
    /// <remarks>
    /// Attach to a bootstrap object, call <see cref="BeginLoad"/> from your startup coroutine, and yield
    /// on <see cref="LoadTask"/> before anything resolves a tag.
    /// </remarks>
    public sealed class GameplayTagWebGLManifestLoader : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("StreamingAssets path of the baked gameplay tag manifest.")]
        private string manifestPath = "GameplayTags/GameplayTags.bin";

        private GameplayTagWebGLPlatform m_Platform;
        private UnityWebRequestAsyncOperation m_LoadTask;

        /// <summary>The platform this loader feeds. Created on first access if you did not supply one.</summary>
        public GameplayTagWebGLPlatform Platform
        {
            get
            {
                if (m_Platform == null)
                {
                    m_Platform = new GameplayTagWebGLPlatform();
                    GameplayTagHost.Use(m_Platform);
                }

                return m_Platform;
            }
        }

        /// <summary>The in-flight request, for coroutines that want to yield on it directly.</summary>
        public UnityWebRequestAsyncOperation LoadTask => m_LoadTask;

        /// <summary>Starts the fetch. Yield on <see cref="LoadTask"/> or poll <see cref="Platform"/>.</summary>
        public void BeginLoad()
        {
            string url = System.IO.Path.Combine(Application.streamingAssetsPath, manifestPath);
            UnityWebRequest request = UnityWebRequest.Get(url);
            m_LoadTask = request.SendWebRequest();
        }

        private void Update()
        {
            if (m_LoadTask != null && m_LoadTask.isDone)
                CompleteManifestLoad();
        }

        /// <summary>
        /// Completion handling lives outside <c>Update</c> so the hot-path analyzer does not have to reason
        /// about it: this runs once per fetch, on its last frame, and does the failure logging and the
        /// registry reload.
        /// </summary>
        private void CompleteManifestLoad()
        {
            UnityWebRequest request = m_LoadTask.webRequest;
            m_LoadTask = null;

#if UNITY_2020_1_OR_NEWER
            if (request.result != UnityWebRequest.Result.Success)
#else
            if (request.isNetworkError || request.isHttpError)
#endif
            {
                // The TState overload defers formatting: the message is only built if the Warning level is
                // actually emitted, and neither the request nor the lambda captures allocate here.
                GameplayTagsRuntimeLog.Channel.Warning(
                    request,
                    static (failedRequest, builder) =>
                    {
                        builder.Append("Gameplay tag manifest fetch failed (");
                        builder.Append(failedRequest.error);
                        builder.Append("). The registry will build from catalogs only; gameplay tags baked ");
                        builder.Append("into the manifest are unavailable.");
                    });
                Platform.SetBuildData(null);
                return;
            }

            Platform.SetBuildData(request.downloadHandler.data);
            GameplayTagManager.Reload(preserveRuntimeIndices: false);
        }
    }
}
#endif
