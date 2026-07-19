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
