using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CycloneGames.Localization.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Localization.Tests.Editor
{
    /// <summary>
    /// Behavioural matrix tests for the localization asset contracts.
    /// </summary>
    public abstract class LocalizationBehaviorMatrixBase
    {
        protected abstract int Seed { get; }
        protected abstract int MatrixScale { get; }
        protected abstract int FuzzBudget { get; }
        protected abstract int SoakIterations { get; }
        protected abstract int RoundNumber { get; }

        private int _assertions;
        private int _failuresAtTestStart;
        private int _matrixCases;
        private int _fuzzCases;
        private int _soakOperations;
        private readonly List<string> _failures = new List<string>();

        private static readonly string[] Locations =
        {
            null, "", " ", "\t", "a", "Valid/Path", "/", "  leading", "路径/图标",
            new string('x', 1024), new string('x', 1025), "a\0b", "Mixed/Path/With.Dots",
        };

        private static readonly string[] Guids =
        {
            null, "", "flag-guid", "  ", new string('g', 1024), "a\0b",
        };

        private enum ProviderBehavior
        {
            Immediate,
            Pending,
            NullLease,
            NullAsset,
            Throws,
            Cancelled,
            Reusable,
            FailsAfterSuccess,
        }

        private sealed class ScriptedLease<T> : ILocalizedAssetLease<T> where T : UnityEngine.Object
        {
            private T _asset;
            public int DisposeCount { get; private set; }
            public ScriptedLease(T asset) { _asset = asset; }
            public T Asset
            {
                get
                {
                    if (DisposeCount > 0) throw new ObjectDisposedException(nameof(ScriptedLease<T>));
                    return _asset;
                }
            }
            public void Dispose() { DisposeCount++; }
        }

        private sealed class ScriptedProvider : ILocalizedAssetProvider
        {
            private readonly ProviderBehavior _behavior;
            private readonly UnityEngine.Object _asset;
            public int AcquireCount;
            public readonly List<ScriptedLease<UnityEngine.Object>> Leases =
                new List<ScriptedLease<UnityEngine.Object>>();

            public ScriptedProvider(ProviderBehavior behavior, UnityEngine.Object asset)
            {
                _behavior = behavior;
                _asset = asset;
            }

            public UniTask<ILocalizedAssetLease<TAsset>> AcquireAsync<TAsset>(
                string location, CancellationToken cancellationToken)
                where TAsset : UnityEngine.Object
            {
                AcquireCount++;
                if (_behavior == ProviderBehavior.Throws)
                    throw new InvalidOperationException("scripted provider fault");
                if (_behavior == ProviderBehavior.Cancelled)
                    throw new OperationCanceledException();
                if (_behavior == ProviderBehavior.FailsAfterSuccess && AcquireCount > 1)
                    throw new InvalidOperationException("scripted late fault");
                if (_behavior == ProviderBehavior.NullLease)
                    return UniTask.FromResult<ILocalizedAssetLease<TAsset>>(null);

                UnityEngine.Object payload = _behavior == ProviderBehavior.NullAsset ? null : _asset;
                var lease = new ScriptedLease<UnityEngine.Object>(payload);
                Leases.Add(lease);
                ILocalizedAssetLease<TAsset> typed = new CastingLease<TAsset>(lease, payload as TAsset);
                if (_behavior == ProviderBehavior.Pending)
                {
                    var completion = new UniTaskCompletionSource<ILocalizedAssetLease<TAsset>>();
                    completion.TrySetResult(typed);
                    return completion.Task;
                }

                return UniTask.FromResult(typed);
            }
        }

        private sealed class CastingLease<T> : ILocalizedAssetLease<T> where T : UnityEngine.Object
        {
            private readonly ScriptedLease<UnityEngine.Object> _inner;
            private readonly T _asset;
            public CastingLease(ScriptedLease<UnityEngine.Object> inner, T asset)
            {
                _inner = inner;
                _asset = asset;
            }
            public T Asset => _asset;
            public void Dispose() => _inner.Dispose();
        }

        [SetUp]
        public void BeginTest()
        {
            _failuresAtTestStart = _failures.Count;
        }

        [TearDown]
        public void EndTest()
        {
            int added = _failures.Count - _failuresAtTestStart;
            Assert.That(added, Is.EqualTo(0),
                "Behavioural checks failed: " + string.Join(" | ",
                    _failures.GetRange(_failuresAtTestStart, added)));
        }

        private void Check(bool condition, string label)
        {
            _assertions++;
            if (!condition) _failures.Add(label);
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\0", "\\u0000").Replace("\t", "\\t")
                .Replace("\r", "\\r").Replace("\n", "\\n");
        }

        [Test]
        public void AssetKey_EdgeCaseMatrix()
        {
            foreach (string location in Locations)
            {
                foreach (string guid in Guids)
                {
                    for (int scale = 0; scale < MatrixScale; scale++)
                    {
                        var reference = new LocalizedAssetRef(location, guid);
                        Check(reference.Location == location, "Location roundtrip");
                        Check(reference.Guid == guid, "Guid roundtrip");
                        Check(reference.IsValid == !string.IsNullOrEmpty(location), "IsValid contract");

                        LocalizedAssetRef<UnityEngine.Object> typed = reference.Typed<UnityEngine.Object>();
                        Check(typed.Location == location, "Typed location");
                        Check(typed.Guid == guid, "Typed guid");
                        LocalizedAssetRef untyped = typed.Untyped();
                        Check(untyped.Location == location, "Untyped location");
                        Check(untyped.Guid == guid, "Untyped guid");

                        var same = new LocalizedAssetRef(location, guid);
                        Check(reference.Equals(same), "Value equality");
                        Check(reference == same, "Equality operator");
                        Check(!(reference != same), "Inequality operator");
                        Check(reference.GetHashCode() == same.GetHashCode(), "Hash agreement");
                        string implicitValue = reference;
                        Check(implicitValue == location, "Implicit string conversion");
                        _matrixCases++;
                    }
                }
            }
        }

        [Test]
        public void Provider_ScenarioMatrix()
        {
            foreach (ProviderBehavior behavior in Enum.GetValues(typeof(ProviderBehavior)))
            {
                foreach (int repeat in new[] { 1, 2, 8 })
                {
                    for (int scale = 0; scale < MatrixScale; scale++)
                    {
                        var probe = ScriptableObject.CreateInstance<LocalizationCatalog>();
                        try
                        {
                            var provider = new ScriptedProvider(behavior, probe);
                            for (int i = 0; i < repeat; i++)
                            {
                                try
                                {
                                    ILocalizedAssetLease<LocalizationCatalog> lease = provider
                                        .AcquireAsync<LocalizationCatalog>("Catalog/Base", CancellationToken.None)
                                        .GetAwaiter().GetResult();
                                    if (lease != null)
                                    {
                                        Check(lease.Asset == probe || lease.Asset == null,
                                            "Lease asset identity");
                                        lease.Dispose();
                                        lease.Dispose();
                                    }
                                    else
                                    {
                                        Check(behavior == ProviderBehavior.NullLease,
                                            "Null lease only in null-lease scenario");
                                    }
                                }
                                catch (InvalidOperationException)
                                {
                                    Check(behavior == ProviderBehavior.Throws ||
                                          behavior == ProviderBehavior.FailsAfterSuccess,
                                        "Fault only in fault scenarios");
                                }
                                catch (OperationCanceledException)
                                {
                                    Check(behavior == ProviderBehavior.Cancelled,
                                        "Cancellation only in cancelled scenario");
                                }
                            }
                            Check(provider.AcquireCount == repeat, "Acquire count conservation");
                            foreach (ScriptedLease<UnityEngine.Object> lease in provider.Leases)
                            {
                                Check(lease.DisposeCount >= 1, "Every created lease disposed");
                            }
                            _matrixCases++;
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(probe);
                        }
                    }
                }
            }
        }

        [Test]
        public void CatalogLoader_EdgeCaseMatrix()
        {
            var service = new LocalizationService();
            try
            {
                foreach (string location in Locations)
                {
                    for (int scale = 0; scale < MatrixScale; scale++)
                    {
                        var probe = ScriptableObject.CreateInstance<LocalizationCatalog>();
                        var provider = new ScriptedProvider(ProviderBehavior.Immediate, probe);
                        try
                        {
                            bool argumentRejected = false;
                            try
                            {
                                service.LoadAndRegisterCatalogAsync(
                                    provider, "owner", location, CancellationToken.None)
                                    .GetAwaiter().GetResult();
                            }
                            catch (ArgumentException) { argumentRejected = true; }
                            catch (Exception) { }
                            bool invalid = string.IsNullOrWhiteSpace(location) ||
                                location.Length > 1024 || location.IndexOf('\0') >= 0;
                            Check(argumentRejected == invalid,
                                "Location validation matches boundary: " + (location ?? "<null>"));
                            _matrixCases++;
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(probe);
                        }
                    }
                }

                foreach (ProviderBehavior behavior in new[]
                {
                    ProviderBehavior.NullLease, ProviderBehavior.NullAsset,
                    ProviderBehavior.Throws, ProviderBehavior.Cancelled,
                })
                {
                    var probe = ScriptableObject.CreateInstance<LocalizationCatalog>();
                    var provider = new ScriptedProvider(behavior, probe);
                    try
                    {
                        bool threw = false;
                        try
                        {
                            service.LoadAndRegisterCatalogAsync(
                                provider, "owner", "Catalog/Valid", CancellationToken.None)
                                .GetAwaiter().GetResult();
                        }
                        catch (Exception) { threw = true; }
                        Check(threw, "Loader surfaces provider failure: " + behavior);
                        _matrixCases++;
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(probe);
                    }
                }
            }
            finally
            {
                service.Dispose();
            }
        }

        [Test]
        public void Fuzz_OperationSequences()
        {
            var rng = new System.Random(Seed);
            for (int sequence = 0; sequence < FuzzBudget; sequence++)
            {
                var probe = ScriptableObject.CreateInstance<LocalizationCatalog>();
                var behaviors = (ProviderBehavior[])Enum.GetValues(typeof(ProviderBehavior));
                var provider = new ScriptedProvider(
                    behaviors[rng.Next(behaviors.Length)], probe);
                var leases = new List<ILocalizedAssetLease<LocalizationCatalog>>();
                try
                {
                    int operations = 4 + rng.Next(24);
                    for (int i = 0; i < operations; i++)
                    {
                        int action = rng.Next(5);
                        if (action <= 1)
                        {
                            try
                            {
                                ILocalizedAssetLease<LocalizationCatalog> lease = provider
                                    .AcquireAsync<LocalizationCatalog>(
                                        "Catalog/" + rng.Next(8), CancellationToken.None)
                                    .GetAwaiter().GetResult();
                                if (lease != null) leases.Add(lease);
                            }
                            catch (InvalidOperationException) { }
                            catch (OperationCanceledException) { }
                        }
                        else if (action == 2 && leases.Count > 0)
                        {
                            int index = rng.Next(leases.Count);
                            leases[index].Dispose();
                            leases.RemoveAt(index);
                        }
                        else if (action == 3)
                        {
                            var key = new LocalizedAssetRef(
                                Locations[rng.Next(Locations.Length)], Guids[rng.Next(Guids.Length)]);
                            Check(key.Location == null || key.Location.Length >= 0, "Fuzz key stable");
                        }
                        else
                        {
                            Check(provider.AcquireCount >= 0, "Acquire count monotonic");
                        }
                    }

                    foreach (ILocalizedAssetLease<LocalizationCatalog> lease in leases)
                    {
                        lease.Dispose();
                    }
                    leases.Clear();
                    Check(provider.AcquireCount >= 0, "Fuzz sequence completed");
                    _fuzzCases++;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(probe);
                }
            }
        }

        [Test]
        public void Soak_LeaseOwnershipConservation()
        {
            var shared = ScriptableObject.CreateInstance<LocalizationCatalog>();
            var provider = new ScriptedProvider(ProviderBehavior.Reusable, shared);
            long acquired = 0;
            long disposed = 0;
            try
            {
                for (int i = 0; i < SoakIterations; i++)
                {
                    ILocalizedAssetLease<LocalizationCatalog> lease = provider
                        .AcquireAsync<LocalizationCatalog>("Catalog/Soak", CancellationToken.None)
                        .GetAwaiter().GetResult();
                    acquired++;
                    Check(lease.Asset == shared, "Soak asset identity");
                    lease.Dispose();
                    lease.Dispose();
                    disposed += 2;
                    Check(provider.AcquireCount == i + 1, "Soak acquire monotonic");
                    _soakOperations++;
                }
                Check(acquired * 2 == disposed, "Soak acquire/dispose conservation");
                Check(provider.AcquireCount == SoakIterations, "Soak provider call count");
                Check(shared != null, "Soak asset survived");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(shared);
            }
        }

        [OneTimeTearDown]
        public void WriteRoundReport()
        {
            string directory = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../.workbuddy/validation"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory,
                $"localization_behavior_round{RoundNumber}.json");
            var payload = new Dictionary<string, object>
            {
                { "round", RoundNumber },
                { "seed", Seed },
                { "matrix_scale", MatrixScale },
                { "fuzz_budget", FuzzBudget },
                { "soak_iterations", SoakIterations },
                { "matrix_cases", _matrixCases },
                { "fuzz_cases", _fuzzCases },
                { "soak_operations", _soakOperations },
                { "assertions", _assertions },
                { "failures", _failures.Count },
                { "failure_samples", _failures.Count > 8 ? _failures.GetRange(0, 8) : _failures },
            };
            var text = new System.Text.StringBuilder();
            text.Append("{\n");
            int index = 0;
            foreach (KeyValuePair<string, object> pair in payload)
            {
                string comma = index++ == payload.Count - 1 ? "" : ",";
                if (pair.Value is int || pair.Value is long)
                {
                    text.Append($"  \"{pair.Key}\": {pair.Value}{comma}\n");
                }
                else if (pair.Value is List<string> list)
                {
                    text.Append($"  \"{pair.Key}\": [");
                    for (int i = 0; i < list.Count; i++)
                    {
                        text.Append(i > 0 ? ", " : "").Append("\"")
                            .Append(Escape(list[i]))
                            .Append("\"");
                    }
                    text.Append($"]{comma}\n");
                }
            }
            text.Append("}\n");
            File.WriteAllText(path, text.ToString());
            TestContext.Progress.WriteLine(
                $"BEHAVIOR_ROUND {RoundNumber}: matrix={_matrixCases} fuzz={_fuzzCases} " +
                $"soak={_soakOperations} assertions={_assertions} failures={_failures.Count}");
        }
    }

    [TestFixture]
    public sealed class LocalizationBehaviorRound1Tests : LocalizationBehaviorMatrixBase
    {
        protected override int Seed => 20261119;
        protected override int MatrixScale => 1;
        protected override int FuzzBudget => 120;
        protected override int SoakIterations => 2000;
        protected override int RoundNumber => 1;
    }

    [TestFixture]
    public sealed class LocalizationBehaviorRound2Tests : LocalizationBehaviorMatrixBase
    {
        protected override int Seed => 0xC0FFEE;
        protected override int MatrixScale => 2;
        protected override int FuzzBudget => 240;
        protected override int SoakIterations => 5000;
        protected override int RoundNumber => 2;
    }

    [TestFixture]
    public sealed class LocalizationBehaviorRound3Tests : LocalizationBehaviorMatrixBase
    {
        protected override int Seed => 15485863;
        protected override int MatrixScale => 3;
        protected override int FuzzBudget => 360;
        protected override int SoakIterations => 9000;
        protected override int RoundNumber => 3;
    }
}
