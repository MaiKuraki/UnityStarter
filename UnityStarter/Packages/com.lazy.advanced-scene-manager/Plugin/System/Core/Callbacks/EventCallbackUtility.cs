using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Core.Callbacks;
using AdvancedSceneManager.Utility;

namespace AdvancedSceneManager.Callbacks.Events
{

    /// <summary>Provides utility functions for working with event callbacks.</summary>
    public static class EventCallbackUtility
    {

        [AttributeUsage(AttributeTargets.Class)]
        public class CalledForAttribute : Attribute
        {

            public When[] When { get; }

            public CalledForAttribute(params When[] when)
            {
                When = when;
            }

        }

        /// <summary>Enumerates all callback types.</summary>
        public static IEnumerable<Type> GetCallbackTypes() =>
            TypeUtility.FindSubclasses<SceneOperationEventBase>().Where(t => !t.IsAbstract);

        /// <summary>Gets if the callback is called for the <see cref="When"/> enum value.</summary>
        /// <param name="when">Then <see cref="When"/> enum value.</param>
        public static bool IsCalledFor<TEventType>(When when) where TEventType : SceneOperationEventBase, new() =>
            typeof(TEventType).GetCustomAttribute<CalledForAttribute>()?.When.Contains(when) ?? false;

        /// <summary>Gets if the callback is called for the <see cref="When"/> enum value.</summary>
        /// <param name="when">Then <see cref="When"/> enum value.</param>
        public static bool IsCalledFor(Type type, When when) =>
            type.GetCustomAttribute<CalledForAttribute>()?.When.Contains(when) ?? false;

        /// <summary>Registers callback for all events.</summary>
        public static SceneOperation RegisterAllCallbacks(SceneOperation operation, EventCallback<SceneOperationEventBase> callback, When? when = null)
        {

            foreach (var type in GetCallbackTypes())
            {
                var method = typeof(SceneOperation).GetMethod(nameof(SceneOperation.RegisterCallback)).MakeGenericMethod(type);
                method.Invoke(operation, new object[] { callback, when });
            }

            return operation;

        }
    }

}
