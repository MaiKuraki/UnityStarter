using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Serialization
{
    public static class SerializerFactory
    {
        private static readonly Dictionary<SerializerType, Func<INetSerializer>> _creators =
            new Dictionary<SerializerType, Func<INetSerializer>>();
        private static Func<INetSerializer> _defaultCreator;

        public static void RegisterCreator(SerializerType type, Func<INetSerializer> creator)
        {
            _creators[type] = creator;
            if (_defaultCreator == null && type == SerializerType.Json)
                _defaultCreator = creator;
        }

        public static void SetDefaultCreator(Func<INetSerializer> creator)
        {
            _defaultCreator = creator;
        }

        public static INetSerializer Create(SerializerType type)
        {
            if (_creators.TryGetValue(type, out var creator))
                return creator();

            throw new InvalidOperationException(
                $"Serializer '{type}' is not registered. Call SerializerFactory.RegisterCreator({type}, ...) during initialization.");
        }

        public static bool IsAvailable(SerializerType type)
        {
            return _creators.ContainsKey(type);
        }

        public static INetSerializer GetRecommended()
        {
            if (_creators.TryGetValue(SerializerType.MessagePack, out var mp))
                return mp();
            if (_creators.TryGetValue(SerializerType.NewtonsoftJson, out var nj))
                return nj();
            return GetDefault();
        }

        public static INetSerializer GetDefault()
        {
            if (_defaultCreator != null)
                return _defaultCreator();

            throw new InvalidOperationException(
                "No default serializer registered. Call SerializerFactory.SetDefaultCreator(...) or RegisterCreator(SerializerType.Json, ...) during initialization.");
        }
    }
}