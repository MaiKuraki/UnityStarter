using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Serialization
{
    public static class SerializerFactory
    {
        private static readonly object _syncRoot = new object();
        private static readonly Dictionary<SerializerType, Func<INetSerializer>> _creators =
            new Dictionary<SerializerType, Func<INetSerializer>>();
        private static Func<INetSerializer> _defaultCreator;
        private static bool _frozen;

        public static bool IsFrozen
        {
            get
            {
                lock (_syncRoot)
                    return _frozen;
            }
        }

        public static void RegisterCreator(SerializerType type, Func<INetSerializer> creator)
        {
            if (creator == null)
                throw new ArgumentNullException(nameof(creator));

            lock (_syncRoot)
            {
                ThrowIfFrozen();

                _creators[type] = creator;
                if (_defaultCreator == null && type == SerializerType.Json)
                    _defaultCreator = creator;
            }
        }

        public static void SetDefaultCreator(Func<INetSerializer> creator)
        {
            if (creator == null)
                throw new ArgumentNullException(nameof(creator));

            lock (_syncRoot)
            {
                ThrowIfFrozen();
                _defaultCreator = creator;
            }
        }

        public static INetSerializer Create(SerializerType type)
        {
            Func<INetSerializer> creator;
            lock (_syncRoot)
            {
                if (!_creators.TryGetValue(type, out creator))
                {
                    throw new InvalidOperationException(
                        $"Serializer '{type}' is not registered. Call SerializerFactory.RegisterCreator({type}, ...) during initialization.");
                }
            }

            INetSerializer serializer = creator();
            if (serializer != null)
                return serializer;

            throw new InvalidOperationException($"Serializer creator for '{type}' returned null.");
        }

        public static bool TryCreate(SerializerType type, out INetSerializer serializer)
        {
            Func<INetSerializer> creator;
            lock (_syncRoot)
            {
                if (!_creators.TryGetValue(type, out creator))
                {
                    serializer = null;
                    return false;
                }
            }

            serializer = creator();
            return serializer != null;
        }

        public static bool IsAvailable(SerializerType type)
        {
            lock (_syncRoot)
                return _creators.ContainsKey(type);
        }

        public static INetSerializer GetRecommended()
        {
            Func<INetSerializer> creator;
            lock (_syncRoot)
            {
                if (!_creators.TryGetValue(SerializerType.MessagePack, out creator) &&
                    !_creators.TryGetValue(SerializerType.NewtonsoftJson, out creator))
                {
                    creator = _defaultCreator;
                }
            }

            if (creator != null)
            {
                INetSerializer serializer = creator();
                if (serializer != null)
                    return serializer;
            }

            return GetDefault();
        }

        public static INetSerializer GetDefault()
        {
            Func<INetSerializer> creator;
            lock (_syncRoot)
            {
                creator = _defaultCreator;
            }

            if (creator != null)
            {
                INetSerializer serializer = creator();
                if (serializer != null)
                    return serializer;
            }

            throw new InvalidOperationException(
                "No default serializer registered. Call SerializerFactory.SetDefaultCreator(...) or RegisterCreator(SerializerType.Json, ...) during initialization.");
        }

        public static void Freeze()
        {
            lock (_syncRoot)
                _frozen = true;
        }

        public static void Reset()
        {
            lock (_syncRoot)
            {
                _creators.Clear();
                _defaultCreator = null;
                _frozen = false;
            }
        }

        private static void ThrowIfFrozen()
        {
            if (_frozen)
                throw new InvalidOperationException("SerializerFactory is frozen. Register serializers before runtime startup, or call Reset during teardown.");
        }
    }
}
