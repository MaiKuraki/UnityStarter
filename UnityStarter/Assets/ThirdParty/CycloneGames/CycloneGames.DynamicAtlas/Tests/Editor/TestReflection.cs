using System;
using System.Reflection;

namespace CycloneGames.DynamicAtlas.Tests.Editor
{
    /// <summary>
    /// Reflection helpers for reaching private members under test. Kept local to this package so the
    /// test assembly has no dependency on another package's test infrastructure.
    /// </summary>
    internal static class TestReflection
    {
        public static void SetField(object target, string fieldName, object value)
        {
            Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }

                type = type.BaseType;
            }

            throw new MissingFieldException(target.GetType().FullName, fieldName);
        }

        public static object GetField(object target, string fieldName)
        {
            Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field.GetValue(target);
                }

                type = type.BaseType;
            }

            throw new MissingFieldException(target.GetType().FullName, fieldName);
        }

        public static object Invoke(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(target.GetType().FullName, methodName);
            }

            return method.Invoke(target, arguments);
        }
    }
}
