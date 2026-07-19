using System;
using System.IO;
using System.Threading;
using MessagePack;
using MessagePack.Resolvers;
using NUnit.Framework;
using UnityEditor.PackageManager;
using MessagePackIntegration = CycloneGames.DataTable.Unity.Integrations.MessagePack;

namespace CycloneGames.DataTable.Tests.Editor.Integrations.MessagePack
{
    public sealed class MessagePackDataTableIntegrationTests
    {
        private static readonly MessagePackSerializerOptions Options =
            MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance);

        private static readonly MessagePackSecurity Security =
            MessagePackSecurity.UntrustedData
                .WithMaximumObjectGraphDepth(16)
                .WithMaximumDecompressedSize(1024 * 1024);

        private static readonly DataTableLoadLimits Limits = new DataTableLoadLimits(
            maxTableCount: 8,
            maxBytesPerTable: 1024 * 1024,
            maxTotalBytes: 8L * 1024 * 1024,
            maxRowsPerTable: 4,
            maxTableNameLength: 64);

        [Test]
        public void InstalledUnityClientAndRuntimeAssemblies_HaveMatchingVersions()
        {
            PackageInfo package = PackageInfo.FindForPackageName("com.github.messagepack-csharp");
            Assert.That(package, Is.Not.Null, "The MessagePack Unity client package must be registered.");

            string semanticVersion = package.version.Split('-', '+')[0];
            Assert.That(Version.TryParse(semanticVersion, out Version packageVersion), Is.True);
            AssertMatchingVersion(
                packageVersion,
                typeof(MessagePackSerializer).Assembly.GetName().Version,
                "MessagePack.dll");
            AssertMatchingVersion(
                packageVersion,
                typeof(MessagePackObjectAttribute).Assembly.GetName().Version,
                "MessagePack.Annotations.dll");
        }

        [Test]
        public void Build_UsesGeneratedResolverAndBuildsImmutableLookup()
        {
            Assert.That(
                GeneratedMessagePackResolver.Instance.GetFormatter<TestRow>(),
                Is.Not.Null,
                "The test DTO must have a source-generated formatter before the standard resolver handles its array container.");

            TestRow[] rows =
            {
                new TestRow { Id = 7, Name = "alpha" },
                new TestRow { Id = 13, Name = "beta" }
            };
            byte[] payload = MessagePackSerializer.Serialize(rows, Options);

            DataTable<TestRow> table = MessagePackIntegration.MessagePackConfigProvider.Build<TestRow>(
                payload,
                Options,
                Security,
                Limits);

            Assert.That(table.Count, Is.EqualTo(2));
            Assert.That(table.Get(13).Name, Is.EqualTo("beta"));
            Assert.That(table.TryGet(7, out TestRow row), Is.True);
            Assert.That(row.Name, Is.EqualTo("alpha"));
        }

        [Test]
        public void Build_RejectsTrailingPayloadBytes()
        {
            byte[] encoded = MessagePackSerializer.Serialize(
                new[] { new TestRow { Id = 1, Name = "one" } },
                Options);
            var payload = new byte[encoded.Length + 1];
            Buffer.BlockCopy(encoded, 0, payload, 0, encoded.Length);
            payload[payload.Length - 1] = 0xc0;

            Assert.Throws<InvalidDataException>(() =>
                MessagePackIntegration.MessagePackConfigProvider.Build<TestRow>(
                    payload,
                    Options,
                    Security,
                    Limits));
        }

        [Test]
        public void Build_RejectsTruncatedPayload()
        {
            byte[] encoded = MessagePackSerializer.Serialize(
                new[] { new TestRow { Id = 1, Name = "one" } },
                Options);
            var truncated = new byte[encoded.Length - 1];
            Buffer.BlockCopy(encoded, 0, truncated, 0, truncated.Length);

            Assert.Throws<InvalidDataException>(() =>
                MessagePackIntegration.MessagePackConfigProvider.Build<TestRow>(
                    truncated,
                    Options,
                    Security,
                    Limits));
        }

        [Test]
        public void Build_RejectsPayloadBeyondPerTableByteLimit()
        {
            byte[] payload = MessagePackSerializer.Serialize(
                new[] { new TestRow { Id = 1, Name = "one" } },
                Options);
            var oneByteLimit = new DataTableLoadLimits(
                maxTableCount: 1,
                maxBytesPerTable: 1,
                maxTotalBytes: 1,
                maxRowsPerTable: 4,
                maxTableNameLength: 32);
            MessagePackSecurity security = MessagePackSecurity.UntrustedData
                .WithMaximumObjectGraphDepth(16)
                .WithMaximumDecompressedSize(1);

            Assert.Throws<InvalidOperationException>(() =>
                MessagePackIntegration.MessagePackConfigProvider.Build<TestRow>(
                    payload,
                    Options,
                    security,
                    oneByteLimit));
        }

        [Test]
        public void Build_ObservesAlreadyCanceledTokenBeforeParsing()
        {
            byte[] payload = MessagePackSerializer.Serialize(
                new[] { new TestRow { Id = 1, Name = "one" } },
                Options);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                MessagePackIntegration.MessagePackConfigProvider.Build<TestRow>(
                    payload,
                    Options,
                    Security,
                    Limits,
                    cancellation.Token));
        }

        [Test]
        public void Build_RejectsDeclaredRowCountBeforeMaterialization()
        {
            byte[] payload = MessagePackSerializer.Serialize(
                new[]
                {
                    new TestRow { Id = 1, Name = "one" },
                    new TestRow { Id = 2, Name = "two" }
                },
                Options);
            var oneRowLimit = new DataTableLoadLimits(
                maxTableCount: 1,
                maxBytesPerTable: 1024 * 1024,
                maxTotalBytes: 1024 * 1024,
                maxRowsPerTable: 1,
                maxTableNameLength: 32);

            Assert.Throws<InvalidDataException>(() =>
                MessagePackIntegration.MessagePackConfigProvider.Build<TestRow>(
                    payload,
                    Options,
                    Security,
                    oneRowLimit));
        }

        [Test]
        public void Build_RejectsTrustedDataSecurityPolicy()
        {
            byte[] payload = MessagePackSerializer.Serialize(
                new[] { new TestRow { Id = 1, Name = "one" } },
                Options);

            Assert.Throws<ArgumentException>(() =>
                MessagePackIntegration.MessagePackConfigProvider.Build<TestRow>(
                    payload,
                    Options,
                    MessagePackSecurity.TrustedData,
                    Limits));
        }

        private static void AssertMatchingVersion(
            Version packageVersion,
            Version assemblyVersion,
            string assemblyName)
        {
            Assert.That(assemblyVersion, Is.Not.Null, $"{assemblyName} must expose an assembly version.");
            Assert.That(
                new Version(assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build),
                Is.EqualTo(new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build)),
                $"{assemblyName} must match the registered MessagePack Unity client package version.");
        }
    }

    [MessagePackObject]
    public sealed class TestRow : IDataRow
    {
        [Key(0)]
        public int Id { get; set; }

        [Key(1)]
        public string Name { get; set; }
    }
}
