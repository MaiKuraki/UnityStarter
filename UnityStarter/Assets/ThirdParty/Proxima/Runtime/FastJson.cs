using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Proxima
{
    [AttributeUsage(AttributeTargets.Field)]
    internal class SerializeIfAttribute : Attribute
    {
        public string Method;

        public SerializeIfAttribute(string method)
        {
            Method = method;
        }
    }

    internal class FastJson
    {
        private static bool _pretty = false;
        private static MemoryStream _stream;
        public static Dictionary<Type, List<FieldInfo>> _typeToObjectFields = new Dictionary<Type, List<FieldInfo>>(100);

        public static MemoryStream Serialize(object data, bool pretty = false)
        {
            if (data == null)
            {
                return null;
            }

            _pretty = pretty;
            _stream = new MemoryStream(1024);

            try
            {
                SerializeRecursively(data.GetType(), data, false);
                _stream.Position = 0;
                return _stream;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }

        private static void SerializeRecursively(Type type, object data, bool quote)
        {
            if (TrySerializeValue(type, data, quote))
            {
                return;
            }

            if (typeof(IList).IsAssignableFrom(type))
            {
                SerializeList(data);
                return;
            }

            if (type.IsArray)
            {
                SerializeArray(data);
                return;
            }

            if (type.IsSerializable)
            {
                SerializeObject(data);
                return;
            }

            if (typeof(IEnumerable).IsAssignableFrom(type))
            {
                SerializeEnumerable(data);
                return;
            }

            throw new Exception($"Unsupported type {type}");
        }

        private static bool TrySerializeValue(Type type, object data, bool quote)
        {
            switch (type)
            {
                case Type t when t == typeof(bool): SerializeBool(data); break;
                case Type t when t == typeof(byte): WriteUInt64((ulong)(byte)data); break;
                case Type t when t == typeof(sbyte): WriteInt64((long)(sbyte)data); break;
                case Type t when t == typeof(short): WriteInt64((long)(short)data); break;
                case Type t when t == typeof(ushort): WriteUInt64((ulong)(ushort)data); break;
                case Type t when t == typeof(int): WriteInt64((long)(int)data); break;
                case Type t when t == typeof(uint): WriteUInt64((ulong)(uint)data); break;
                case Type t when t == typeof(long): WriteInt64((long)data); break;
                case Type t when t == typeof(ulong): WriteUInt64((ulong)data); break;
                case Type t when t == typeof(float): SerializeFloat(data); break;
                case Type t when t == typeof(double): SerializeDouble(data); break;
                case Type t when t == typeof(string): SerializeString(data, quote); break;
                case Type t when t == typeof(Vector2): SerializeVector2(data); break;
                case Type t when t == typeof(Vector3): SerializeVector3(data); break;
                case Type t when t == typeof(Vector4): SerializeVector4(data); break;
                case Type t when t == typeof(Vector2Int): SerializeVector2Int(data); break;
                case Type t when t == typeof(Vector3Int): SerializeVector3Int(data); break;
                case Type t when t == typeof(Quaternion): SerializeQuaternion(data); break;
                case Type t when t == typeof(Rect): SerializeRect(data); break;
                case Type t when t == typeof(RectInt): SerializeRectInt(data); break;
                case Type t when t == typeof(Bounds): SerializeBounds(data); break;
                case Type t when t == typeof(BoundsInt): SerializeBoundsInt(data); break;
                case Type t when t == typeof(Color): SerializeColor(data, quote); break;
                case Type t when t == typeof(LayerMask): SerializeLayerMask(data); break;
                case Type t when t.IsEnum: SerializeEnum(data); break;
                case Type t when t.IsSubclassOf(typeof(UnityEngine.Object)): SerializeUnityObject(type, data, quote); break;
                default: return false;
            }

            return true;
        }

        private static void Write(char value)
        {
            _stream.WriteByte((byte)value);
        }

        private static void Write(string value, int offset, int count)
        {
            var encoding = System.Text.Encoding.UTF8;
            var startLength = (int)_stream.Length;
            _stream.SetLength(_stream.Length + encoding.GetMaxByteCount(count));
            var written = encoding.GetBytes(value, offset, count, _stream.GetBuffer(), startLength);
            _stream.SetLength(startLength + written);
            _stream.Seek(0, SeekOrigin.End);
        }

        private static void Write(string value)
        {
            Write(value, 0, value.Length);
        }

        private static void SerializeBool(object data)
        {
            SerializeBool(data != null ? (bool)data : false);
        }

        private static void SerializeBool(bool data)
        {
            if (data)
            {
                Write("true");
            }
            else
            {
                Write("false");
            }
        }

        private static void SerializeFloat(object data)
        {
            SerializeFloat(data != null ? (float)data : 0f);
        }

        private static void SerializeFloat(float data)
        {
            if (float.IsNaN(data))
            {
                Write('0');
                return;
            }

            if (float.IsInfinity(data))
            {
                Write("\"Infinity\"");
                return;
            }

            if (float.IsNegativeInfinity(data))
            {
                Write("\"-Infinity\"");
                return;
            }

            var value = data;
            Write(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void SerializeDouble(object data)
        {
            SerializeDouble(data != null ? (double)data : 0d);
        }

        private static void SerializeDouble(double data)
        {
            if (double.IsNaN(data))
            {
                Write('0');
                return;
            }

            if (double.IsInfinity(data))
            {
                Write("\"Infinity\"");
                return;
            }

            if (double.IsNegativeInfinity(data))
            {
                Write("\"-Infinity\"");
                return;
            }

            var value = data;
            Write(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void SerializeString(object data, bool quote)
        {
            var value = (string)data;
            if (quote)
            {
                Write('"');
            }

            if (String.IsNullOrEmpty(value))
            {
                if (quote)
                {
                    Write('"');
                }

                return;
            }

            int len = value.Length;
            bool needEncode = false;
            char c;
            for (int i = 0; i < len; i++)
            {
                c = value[i];

                if (c >= 0 && c <= 31 || c == 34 || c == 39 || c == 60 || c == 62 || c == 92)
                {
                    needEncode = true;
                    break;
                }
            }

            if (!needEncode)
            {
                Write(value);
                if (quote)
                {
                    Write('"');
                }

                return;
            }

            int start = 0;
            for (int i = 0; i < len; i++)
            {
                c = value[i];
                string escaped = null;
                if (c >= 0 && c <= 7 || c == 11 || c >= 14 && c <= 31 || c == 39 || c == 60 || c == 62)
                {
                    escaped = string.Format("\\u{0:x4}", (int)c);
                }
                else switch ((int)c)
                {
                    case 8:
                        escaped = "\\b";
                        break;
                    case 9:
                        escaped = "\\t";
                        break;
                    case 10:
                        escaped = "\\n";
                        break;
                    case 12:
                        escaped = "\\f";
                        break;
                    case 13:
                        escaped = "\\r";
                        break;
                    case 34:
                        escaped = "\\\"";
                        break;
                    case 92:
                        escaped = "\\\\";
                        break;
                }

                if (escaped != null)
                {
                    if (start < i)
                    {
                        Write(value.Substring(start, i - start));
                    }

                    Write(escaped);
                    start = i + 1;
                }
            }

            if (start < len)
            {
                Write(value.Substring(start));
            }

            if (quote)
            {
                Write('"');
            }
        }

        private static void SerializeParams<T>(params T[] values)
        {
            SerializeList(values);
        }

        private static void SerializeVector2(object data)
        {
            var v = (data != null) ? (Vector2)data : Vector2.zero;
            SerializeParams(v.x, v.y);
        }

        private static void SerializeVector3(object data)
        {
            var v = (data != null) ? (Vector3)data : Vector3.zero;
            SerializeParams(v.x, v.y, v.z);
        }

        private static void SerializeVector4(object data)
        {
            var v = (data != null) ? (Vector4)data : Vector4.zero;
            SerializeParams(v.x, v.y, v.z, v.w);
        }

        private static void SerializeVector2Int(object data)
        {
            var v = (data != null) ? (Vector2Int)data : Vector2Int.zero;
            SerializeParams(v.x, v.y);
        }

        private static void SerializeVector3Int(object data)
        {
            var v = (data != null) ? (Vector3Int)data : Vector3Int.zero;
            SerializeParams(v.x, v.y, v.z);
        }

        private static void SerializeQuaternion(object data)
        {
            var euler = (data != null) ? ((Quaternion)data).eulerAngles : Vector3.zero;
            SerializeVector3(euler);
        }

        private static void SerializeRect(object data)
        {
            var r = (data != null) ? (Rect)data : Rect.zero;
            SerializeParams(r.x, r.y, r.width, r.height);
        }

        private static void SerializeRectInt(object data)
        {
            var r = (data != null) ? (RectInt)data : new RectInt(0, 0, 0, 0);
            SerializeParams(r.x, r.y, r.width, r.height);
        }

        private static void SerializeBounds(object data)
        {
            var b = (data != null) ? (Bounds)data : new Bounds(Vector3.zero, Vector3.zero);
            SerializeParams(b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z);
        }

        private static void SerializeBoundsInt(object data)
        {
            var b = (data != null) ? (BoundsInt)data : new BoundsInt(Vector3Int.zero, Vector3Int.zero);
            SerializeParams(b.position.x, b.position.y, b.position.z, b.size.x, b.size.y, b.size.z);
        }

        private static void WriteHex(byte value)
        {
            byte high = (byte)(value >> 4);
            byte low = (byte)(value & 15);
            Write((char)(high < 10 ? high + 48 : high + 55));
            Write((char)(low < 10 ? low + 48 : low + 55));
        }

        private static void SerializeColor(object data, bool quote)
        {
            var color = data != null ? (Color)data : Color.black;
            if (quote)
            {
                Write('"');
            }

            Write('#');
            WriteHex((byte)(color.r * 255));
            WriteHex((byte)(color.g * 255));
            WriteHex((byte)(color.b * 255));
            WriteHex((byte)(color.a * 255));

            if (quote)
            {
                Write('"');
            }
        }

        private static void SerializeLayerMask(object data)
        {
            var value = (LayerMask)data;
            WriteInt64(value.value);
        }

        private static void SerializeEnum(object data)
        {
            if (data == null)
            {
                Write('0');
                return;
            }

            var underlyingType = Enum.GetUnderlyingType(data.GetType());
            switch (underlyingType)
            {
                case Type t when t == typeof(byte): WriteUInt64((byte)data); break;
                case Type t when t == typeof(sbyte): WriteInt64((sbyte)data); break;
                case Type t when t == typeof(short): WriteInt64((short)data); break;
                case Type t when t == typeof(ushort): WriteUInt64((ushort)data); break;
                case Type t when t == typeof(int): WriteInt64((int)data); break;
                case Type t when t == typeof(uint): WriteUInt64((uint)data); break;
                case Type t when t == typeof(long): WriteInt64((long)data); break;
                case Type t when t == typeof(ulong): WriteUInt64((ulong)data); break;
                default: throw new Exception($"Unsupported enum type {underlyingType}");
            }
        }

        private static void SerializeUnityObject(Type type, object data, bool quote)
        {
            var value = (UnityEngine.Object)data;
            if (value == null)
            {
                Write("null");
                return;
            }

            SerializeString(value.name, quote);
        }

        private static void SerializeList(object data)
        {
            var list = (IList)data;
            Write('[');
            var first = true;
            var elementType = list.GetType().GetElementType();
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (!first)
                {
                    if (_pretty)
                    {
                        Write(", ");
                    }
                    else
                    {
                        Write(',');
                    }
                }

                SerializeRecursively(item != null ? item.GetType() : elementType, item, true);
                first = false;
            }

            Write(']');
        }

        private static void SerializeArray(object data)
        {
            var array = (Array)data;
            Write('[');
            var first = true;
            var elementType = array.GetType().GetElementType();
            for (int i = 0; i < array.Length; i++)
            {
                var item = array.GetValue(i);
                if (!first)
                {
                    if (_pretty)
                    {
                        Write(", ");
                    }
                    else
                    {
                        Write(',');
                    }
                }

                SerializeRecursively(item != null ? item.GetType() : elementType, item, true);
                first = false;
            }

            Write(']');
        }

        private static void SerializeEnumerable(object data)
        {
            var enumerable = (IEnumerable)data;
            Write('[');
            var first = true;
            var genericType = data.GetType().GetGenericArguments()[0];
            foreach (var item in enumerable)
            {
                if (!first)
                {
                    if (_pretty)
                    {
                        Write(", ");
                    }
                    else
                    {
                        Write(',');
                    }
                }

                SerializeRecursively(item != null ? item.GetType() : genericType, item, true);
                first = false;
            }

            Write(']');
        }

        private static void SerializeObject(object data)
        {
            if (data == null)
            {
                Write("null");
            }

            var type = data.GetType();
            var json = JsonUtility.ToJson(data);

            if (!_typeToObjectFields.TryGetValue(type, out var objectFields))
            {
                objectFields = new List<FieldInfo>();
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if ((field.FieldType == typeof(object) || field.FieldType == typeof(object[]))
                        && !Attribute.IsDefined(field, typeof(NonSerializedAttribute)))
                    {
                        objectFields.Add(field);
                    }
                }

                _typeToObjectFields.Add(type, objectFields);
            }

            if (objectFields.Count > 0)
            {
                Write(json, 0, json.Length - 1);
                foreach (var field in objectFields)
                {
                    var value = field.GetValue(data);
                    if (value != null)
                    {
                        var serializeIf = field.GetCustomAttribute<SerializeIfAttribute>();
                        if (serializeIf != null)
                        {
                            var conditionMethod = data.GetType().GetMethod(serializeIf.Method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                            if (conditionMethod == null)
                            {
                                throw new Exception($"Method {serializeIf.Method} not found in {data.GetType()}");
                            }

                            var shouldSerialize = (bool)conditionMethod.Invoke(data, null);
                            if (!shouldSerialize)
                            {
                                continue;
                            }
                        }

                        if (json.Length > 2)
                        {
                            Write(',');
                        }

                        Write('"');
                        Write(field.Name);
                        Write("\":");
                        SerializeRecursively(value.GetType(), value, true);
                    }
                }
                Write('}');
            }
            else
            {
                Write(json);
            }
        }

        private static void WriteInt64(long data)
        {
            var value = (long)data;
            if (value < 0)
            {
                if (value == long.MinValue) // -9223372036854775808
                {
                    Write("-9223372036854775808");
                    return;
                }

                Write('-');
                value = unchecked(-value);
            }

            WriteUInt64((ulong)value);
        }

        private static void WriteUInt64(ulong value)
        {
            var start = _stream.Length;
            var buf = _stream.GetBuffer();

            while (value > 9)
            {
                var digit = value % 10;
                value /= 10;
                _stream.WriteByte((byte)('0' + digit));
            }

            _stream.WriteByte((byte)('0' + value));
            var newLength = _stream.Length;
            for (int i = 0; i < (newLength - start) / 2; i++)
            {
                var tmp = buf[start + i];
                buf[start + i] = buf[newLength - i - 1];
                buf[newLength - i - 1] = tmp;
            }
        }
    }
}