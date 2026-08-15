using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.GameplayFramework.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Discovers inactive and active Actors beneath the roots of one explicitly selected Scene.
    /// The source is reusable across replacement Worlds while the Scene remains loaded.
    /// </summary>
    public sealed class SceneWorldActorSource : IWorldActorSource
    {
        private readonly Scene scene;
        private readonly int ownerThreadId;
        private readonly int maximumVisitedGameObjectCount;
        private readonly List<GameObject> rootObjects = new List<GameObject>(16);
        private readonly List<Transform> traversalStack = new List<Transform>(64);
        private bool isCollecting;

        public SceneWorldActorSource(
            Scene scene,
            int maximumVisitedGameObjectCount = WorldRuntimeLimits.MaximumSupportedActorCount)
        {
            if (!scene.IsValid())
            {
                throw new ArgumentException("World Actor discovery requires a valid Scene.", nameof(scene));
            }

            if (maximumVisitedGameObjectCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumVisitedGameObjectCount),
                    maximumVisitedGameObjectCount,
                    "Scene Actor discovery traversal capacity must be positive.");
            }

            this.scene = scene;
            this.maximumVisitedGameObjectCount = maximumVisitedGameObjectCount;
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public Scene Scene => scene;
        public int MaximumVisitedGameObjectCount => maximumVisitedGameObjectCount;

        public void CollectActors(IWorldActorCollector collector)
        {
            if (collector == null)
            {
                throw new ArgumentNullException(nameof(collector));
            }

            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "Scene World Actor discovery must run on the thread that created the source.");
            }

            if (isCollecting)
            {
                throw new InvalidOperationException("Scene World Actor discovery does not support re-entry.");
            }

            if (!scene.isLoaded)
            {
                throw new InvalidOperationException(
                    $"Scene '{scene.name}' must be loaded before World Actor discovery.");
            }

            isCollecting = true;
            rootObjects.Clear();
            traversalStack.Clear();
            try
            {
                if (scene.rootCount > maximumVisitedGameObjectCount)
                {
                    throw CreateTraversalCapacityException();
                }

                scene.GetRootGameObjects(rootObjects);
                int visitedGameObjectCount = 0;
                for (int i = 0; i < rootObjects.Count; i++)
                {
                    GameObject root = rootObjects[i];
                    if (root == null)
                    {
                        continue;
                    }

                    traversalStack.Add(root.transform);
                    while (traversalStack.Count > 0)
                    {
                        int lastIndex = traversalStack.Count - 1;
                        Transform current = traversalStack[lastIndex];
                        traversalStack.RemoveAt(lastIndex);
                        if (current == null)
                        {
                            continue;
                        }

                        if (visitedGameObjectCount >= maximumVisitedGameObjectCount)
                        {
                            throw CreateTraversalCapacityException();
                        }

                        visitedGameObjectCount++;
                        if (current.TryGetComponent(out Actor actor) && !collector.TryAdd(actor))
                        {
                            return;
                        }

                        int childCount = current.childCount;
                        int remainingTraversalCapacity =
                            maximumVisitedGameObjectCount -
                            visitedGameObjectCount -
                            traversalStack.Count;
                        if (childCount > remainingTraversalCapacity)
                        {
                            throw CreateTraversalCapacityException();
                        }

                        for (int childIndex = childCount - 1; childIndex >= 0; childIndex--)
                        {
                            traversalStack.Add(current.GetChild(childIndex));
                        }
                    }
                }
            }
            finally
            {
                rootObjects.Clear();
                traversalStack.Clear();
                isCollecting = false;
            }
        }

        private InvalidOperationException CreateTraversalCapacityException()
        {
            return new InvalidOperationException(
                $"Scene Actor discovery exceeded its configured traversal limit of {maximumVisitedGameObjectCount} GameObjects.");
        }
    }
}
