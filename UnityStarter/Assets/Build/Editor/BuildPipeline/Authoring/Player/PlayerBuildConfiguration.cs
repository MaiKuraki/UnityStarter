using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [CreateAssetMenu(menuName = "CycloneGames/Build/Player Build Configuration")]
    public sealed class PlayerBuildConfiguration : ScriptableObject
    {
        [Tooltip("Ordered, provider-owned extensions applied around the Unity Player build. An empty list builds an unextended Player.")]
        [SerializeField] private PlayerBuildExtensionConfiguration[] extensions =
            Array.Empty<PlayerBuildExtensionConfiguration>();

        public IReadOnlyList<PlayerBuildExtensionConfiguration> Extensions
        {
            get
            {
                if (extensions == null || extensions.Length == 0)
                {
                    return Array.Empty<PlayerBuildExtensionConfiguration>();
                }

                var snapshot = new PlayerBuildExtensionConfiguration[extensions.Length];
                Array.Copy(extensions, snapshot, extensions.Length);
                return new ReadOnlyCollection<PlayerBuildExtensionConfiguration>(snapshot);
            }
        }
    }
}
