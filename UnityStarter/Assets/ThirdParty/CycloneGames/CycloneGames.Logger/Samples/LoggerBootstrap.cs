using CycloneGames.Logger;
using UnityEngine;

/// <summary>
/// Centralized, project-wide logger configuration. Runs once before any scene loads.
/// Note: CycloneGames.Logger now ships with a default bootstrap in Runtime that reads
/// Resources/CycloneGames.Logger/LoggerSettings. You can delete this sample file if you
/// prefer the built-in bootstrap, or keep it to demonstrate per-project customization.
/// </summary>
public static class LoggerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        // Select processing strategy based on platform capabilities.
        // Web/WASM (e.g., WebGL) does not support background threads.
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            CLogger.ConfigureSingleThreadedProcessing();
        }
        else
        {
            CLogger.ConfigureThreadedProcessing();
        }

        // Intentionally left empty to avoid side-effects.
        // Projects may add loggers and defaults here if they choose to use this sample bootstrap.
    }
}


