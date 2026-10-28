using System;
using System.Collections.Generic;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Guard tests for the engine seam: the Core assemblies of GameplayTags and
    /// GameplayAbilities must stay Unity-free so the simulation remains portable
    /// (Godot, headless server, CLI). Runtime Unity references are confined to a
    /// fixed whitelist of authoring/presentation adapter files; growing that list
    /// requires an explicit, reviewed change to this test.
    /// </summary>
    public sealed class GASEngineSeamGuardTests
    {
        private const string PackagesRoot = "Assets/ThirdParty/CycloneGames";

        private static readonly string[] RuntimeUnityWhitelist =
        {
            "AbilitySystemComponent.cs",
            "ConcreteGameplayEffectSO.cs",
            "GameObjectPoolManager.cs",
            "GameplayAbility.cs",
            "GameplayAbilitySO.cs",
            "GameplayAbilityTargetTypes.cs",
            "GameplayCueManager.cs",
            "GameplayCueSO.cs",
            "GameplayEffectExecutionCalculationSO.cs",
            "GameplayEffectSO.cs",
            "GameplayModMagnitudeCalculationSO.cs",
            "GASDebugOverlay.cs",
            "GASOverlayConfig.cs",
            "GASTrace.cs",
            "IGameplayCueSystem.cs",
            "Integrations/AssetManagement/AssetManagementResourceLocator.cs",
            "ModifierInfoSerializable.cs",
            "TargetingQuery.cs"
        };

        [Test]
        public void GameplayTagsCore_HasNoUnityEngineReference()
        {
            AssertNoUnityEngineReference(System.IO.Path.Combine(PackagesRoot, "CycloneGames.GameplayTags", "Core"));
        }

        [Test]
        public void GameplayAbilitiesCore_HasNoUnityEngineReference()
        {
            AssertNoUnityEngineReference(System.IO.Path.Combine(PackagesRoot, "CycloneGames.GameplayAbilities", "Core"));
        }

        /// <summary>
        /// Core must not branch on host-specific compilation symbols. asmdef noEngineReferences blocks
        /// type references only, so the symbol rule needs its own guard.
        /// </summary>
        [Test]
        public void CoreAssemblies_UseNoHostSpecificCompilationSymbols()
        {
            AssertNoHostSymbols(System.IO.Path.Combine(PackagesRoot, "CycloneGames.GameplayTags", "Core"));
            AssertNoHostSymbols(System.IO.Path.Combine(PackagesRoot, "CycloneGames.GameplayAbilities", "Core"));
        }

        [Test]
        public void GameplayAbilitiesRuntime_UnityReferencesStayWhitelisted()
        {
            string runtimeRoot = System.IO.Path.Combine(PackagesRoot, "CycloneGames.GameplayAbilities", "Runtime");
            var offenders = new List<string>();
            foreach (string file in System.IO.Directory.GetFiles(runtimeRoot, "*.cs", System.IO.SearchOption.AllDirectories))
            {
                string content = System.IO.File.ReadAllText(file);
                if (!content.Contains("UnityEngine"))
                {
                    continue;
                }

                string relative = System.IO.Path.GetRelativePath(runtimeRoot, file).Replace('\\', '/');
                offenders.Add(relative);
            }

            offenders.Sort(StringComparer.Ordinal);
            var expected = new List<string>(RuntimeUnityWhitelist);
            expected.Sort(StringComparer.Ordinal);

            Assert.That(offenders, Is.EqualTo(expected),
                "Runtime UnityEngine references must stay confined to the reviewed adapter whitelist. " +
                "New Unity-dependent runtime code belongs in the Editor/Integrations layer, not the simulation.");
        }

        private static readonly string[] HostSymbolMarkers =
        {
            "UNITY_",
            "DEVELOPMENT_BUILD",
            "GODOT",
            "UNREAL",
            "UE_"
        };

        private static void AssertNoHostSymbols(string relativeDirectory)
        {
            string absolute = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), relativeDirectory);
            Assert.That(System.IO.Directory.Exists(absolute), Is.True, $"Expected directory missing: {relativeDirectory}");

            // Only conditional-compilation lines are checked, so prose may name these symbols.
            var offenders = new List<string>();
            foreach (string file in System.IO.Directory.GetFiles(absolute, "*.cs", System.IO.SearchOption.AllDirectories))
            {
                string relative = System.IO.Path.GetRelativePath(absolute, file).Replace('\\', '/');
                foreach (string line in System.IO.File.ReadLines(file))
                {
                    string directive = line.TrimStart();
                    if (!directive.StartsWith("#if", StringComparison.Ordinal) &&
                        !directive.StartsWith("#elif", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (string marker in HostSymbolMarkers)
                    {
                        if (!directive.Contains(marker))
                        {
                            continue;
                        }

                        // Reviewed exception: GameplayTagManager gates InternalsVisibleTo only; nothing
                        // reaches a player build.
                        if (directive.Contains("UNITY_INCLUDE_TESTS"))
                        {
                            break;
                        }

                        offenders.Add($"{relative} ({marker})");
                        break;
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                $"Core assembly '{relativeDirectory}' must not branch on host-specific compilation symbols; " +
                $"use DEBUG or a CycloneGames-owned switch instead. Offenders: {string.Join(", ", offenders)}");
        }

        private static void AssertNoUnityEngineReference(string relativeDirectory)
        {
            string absolute = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), relativeDirectory);
            Assert.That(System.IO.Directory.Exists(absolute), Is.True, $"Expected directory missing: {relativeDirectory}");

            var offenders = new List<string>();
            foreach (string file in System.IO.Directory.GetFiles(absolute, "*.cs", System.IO.SearchOption.AllDirectories))
            {
                if (System.IO.File.ReadAllText(file).Contains("UnityEngine"))
                {
                    offenders.Add(System.IO.Path.GetRelativePath(absolute, file));
                }
            }

            Assert.That(offenders, Is.Empty,
                $"Core assembly '{relativeDirectory}' must stay Unity-free (portability seam). Offenders: {string.Join(", ", offenders)}");
        }
    }
}
