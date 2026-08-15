using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Validates pipeline-owned JSON as one exact current document shape.
    /// Unknown members, duplicate keys, comments, and trailing content are rejected.
    /// </summary>
    internal static class BuildJsonDocumentContract
    {
        private const int MaximumDepth = 64;

        internal static void Validate<T>(
            string json,
            string expectedDocumentType,
            string description)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new InvalidOperationException(
                    $"{description} is empty.");
            }

            if (string.IsNullOrWhiteSpace(expectedDocumentType))
            {
                throw new ArgumentException(
                    "A current document type is required.",
                    nameof(expectedDocumentType));
            }

            JToken token;
            try
            {
                RejectComments(json);
                using (var textReader = new System.IO.StringReader(json))
                using (var reader = new JsonTextReader(textReader)
                       {
                           DateParseHandling = DateParseHandling.None,
                           FloatParseHandling = FloatParseHandling.Decimal,
                           MaxDepth = MaximumDepth
                       })
                {
                    token = JToken.ReadFrom(
                        reader,
                        new JsonLoadSettings
                        {
                            CommentHandling = CommentHandling.Ignore,
                            DuplicatePropertyNameHandling =
                                DuplicatePropertyNameHandling.Error,
                            LineInfoHandling = LineInfoHandling.Ignore
                        });
                    if (reader.Read())
                    {
                        throw new JsonReaderException(
                            "Trailing JSON content is not allowed.");
                    }
                }
            }
            catch (Exception exception) when (
                exception is JsonException
                || exception is ArgumentException
                || exception is InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"{description} is not strict current-contract JSON.",
                    exception);
            }

            if (!(token is JObject root))
            {
                throw new InvalidOperationException(
                    $"{description} must be a JSON object.");
            }

            ValidateShape(root, typeof(T), "$", description);
            JToken documentType = root["documentType"];
            if (documentType == null
                || documentType.Type != JTokenType.String
                || !string.Equals(
                    documentType.Value<string>(),
                    expectedDocumentType,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{description} does not match document type '{expectedDocumentType}'.");
            }
        }

        private static void RejectComments(string json)
        {
            using (var textReader = new System.IO.StringReader(json))
            using (var reader = new JsonTextReader(textReader)
                   {
                       DateParseHandling = DateParseHandling.None,
                       MaxDepth = MaximumDepth
                   })
            {
                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.Comment)
                    {
                        throw new JsonReaderException(
                            "JSON comments are not allowed.");
                    }
                }
            }
        }

        private static void ValidateShape(
            JToken token,
            Type declaredType,
            string path,
            string description)
        {
            if (token.Type == JTokenType.Null)
            {
                if (declaredType.IsValueType
                    && Nullable.GetUnderlyingType(declaredType) == null)
                {
                    throw new InvalidOperationException(
                        $"{description} contains null for value field '{path}'.");
                }

                return;
            }

            Type type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
            if (type == typeof(string))
            {
                RequireTokenType(token, JTokenType.String, path, description);
                return;
            }

            if (type == typeof(bool))
            {
                RequireTokenType(token, JTokenType.Boolean, path, description);
                return;
            }

            if (type.IsEnum || IsInteger(type))
            {
                RequireTokenType(token, JTokenType.Integer, path, description);
                return;
            }

            if (IsFloatingPoint(type))
            {
                if (token.Type != JTokenType.Integer
                    && token.Type != JTokenType.Float)
                {
                    throw CreateTypeException(path, description, "number", token.Type);
                }

                return;
            }

            Type elementType = ResolveElementType(type);
            if (elementType != null)
            {
                if (!(token is JArray array))
                {
                    throw CreateTypeException(path, description, "array", token.Type);
                }

                for (int index = 0; index < array.Count; index++)
                {
                    ValidateShape(
                        array[index],
                        elementType,
                        path + "[" + index + "]",
                        description);
                }

                return;
            }

            if (!(token is JObject value))
            {
                throw CreateTypeException(path, description, "object", token.Type);
            }

            var fields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            foreach (FieldInfo field in type.GetFields(
                         BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.IsNotSerialized)
                {
                    continue;
                }

                fields.Add(field.Name, field);
            }

            foreach (JProperty property in value.Properties())
            {
                if (!fields.TryGetValue(property.Name, out FieldInfo field))
                {
                    throw new InvalidOperationException(
                        $"{description} contains unknown field '{path}.{property.Name}'.");
                }

                ValidateShape(
                    property.Value,
                    field.FieldType,
                    path + "." + property.Name,
                    description);
            }
        }

        private static Type ResolveElementType(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }

            if (type != typeof(string)
                && typeof(IEnumerable).IsAssignableFrom(type)
                && type.IsGenericType)
            {
                Type[] arguments = type.GetGenericArguments();
                if (arguments.Length == 1)
                {
                    return arguments[0];
                }
            }

            return null;
        }

        private static bool IsInteger(Type type)
        {
            return type == typeof(byte)
                   || type == typeof(sbyte)
                   || type == typeof(short)
                   || type == typeof(ushort)
                   || type == typeof(int)
                   || type == typeof(uint)
                   || type == typeof(long)
                   || type == typeof(ulong);
        }

        private static bool IsFloatingPoint(Type type)
        {
            return type == typeof(float)
                   || type == typeof(double)
                   || type == typeof(decimal);
        }

        private static void RequireTokenType(
            JToken token,
            JTokenType expected,
            string path,
            string description)
        {
            if (token.Type != expected)
            {
                throw CreateTypeException(
                    path,
                    description,
                    expected.ToString(),
                    token.Type);
            }
        }

        private static InvalidOperationException CreateTypeException(
            string path,
            string description,
            string expected,
            JTokenType actual)
        {
            return new InvalidOperationException(
                $"{description} field '{path}' must be {expected}; found {actual}.");
        }
    }
}
