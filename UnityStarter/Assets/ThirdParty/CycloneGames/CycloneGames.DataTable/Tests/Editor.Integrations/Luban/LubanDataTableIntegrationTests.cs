using System;
using Luban;
using NUnit.Framework;
using LubanIntegration = CycloneGames.DataTable.Unity.Integrations.Luban;

namespace CycloneGames.DataTable.Tests.Editor.Integrations.Luban
{
    public sealed class LubanDataTableIntegrationTests
    {
        [Test]
        public void Create_CopiesPayloadBeforeReturningItToGeneratedTableFactory()
        {
            byte[] source = { 3, 7, 11, 19 };
            var provider = new ArrayBytesProvider(source);

            ByteBuf buffer = LubanIntegration.LubanDataTableSetFactory.Create(
                provider,
                getBytes => getBytes("items"));

            source[0] = 255;

            Assert.That(buffer.Bytes, Is.Not.SameAs(source));
            Assert.That(buffer.Bytes, Is.EqualTo(new byte[] { 3, 7, 11, 19 }));
        }

        [Test]
        public void Create_RejectsAggregatePayloadsBeyondConfiguredBudget()
        {
            var provider = new ArrayBytesProvider(new byte[] { 1, 2, 3, 4 });
            var limits = new DataTableLoadLimits(
                maxTableCount: 2,
                maxBytesPerTable: 4,
                maxTotalBytes: 6,
                maxRowsPerTable: 16,
                maxTableNameLength: 32);

            Assert.Throws<InvalidOperationException>(() =>
                LubanIntegration.LubanDataTableSetFactory.Create(
                    provider,
                    getBytes =>
                    {
                        getBytes("first");
                        return getBytes("second");
                    },
                    limits));
        }

        [Test]
        public void Create_RejectsPayloadCallbackAfterFactoryReturns()
        {
            var provider = new ArrayBytesProvider(new byte[] { 1 });
            Func<string, ByteBuf> escapedCallback = null;

            LubanIntegration.LubanDataTableSetFactory.Create(
                provider,
                getBytes =>
                {
                    escapedCallback = getBytes;
                    return getBytes("items");
                });

            Assert.That(escapedCallback, Is.Not.Null);
            Assert.Throws<InvalidOperationException>(() => escapedCallback("items"));
        }

        [Test]
        public void CreateOwnedByteBuf_NormalizesExtensionAndEnforcesNameLimit()
        {
            var provider = new RecordingBytesProvider(new byte[] { 5 });
            var limits = new DataTableLoadLimits(
                maxTableCount: 1,
                maxBytesPerTable: 8,
                maxTotalBytes: 8,
                maxRowsPerTable: 1,
                maxTableNameLength: 8);

            ByteBuf buffer = LubanIntegration.LubanDataTableSetFactory.CreateOwnedByteBuf(
                provider,
                "items.bytes",
                limits);

            Assert.That(provider.LastRequestedName, Is.EqualTo("items"));
            Assert.That(buffer.CopyData(), Is.EqualTo(new byte[] { 5 }));
        }

        private sealed class ArrayBytesProvider : IDataTableBytesProvider
        {
            private readonly byte[] _bytes;

            public ArrayBytesProvider(byte[] bytes)
            {
                _bytes = bytes;
            }

            public ReadOnlyMemory<byte> GetBytes(string tableName)
            {
                return _bytes;
            }

            public bool TryGetBytes(string tableName, out ReadOnlyMemory<byte> bytes)
            {
                bytes = _bytes;
                return true;
            }
        }

        private sealed class RecordingBytesProvider : IDataTableBytesProvider
        {
            private readonly byte[] _bytes;

            public RecordingBytesProvider(byte[] bytes)
            {
                _bytes = bytes;
            }

            public string LastRequestedName { get; private set; }

            public ReadOnlyMemory<byte> GetBytes(string tableName)
            {
                LastRequestedName = tableName;
                return _bytes;
            }

            public bool TryGetBytes(string tableName, out ReadOnlyMemory<byte> bytes)
            {
                LastRequestedName = tableName;
                bytes = _bytes;
                return true;
            }
        }
    }
}
