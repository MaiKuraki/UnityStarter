using System.Threading;
using CycloneGames.GameplayFramework.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class WorldSettingsTests
    {
        private WorldSettings settings;
        private GameObject gameModeObject;
        private GameObject playerControllerObject;
        private GameObject pawnObject;

        [TearDown]
        public void TearDown()
        {
            PathResolver.Instance.Reset();
            WorldSettingsReferenceResolverRegistry.Unregister(PathResolver.Instance);

            if (settings != null)
            {
                Object.DestroyImmediate(settings);
            }

            if (gameModeObject != null)
            {
                Object.DestroyImmediate(gameModeObject);
            }

            if (playerControllerObject != null)
            {
                Object.DestroyImmediate(playerControllerObject);
            }

            if (pawnObject != null)
            {
                Object.DestroyImmediate(pawnObject);
            }
        }

        [Test]
        public void Validate_ReturnsFalse_WhenRequiredDirectReferencesAreMissing()
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>();

            Assert.IsFalse(settings.Validate(logWarnings: false));
            Assert.IsFalse(settings.HasConfiguredGameMode);
            Assert.IsFalse(settings.HasConfiguredPlayerController);
            Assert.IsFalse(settings.HasConfiguredPawn);
            Assert.IsFalse(settings.UsesExternalReferences);
        }

        [Test]
        public void Validate_ReturnsTrue_WhenRequiredDirectReferencesAreAssigned()
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>();
            AssignRequiredDirectReferences(settings);

            Assert.IsTrue(settings.Validate(logWarnings: false));
            Assert.IsTrue(settings.HasConfiguredGameMode);
            Assert.IsTrue(settings.HasConfiguredPlayerController);
            Assert.IsTrue(settings.HasConfiguredPawn);
            Assert.AreSame(gameModeObject.GetComponent<GameMode>(), settings.GameModeClass);
            Assert.AreSame(playerControllerObject.GetComponent<PlayerController>(), settings.PlayerControllerClass);
            Assert.AreSame(pawnObject.GetComponent<Pawn>(), settings.PawnClass);
        }

        [Test]
        public void ResolveReferencesAsync_UsesRegisteredResolverForExternalReferences()
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>();
            AssignRequiredDirectReferences(settings);
            SetSource(settings, "pawnSource", WorldSettingsReferenceSource.PathLocation);
            SetString(settings, "pawnAssetLocation", "resolved/pawn");
            Pawn resolvedPawn = CreateComponent<Pawn>(ref pawnObject, "ResolvedPawn");
            PathResolver.Instance.Asset = resolvedPawn;
            WorldSettingsReferenceResolverRegistry.Register(PathResolver.Instance);

            bool resolved = settings.ResolveReferencesAsync(CancellationToken.None, logWarnings: false).GetAwaiter().GetResult();

            Assert.IsTrue(resolved);
            Assert.IsTrue(settings.UsesExternalReferences);
            Assert.AreSame(resolvedPawn, settings.PawnClass);
            Assert.AreEqual("resolved/pawn", PathResolver.Instance.LastLocation);
        }

        [Test]
        public void ClearResolvedReferences_RemovesExternalResolvedValue()
        {
            settings = ScriptableObject.CreateInstance<WorldSettings>();
            AssignRequiredDirectReferences(settings);
            SetSource(settings, "pawnSource", WorldSettingsReferenceSource.PathLocation);
            SetString(settings, "pawnAssetLocation", "resolved/pawn");
            Pawn resolvedPawn = CreateComponent<Pawn>(ref pawnObject, "ResolvedPawn");
            PathResolver.Instance.Asset = resolvedPawn;
            WorldSettingsReferenceResolverRegistry.Register(PathResolver.Instance);
            settings.ResolveReferencesAsync(CancellationToken.None, logWarnings: false).GetAwaiter().GetResult();

            settings.ClearResolvedReferences();

            Assert.IsNull(settings.PawnClass);
        }

        private void AssignRequiredDirectReferences(WorldSettings worldSettings)
        {
            SetObject(worldSettings, "gameModeClass", CreateComponent<GameMode>(ref gameModeObject, "GameMode"));
            SetObject(worldSettings, "playerControllerClass", CreateComponent<PlayerController>(ref playerControllerObject, "PlayerController"));
            SetObject(worldSettings, "pawnClass", CreateComponent<Pawn>(ref pawnObject, "Pawn"));
        }

        private static T CreateComponent<T>(ref GameObject gameObject, string name) where T : Component
        {
            if (gameObject != null)
            {
                Object.DestroyImmediate(gameObject);
            }

            gameObject = new GameObject(name);
            return gameObject.AddComponent<T>();
        }

        private static void SetObject(WorldSettings worldSettings, string propertyName, Object value)
        {
            SerializedObject serializedObject = new SerializedObject(worldSettings);
            serializedObject.FindProperty(propertyName).objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSource(WorldSettings worldSettings, string propertyName, WorldSettingsReferenceSource source)
        {
            SerializedObject serializedObject = new SerializedObject(worldSettings);
            serializedObject.FindProperty(propertyName).enumValueIndex = (int)source;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(WorldSettings worldSettings, string propertyName, string value)
        {
            SerializedObject serializedObject = new SerializedObject(worldSettings);
            serializedObject.FindProperty(propertyName).stringValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private sealed class PathResolver : IWorldSettingsReferenceResolver
        {
            public static readonly PathResolver Instance = new PathResolver();

            public Object Asset { get; set; }
            public string LastLocation { get; private set; }

            public void Reset()
            {
                Asset = null;
                LastLocation = null;
            }

            public bool Supports(WorldSettingsReferenceSource source)
            {
                return source == WorldSettingsReferenceSource.PathLocation;
            }

            public UniTask<WorldSettingsAssetLoadResult<T>> ResolveAsync<T>(string location, CancellationToken cancellationToken) where T : Object
            {
                LastLocation = location;
                T asset = Asset as T;
                return UniTask.FromResult(asset != null
                    ? new WorldSettingsAssetLoadResult<T>(true, asset, null)
                    : new WorldSettingsAssetLoadResult<T>(false, null, "Missing test asset."));
            }
        }
    }
}
