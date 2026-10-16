using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;

namespace CycloneGames.Networking.Serializer.NewtonsoftJson.Tests.Editor
{
    public sealed class NewtonsoftJsonSerializerAdapterTests
    {
        [Test]
        public void DefaultSettings_RoundTripStruct()
        {
            var adapter = new NewtonsoftJsonSerializerAdapter();
            var source = new TestMessage { Number = 42, Text = "network" };
            var buffer = new byte[512];

            adapter.Serialize(source, buffer, 0, out int writtenBytes);
            TestMessage result = adapter.Deserialize<TestMessage>(
                new ReadOnlySpan<byte>(buffer, 0, writtenBytes));

            Assert.AreEqual(source.Number, result.Number);
            Assert.AreEqual(source.Text, result.Text);
        }

        [Test]
        public void Constructor_RejectsNullSettings()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new NewtonsoftJsonSerializerAdapter(null));
        }

        [Test]
        public void Constructor_RejectsTypeHandlingWithoutBinder()
        {
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            };

            Assert.Throws<ArgumentException>(() => new NewtonsoftJsonSerializerAdapter(settings));
        }

        [Test]
        public void CreateWithTypeHandling_RejectsNullBinder()
        {
            Assert.Throws<ArgumentNullException>(() =>
                NewtonsoftJsonSerializerAdapter.CreateWithTypeHandling(null));
        }

        [Test]
        public void CreateWithTypeHandling_AllowListedPayloadRoundTrips()
        {
            var adapter = NewtonsoftJsonSerializerAdapter.CreateWithTypeHandling(new TestAllowListBinder());
            var source = new PolymorphicMessage
            {
                Payload = new AllowedPayload { Value = 17 }
            };
            var buffer = new byte[1024];

            adapter.Serialize(source, buffer, 0, out int writtenBytes);
            PolymorphicMessage result = adapter.Deserialize<PolymorphicMessage>(
                new ReadOnlySpan<byte>(buffer, 0, writtenBytes));

            Assert.IsInstanceOf<AllowedPayload>(result.Payload);
            Assert.AreEqual(17, ((AllowedPayload)result.Payload).Value);
        }

        [Test]
        public void MutatedSettings_CannotEnableTypeHandlingWithoutBinder()
        {
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.None
            };
            var adapter = new NewtonsoftJsonSerializerAdapter(settings);
            var buffer = new byte[512];
            var source = new TestMessage { Number = 1, Text = "unsafe" };
            settings.TypeNameHandling = TypeNameHandling.Auto;

            Assert.Throws<InvalidOperationException>(() =>
                adapter.Serialize(source, buffer, 0, out _));
        }

        [Test]
        public void WritePath_IsNotDepthLimited_ButReadPath_RejectsTheSamePayload()
        {
            // Json.NET enforces MaxDepth on the READ path only. The trust model is
            // asymmetric by design: outbound payloads come from our own trusted
            // object graph, while inbound bytes are hostile. This test locks that
            // contract: deep payloads must serialize, and the identical bytes must
            // be rejected on deserialization.
            var adapter = new NewtonsoftJsonSerializerAdapter();
            object node = 1;
            for (int i = 0; i < 100; i++)
            {
                node = new System.Collections.Generic.List<object> { node };
            }

            var source = new DeepNestingMessage { Payload = node };
            var buffer = new byte[8192];

            adapter.Serialize(source, buffer, 0, out int writtenBytes);
            Assert.Greater(writtenBytes, 0, "Trusted outbound payloads are not depth-limited by design.");

            Assert.Catch<JsonException>(() => adapter.Deserialize<DeepNestingMessage>(
                new ReadOnlySpan<byte>(buffer, 0, writtenBytes)),
                "The same bytes must be rejected on the trust-boundary side.");
        }

        [Test]
        public void Deserialize_RejectsPayloadNestedBeyondMaxDepth()
        {
            var adapter = new NewtonsoftJsonSerializerAdapter();
            var builder = new System.Text.StringBuilder("{\"Payload\":");
            for (int i = 0; i < 100; i++)
            {
                builder.Append('[');
            }

            for (int i = 0; i < 100; i++)
            {
                builder.Append(']');
            }

            builder.Append('}');
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(builder.ToString());

            Assert.Catch<JsonException>(() => adapter.Deserialize<DeepNestingMessage>(payload),
                "Nested-bomb payloads must be rejected on the read path, not merely on write.");
        }

        private struct DeepNestingMessage
        {
            public object Payload;
        }

        private struct TestMessage
        {
            public int Number;
            public string Text;
        }

        private struct PolymorphicMessage
        {
            public object Payload;
        }

        private sealed class AllowedPayload
        {
            public int Value;
        }

        private sealed class TestAllowListBinder : ISerializationBinder
        {
            public Type BindToType(string assemblyName, string typeName)
            {
                if (string.Equals(typeName, typeof(AllowedPayload).FullName, StringComparison.Ordinal)
                    && string.Equals(assemblyName, typeof(AllowedPayload).Assembly.GetName().Name, StringComparison.Ordinal))
                {
                    return typeof(AllowedPayload);
                }

                throw new JsonSerializationException("The requested type is not part of the test protocol allow-list.");
            }

            public void BindToName(Type serializedType, out string assemblyName, out string typeName)
            {
                if (serializedType != typeof(AllowedPayload))
                {
                    throw new JsonSerializationException("The requested type is not part of the test protocol allow-list.");
                }

                assemblyName = serializedType.Assembly.GetName().Name;
                typeName = serializedType.FullName;
            }
        }
    }
}
