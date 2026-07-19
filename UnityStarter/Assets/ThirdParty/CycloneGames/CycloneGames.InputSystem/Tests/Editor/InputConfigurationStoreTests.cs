using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CycloneGames.InputSystem.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace CycloneGames.InputSystem.Tests.Editor
{
    public sealed class InputConfigurationStoreTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "CycloneGames.InputSystem.Tests",
                Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [Test]
        public void TryResolveKey_RejectsRootedAndTraversalPaths()
        {
            var store = new FileInputConfigurationStore(_root);

            Assert.That(store.TryResolveKey(Path.Combine(_root, "input.yaml"), out _, out _), Is.False);
            Assert.That(store.TryResolveKey("../input.yaml", out _, out _), Is.False);
            Assert.That(store.TryResolveKey("profiles/../input.yaml", out _, out _), Is.False);
            Assert.That(store.TryResolveKey("profiles\\input.yaml", out _, out _), Is.False);
            Assert.That(store.TryResolveKey("https://example.invalid/input.yaml", out _, out _), Is.False);
            Assert.That(store.TryResolveKey("input\u200B.yaml", out _, out _), Is.False);
            Assert.That(store.TryResolveKey("input:alternate.yaml", out _, out _), Is.False);
            Assert.That(store.TryResolveKey("NUL.yaml", out _, out _), Is.False);
            Assert.That(store.TryResolveKey("input*.yaml", out _, out _), Is.False);
            Assert.That(store.TryResolveKey("input.yaml.bak", out _, out _), Is.False);
            Assert.That(store.TryResolveKey("input.yaml.tmp-deadbeef", out _, out _), Is.False);
            Assert.That(store.TryResolveKey("input.yaml.bak.tmp.deadbeef", out _, out _), Is.False);
            Assert.That(store.TryResolveKey("profiles/input.yaml.bak/current.yaml", out _, out _), Is.False);
            Assert.That(store.TryResolveKey("profiles/cache.tmp-deadbeef/current.yaml", out _, out _), Is.False);
            Assert.That(store.TryResolveKey("profiles/cache.bak.tmp.deadbeef/current.yaml", out _, out _), Is.False);
            Assert.That(store.TryResolveKey(new string('a', 513), out _, out _), Is.False);
            Assert.That(store.TryResolveKey("input\uD800.yaml", out _, out _), Is.False);
            Assert.That(store.TryResolveKey("profiles/Cafe\u0301.yaml", out _, out _), Is.False);
            Assert.That(store.TryResolveKey("profiles/Caf\u00E9.yaml", out _, out _), Is.True);
            Assert.That(store.TryResolveKey("profiles/\U0001F600.yaml", out _, out _), Is.True);
        }

        [Test]
        public async Task SaveAndLoadAsync_UsesRootConfinedLogicalKey()
        {
            var store = new FileInputConfigurationStore(_root, 1024);

            InputConfigurationStoreResult save = await store.SaveAsync("profiles/input.yaml", "playerSlots: []");
            InputConfigurationReadResult load = await store.LoadAsync("profiles/input.yaml");

            Assert.That(save.IsSuccess, Is.True, save.Error);
            Assert.That(load.IsSuccess, Is.True, load.Error);
            Assert.That(load.Content, Is.EqualTo("playerSlots: []"));
            Assert.That(File.Exists(Path.Combine(_root, "profiles", "input.yaml")), Is.True);
        }

        [Test]
        public async Task SaveAsync_ReplacesThroughBackupWithoutLosingPreviousContent()
        {
            var store = new FileInputConfigurationStore(_root, 1024);

            InputConfigurationStoreResult first = await store.SaveAsync("input.yaml", "first");
            InputConfigurationStoreResult second = await store.SaveAsync("input.yaml", "second");

            Assert.That(first.IsSuccess, Is.True, first.Error);
            Assert.That(second.IsSuccess, Is.True, second.Error);
            Assert.That(File.ReadAllText(Path.Combine(_root, "input.yaml"), Encoding.UTF8), Is.EqualTo("second"));
            Assert.That(File.ReadAllText(Path.Combine(_root, "input.yaml.bak"), Encoding.UTF8), Is.EqualTo("first"));
        }

        [Test]
        public async Task SaveAsync_SerializesSamePathAcrossStoreInstances()
        {
            const int OperationCount = 24;
            var payloads = new string[OperationCount];
            var operations = new Task<InputConfigurationStoreResult>[OperationCount];
            for (int i = 0; i < OperationCount; i++)
            {
                payloads[i] = $"payload-{i:D2}";
                var store = new FileInputConfigurationStore(_root, 1024);
                operations[i] = store.SaveAsync("input.yaml", payloads[i]).AsTask();
            }

            InputConfigurationStoreResult[] results = await Task.WhenAll(operations);
            for (int i = 0; i < results.Length; i++)
            {
                Assert.That(results[i].IsSuccess, Is.True, results[i].Error);
            }

            string primary = File.ReadAllText(Path.Combine(_root, "input.yaml"), Encoding.UTF8);
            string backup = File.ReadAllText(Path.Combine(_root, "input.yaml.bak"), Encoding.UTF8);
            Assert.That(payloads, Does.Contain(primary));
            Assert.That(payloads, Does.Contain(backup));
            Assert.That(backup, Is.Not.EqualTo(primary));
            Assert.That(Directory.GetFiles(_root, "*.tmp-*"), Is.Empty);
        }

        [Test]
        public async Task LoadAsync_UsesBackupWhenFallbackCommitWasInterrupted()
        {
            var store = new FileInputConfigurationStore(_root, 1024);
            Assert.That((await store.SaveAsync("input.yaml", "first")).IsSuccess, Is.True);
            Assert.That((await store.SaveAsync("input.yaml", "second")).IsSuccess, Is.True);
            File.Delete(Path.Combine(_root, "input.yaml"));

            InputConfigurationReadResult load = await store.LoadAsync("input.yaml");

            Assert.That(load.IsSuccess, Is.True, load.Error);
            Assert.That(load.WasRecoveredFromBackup, Is.True);
            Assert.That(load.Content, Is.EqualTo("first"));
        }

        [Test]
        public async Task SaveAsync_RejectsContentAboveConfiguredBudget()
        {
            var store = new FileInputConfigurationStore(_root, 8);

            InputConfigurationStoreResult result = await store.SaveAsync("input.yaml", "123456789");

            Assert.That(result.Status, Is.EqualTo(InputConfigurationStorageStatus.TooLarge));
            Assert.That(File.Exists(Path.Combine(_root, "input.yaml")), Is.False);
        }

        [Test]
        public async Task LoadAsync_RejectsExistingFileAboveConfiguredBudget()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, "input.yaml"), "123456789", new UTF8Encoding(false));
            var store = new FileInputConfigurationStore(_root, 8);

            InputConfigurationReadResult result = await store.LoadAsync("input.yaml");

            Assert.That(result.Status, Is.EqualTo(InputConfigurationStorageStatus.TooLarge));
        }

        [Test]
        public async Task SaveAndLoadAsync_RejectInvalidUnicodeAndUtf8()
        {
            var store = new FileInputConfigurationStore(_root, 1024);

            InputConfigurationStoreResult save = await store.SaveAsync("input.yaml", "value: \uD800");
            Assert.That(save.Status, Is.EqualTo(InputConfigurationStorageStatus.InvalidContent));

            Directory.CreateDirectory(_root);
            File.WriteAllBytes(Path.Combine(_root, "input.yaml"), new byte[] { 0xC3, 0x28 });
            InputConfigurationReadResult load = await store.LoadAsync("input.yaml");
            Assert.That(load.Status, Is.EqualTo(InputConfigurationStorageStatus.InvalidContent));
        }

        [Test]
        public async Task DeleteAsync_RemovesPrimaryAndRecoveryBackup()
        {
            var store = new FileInputConfigurationStore(_root, 1024);
            Assert.That((await store.SaveAsync("input.yaml", "first")).IsSuccess, Is.True);
            Assert.That((await store.SaveAsync("input.yaml", "second")).IsSuccess, Is.True);

            InputConfigurationStoreResult delete = await store.DeleteAsync("input.yaml");
            InputConfigurationReadResult load = await store.LoadAsync("input.yaml");

            Assert.That(delete.IsSuccess, Is.True, delete.Error);
            Assert.That(File.Exists(Path.Combine(_root, "input.yaml")), Is.False);
            Assert.That(File.Exists(Path.Combine(_root, "input.yaml.bak")), Is.False);
            Assert.That(load.Status, Is.EqualTo(InputConfigurationStorageStatus.NotFound));
        }

        [Test]
        public void CompatibilityDelete_NormalizesNestedKeyAndRemovesRecoveryState()
        {
            string directory = Path.Combine(
                UnityEngine.Application.persistentDataPath,
                "CycloneGames.InputSystem.Tests",
                Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "input.yaml");
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(path, "current", new UTF8Encoding(false));
                File.WriteAllText(path + ".bak", "previous", new UTF8Encoding(false));

#pragma warning disable CS0618 // Verifies the retained synchronous compatibility contract.
                bool deleted = InputSystemLoader.TryDeleteUserConfigFile(path);
#pragma warning restore CS0618

                Assert.That(deleted, Is.True);
                Assert.That(File.Exists(path), Is.False);
                Assert.That(File.Exists(path + ".bak"), Is.False);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Constructor_RejectsUnboundedAllocationBudget()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new FileInputConfigurationStore(_root, FileInputConfigurationStore.MaximumSupportedBytes + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new UriInputConfigurationSource(FileInputConfigurationStore.MaximumSupportedBytes + 1));
        }

        [Test]
        public void HttpsAllowlist_RequiresCredentialFreeDefaultPortOrigin()
        {
            var source = new UriInputConfigurationSource(
                allowedHttpsHosts: new[] { "config.example.com" });

            Assert.That(
                source.TryNormalizeAllowedUri(
                    "https://config.example.com/input.yaml",
                    out string normalized,
                    out _),
                Is.True);
            Assert.That(normalized, Is.EqualTo("https://config.example.com/input.yaml"));
            Assert.That(
                source.TryNormalizeAllowedUri(
                    "https://user@config.example.com/input.yaml",
                    out _,
                    out _),
                Is.False);
            Assert.That(
                source.TryNormalizeAllowedUri(
                    "https://config.example.com:444/input.yaml",
                    out _,
                    out _),
                Is.False);
            Assert.That(
                source.TryNormalizeAllowedUri(
                    "https://other.example.com/input.yaml",
                    out _,
                    out _),
                Is.False);
        }

        [Test]
        public void UriSource_RejectsOversizedUriHostAndAllowlistBeforeUse()
        {
            var source = new UriInputConfigurationSource(
                allowedHttpsHosts: new[] { "config.example.com" });

            Assert.That(
                source.TryNormalizeAllowedUri(
                    new string('a', UriInputConfigurationSource.MaximumUriCharacters + 1),
                    out _,
                    out _),
                Is.False);
            Assert.That(
                source.TryNormalizeAllowedUri(
                    "https://" +
                    new string('a', UriInputConfigurationSource.MaximumHttpsHostCharacters + 1) +
                    "/input.yaml",
                    out _,
                    out _),
                Is.False);
            Assert.Throws<ArgumentException>(() =>
                new UriInputConfigurationSource(
                    allowedHttpsHosts: new[]
                    {
                        new string('a', UriInputConfigurationSource.MaximumHttpsHostCharacters + 1)
                    }));

            var hosts = new string[UriInputConfigurationSource.MaximumAllowedHttpsHosts + 1];
            for (int i = 0; i < hosts.Length; i++)
            {
                hosts[i] = $"config-{i}.example.com";
            }

            Assert.Throws<ArgumentException>(() =>
                new UriInputConfigurationSource(allowedHttpsHosts: hosts));
            Assert.Throws<ArgumentException>(() =>
                new UriInputConfigurationSource(
                    allowedHttpsHosts: new[] { "config.example.com/path" }));
            Assert.Throws<ArgumentException>(() =>
                new UriInputConfigurationSource(
                    allowedHttpsHosts: new[] { "config.\u200Bexample.com" }));
        }

        [Test]
        public async Task DeleteAsync_DoesNotAcceptPathOutsideRoot()
        {
            string outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".yaml");
            File.WriteAllText(outside, "preserve", new UTF8Encoding(false));
            var store = new FileInputConfigurationStore(_root);

            try
            {
                InputConfigurationStoreResult result = await store.DeleteAsync(outside);

                Assert.That(result.Status, Is.EqualTo(InputConfigurationStorageStatus.InvalidKey));
                Assert.That(File.ReadAllText(outside, Encoding.UTF8), Is.EqualTo("preserve"));
            }
            finally
            {
                File.Delete(outside);
            }
        }
    }
}
