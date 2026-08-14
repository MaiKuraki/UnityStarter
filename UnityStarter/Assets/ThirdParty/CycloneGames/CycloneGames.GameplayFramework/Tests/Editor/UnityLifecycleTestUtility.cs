using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    internal static class UnityLifecycleTestUtility
    {
        public static void InvokeAwake(MonoBehaviour behaviour)
        {
            InvokeMessage(behaviour, "Awake");
        }

        public static void InvokeOnEnable(MonoBehaviour behaviour)
        {
            InvokeMessage(behaviour, "OnEnable");
        }

        public static void InvokeOnDisable(MonoBehaviour behaviour)
        {
            InvokeMessage(behaviour, "OnDisable");
        }

        public static void InvokeOnDestroy(MonoBehaviour behaviour)
        {
            InvokeMessage(behaviour, "OnDestroy");
        }

        private static void InvokeMessage(MonoBehaviour behaviour, string messageName)
        {
            if (behaviour == null)
            {
                throw new ArgumentNullException(nameof(behaviour));
            }

            Type currentType = behaviour.GetType();
            while (currentType != null && typeof(MonoBehaviour).IsAssignableFrom(currentType))
            {
                MethodInfo method = currentType.GetMethod(
                    messageName,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                if (method != null)
                {
                    try
                    {
                        method.Invoke(behaviour, null);
                        return;
                    }
                    catch (TargetInvocationException exception) when (exception.InnerException != null)
                    {
                        ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                        throw;
                    }
                }

                currentType = currentType.BaseType;
            }

            throw new MissingMethodException(
                behaviour.GetType().FullName,
                messageName);
        }
    }
}
