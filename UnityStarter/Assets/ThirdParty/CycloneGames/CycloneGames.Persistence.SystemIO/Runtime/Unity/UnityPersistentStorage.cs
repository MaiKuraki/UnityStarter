using CycloneGames.Persistence.SystemIO;
using UnityEngine;

namespace CycloneGames.Persistence.Unity
{
    /// <summary>
    /// Resolves a portable relative entry below <see cref="Application.persistentDataPath"/>.
    /// Call <see cref="Create"/> on the Unity main thread; the returned storage does not use Unity APIs.
    /// Unsupported platforms must inject another <see cref="IPersistenceStorage"/> adapter.
    /// </summary>
    public static class UnityPersistentStorage
    {
        public static IPersistenceStorage Create(string relativePath)
        {
            return SystemFilePersistenceStorage.CreateSandboxed(
                Application.persistentDataPath,
                relativePath);
        }
    }
}
