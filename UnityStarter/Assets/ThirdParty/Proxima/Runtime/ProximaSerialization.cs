using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Proxima
{
    [Serializable]
    internal enum ProximaRequestType
    {
        Command = 0,
        StartStream = 1,
        StopStream = 2,
        List = 3,
        Select = 4
    }

    [Serializable]
    internal class ProximaRequest
    {
        public string Id;
        public ProximaRequestType Type;
        public string Cmd;
        public string[] Args;
    }

    [Serializable]
    internal class ProximaDataResponse
    {
        public string Id;
        public object Data;
    }

    [Serializable]
    internal class ProximaErrorResponse
    {
        public string Id;
        public string Error;
    }

    [System.Serializable]
    internal struct ProximaInstanceHello
    {
        public string DisplayName;
        public string InstanceId;
        public string ProductName;
        public string CompanyName;
        public string Platform;
        public string ProductVersion;
        public string ProximaVersion;
        public string ConnectionId;
        public string Password;
    }

    public static class ProximaSerialization
    {
        internal static readonly string VERSION = "1.0.0";

        public static string Serialize(object data, bool pretty = false)
        {
            var stream = FastJson.Serialize(data, pretty);
            if (stream == null)
            {
                return null;
            }

            return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
        }

        internal static MemoryStream DataResponse(ProximaRequest request, object data)
        {
            return DataResponse(request.Id, data);
        }

        internal static MemoryStream DataResponse(string id, object data)
        {
            return FastJson.Serialize(new ProximaDataResponse { Id = id, Data = data });
        }

        internal static MemoryStream ErrorResponse(ProximaRequest request, string error)
        {
            return ErrorResponse(request.Id, error);
        }

        internal static MemoryStream ErrorResponse(string id, string error)
        {
            return FastJson.Serialize(new ProximaErrorResponse { Id = id, Error = error });
        }

        internal static string HashPassword(string password, string connectionId)
        {
            SHA256 hash = SHA256.Create();
            byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(password + connectionId));
            var hashedPassword = Convert.ToBase64String(bytes);
            return hashedPassword;
        }

        internal static ProximaInstanceHello CreateHello(string displayName, string password = "")
        {
            var hello = new ProximaInstanceHello();
            hello.DisplayName = displayName;
            hello.InstanceId = displayName.GetHashCode().ToString() + "_" + VERSION;
            hello.ProductName = Application.productName;
            hello.CompanyName = Application.companyName;
            hello.Platform = Enum.GetName(typeof(RuntimePlatform), Application.platform);
            hello.ProductVersion = Application.version;
            hello.ProximaVersion = VERSION;
            hello.ConnectionId = Guid.NewGuid().ToString();

            if (!string.IsNullOrEmpty(password))
            {
                hello.Password = HashPassword(password, hello.ConnectionId);
            }

            return hello;
        }

        private static bool TrySplitArray(string arrayString, out List<string> elements)
        {
            elements = null;

            if (arrayString.Length < 2)
            {
                return false;
            }

            if (arrayString[0] != '[')
            {
                return false;
            }

            if (arrayString[arrayString.Length - 1] != ']')
            {
                return false;
            }

            elements = new List<string>();
            int b = 0;
            bool q = false;
            bool bs = false;
            var sb = new StringBuilder();
            for (int i = 1; i < arrayString.Length - 1; i++)
            {
                var c = arrayString[i];
                if (!q && b == 0 && c == ',') {
                    elements.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(c);

                if (bs)
                {
                    bs = false;
                }
                else if (c == '\\')
                {
                    bs = true;
                }
                else if (c == '"')
                {
                    q = !q;
                }
                else if (c == '[')
                {
                    b++;
                }
                else if (c == ']')
                {
                    b--;
                }
            }

            if (sb.Length > 0)
            {
                elements.Add(sb.ToString());
            }

            return true;
        }

        internal static IList MakeListOf(Type elementType)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
            return list;
        }

        internal static IList MakeList(Type listType)
        {
            return MakeListOf(listType.GetGenericArguments()[0]);
        }

        internal static IList CopyList(IList list)
        {
            var newList = MakeList(list.GetType());
            foreach (var item in list)
            {
                newList.Add(item);
            }

            return newList;
        }

        internal static Array MakeArrayOf(Type elementType, int length)
        {
            return Array.CreateInstance(elementType, length);
        }

        internal static Array MakeArray(Type arrayType, int length)
        {
            return MakeArrayOf(arrayType.GetElementType(), length);
        }

        internal static Array CopyArray(Array array)
        {
            var newArray = MakeArray(array.GetType(), array.Length);
            Array.Copy(array, newArray, array.Length);
            return newArray;
        }

        internal static Array ResizeArray(Array array, int size)
        {
            var newArray = MakeArray(array.GetType(), size);
            Array.Copy(array, newArray, Math.Min(array.Length, size));
            return newArray;
        }

        private static bool TryDeserializeList(Type listType, string arrayString, out IList values)
        {
            if (!TrySplitArray(arrayString, out var elements))
            {
                values = null;
                return false;
            }

            var elementType = listType.GetGenericArguments()[0];
            values = MakeListOf(elementType);
            for (int i = 0; i < elements.Count; i++)
            {
                if (!TryDeserialize(elementType, elements[i].Trim('"'), out var obj))
                {
                    values = null;
                    return false;
                }

                values.Add(obj);
            }

            return true;
        }

        private static bool TryDeserializeArray(Type elementType, string arrayString, out Array values)
        {
            if (!TrySplitArray(arrayString, out var elements))
            {
                values = null;
                return false;
            }

            values = MakeArrayOf(elementType, elements.Count);
            for (int i = 0; i < elements.Count; i++)
            {
                if (!TryDeserialize(elementType, elements[i].Trim('"'), out var obj))
                {
                    values = null;
                    return false;
                }

                values.SetValue(obj, i);
            }

            return true;
        }

        private static bool TryDeserializeArray<T>(string arrayString, out T[] values)
        {
            if (TryDeserializeArray(typeof(T), arrayString, out var array))
            {
                values = (T[])array;
                return true;
            }

            values = null;
            return false;
        }

        private static bool TryDeserializeArray<T>(string arrayString, int count, out T[] values)
        {
            if (!TryDeserializeArray<T>(arrayString, out values))
            {
                return false;
            }

            if (values.Length != count)
            {
                values = null;
                return false;
            }

            return true;
        }

        internal static bool TryDeserialize<T>(string value, out T result)
        {
            if (TryDeserialize(typeof(T), value, out var obj))
            {
                result = (T)obj;
                return true;
            }

            result = default(T);
            return false;
        }

        // This does NOT do full JSON deserialization. It just deserializes single values
        // or arrays of values, which is all the inspector needs.
        internal static bool TryDeserialize(Type type, string value, out object result)
        {
            if (type == typeof(string))
            {
                result = value;
                return true;
            }
            else if (type == typeof(float))
            {
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
                {
                    result = floatValue;
                    return true;
                }
            }
            else if (type == typeof(double))
            {
                if (double.TryParse(value, out var doubleValue))
                {
                    result = doubleValue;
                    return true;
                }
            }
            else if (type == typeof(byte))
            {
                if (byte.TryParse(value, out var byteValue))
                {
                    result = byteValue;
                    return true;
                }
            }
            else if (type == typeof(sbyte))
            {
                if (sbyte.TryParse(value, out var sbyteValue))
                {
                    result = sbyteValue;
                    return true;
                }
            }
            else if (type == typeof(short))
            {
                if (short.TryParse(value, out var shortValue))
                {
                    result = shortValue;
                    return true;
                }
            }
            else if (type == typeof(ushort))
            {
                if (ushort.TryParse(value, out var ushortValue))
                {
                    result = ushortValue;
                    return true;
                }
            }
            else if (type == typeof(int))
            {
                if (int.TryParse(value, out var intValue))
                {
                    result = intValue;
                    return true;
                }
            }
            else if (type == typeof(uint))
            {
                if (uint.TryParse(value, out var uintValue))
                {
                    result = uintValue;
                    return true;
                }
            }
            else if (type == typeof(long))
            {
                if (long.TryParse(value, out var longValue))
                {
                    result = longValue;
                    return true;
                }
            }
            else if (type == typeof(ulong))
            {
                if (ulong.TryParse(value, out var ulongValue))
                {
                    result = ulongValue;
                    return true;
                }
            }
            else if (type == typeof(bool))
            {
                result = (value == "true");
                return true;
            }
            else if (type == typeof(Vector2))
            {
                if (TryDeserializeArray<float>(value, 2, out var v))
                {
                    result = new Vector2(v[0], v[1]);
                    return true;
                }
            }
            else if (type == typeof(Vector3))
            {
                if (TryDeserializeArray<float>(value, 3, out var v))
                {
                    result = new Vector3(v[0], v[1], v[2]);
                    return true;
                }
            }
            else if (type == typeof(Vector4))
            {
                if (TryDeserializeArray<float>(value, 4, out var v))
                {
                    result = new Vector4(v[0], v[1], v[2], v[3]);
                    return true;
                }
            }
            else if (type == typeof(Vector2Int))
            {
                if (TryDeserializeArray<int>(value, 2, out var v))
                {
                    result = new Vector2Int(v[0], v[1]);
                    return true;
                }
            }
            else if (type == typeof(Vector3Int))
            {
                if (TryDeserializeArray<int>(value, 3, out var v))
                {
                    result = new Vector3Int(v[0], v[1], v[2]);
                    return true;
                }
            }
            else if (type == typeof(Quaternion))
            {
                if (TryDeserializeArray<float>(value, 3, out var v))
                {
                    result = Quaternion.Euler(v[0], v[1], v[2]);
                    return true;
                }
            }
            else if (type == typeof(Rect))
            {
                if (TryDeserializeArray<float>(value, 4, out var v))
                {
                    result = new Rect(v[0], v[1], v[2], v[3]);
                    return true;
                }
            }
            else if (type == typeof(RectInt))
            {
                if (TryDeserializeArray<int>(value, 4, out var v))
                {
                    result = new RectInt(v[0], v[1], v[2], v[3]);
                    return true;
                }
            }
            else if (type == typeof(Bounds))
            {
                if (TryDeserializeArray<float>(value, 6, out var v))
                {
                    result = new Bounds(new Vector3(v[0], v[1], v[2]), new Vector3(v[3], v[4], v[5]));
                    return true;
                }
            }
            else if (type == typeof(BoundsInt))
            {
                if (TryDeserializeArray<int>(value, 6, out var v))
                {
                    result = new BoundsInt(new Vector3Int(v[0], v[1], v[2]), new Vector3Int(v[3], v[4], v[5]));
                    return true;
                }
            }
            else if (type == typeof(Color))
            {
                if (ColorUtility.TryParseHtmlString(value, out var color))
                {
                    result = color;
                    return true;
                }
            }
            else if (type == typeof(LayerMask))
            {
                if (int.TryParse(value, out var intValue))
                {
                    result = new LayerMask() { value = intValue };
                    return true;
                }
            }
            else if (type.IsEnum)
            {
                if (int.TryParse(value, out var enumValue))
                {
                    result = enumValue;
                    return true;
                }
            }
            else if (type.IsArray)
            {
                if (TryDeserializeArray(type.GetElementType(), value, out var array))
                {
                    result = array;
                    return true;
                }
            }
            else if (typeof(IList).IsAssignableFrom(type))
            {
                if (TryDeserializeList(type, value, out var list))
                {
                    result = list;
                    return true;
                }
            }

            result = null;
            return false;
        }

        internal static object Deserialize(Type t, string value)
        {
            if (TryDeserialize(t, value, out var result))
            {
                return result;
            }

            throw new Exception("Failed to deserialize value: " + value + " as type: " + t.Name);
        }

        internal static T Deserialize<T>(string value)
        {
            return (T)Deserialize(typeof(T), value);
        }

        private static List<Type> _nativeFlags = new List<Type>() {
#if UNITY_PHYSICS
            typeof(RigidbodyConstraints)
#endif
        };

        internal static string GetSerializedTypeName(Type propertyType)
        {
            if (propertyType.IsEnum)
            {
                var sb = new StringBuilder();
                var values = Enum.GetValues(propertyType);
                var names = propertyType.GetEnumNames();
                var underlyingType = Enum.GetUnderlyingType(propertyType);

                bool nativeFlag = _nativeFlags.Contains(propertyType);
                if (nativeFlag || propertyType.GetCustomAttribute(typeof(FlagsAttribute)) != null)
                {
                    sb.Append("Flags");
                }
                else
                {
                    sb.Append("Enum");
                }

                if (values.Length > 0)
                {
                    sb.Append(" ");
                }

                for (int i = 0; i < values.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(",");
                    }

                    sb.Append(names[i]);
                    sb.Append(":");
                    sb.Append(Convert.ChangeType(values.GetValue(i), underlyingType));
                }

                return sb.ToString();
            }

            if (propertyType.IsArray)
            {
                return "Array " + GetSerializedTypeName(propertyType.GetElementType());
            }

            if (typeof(IList).IsAssignableFrom(propertyType))
            {
                return "Array " + GetSerializedTypeName(propertyType.GetGenericArguments()[0]);
            }

            if (propertyType.IsSubclassOf(typeof(UnityEngine.Object)))
            {
                return "Object " + propertyType.Name;
            }

            return propertyType.Name;
        }
    }
}