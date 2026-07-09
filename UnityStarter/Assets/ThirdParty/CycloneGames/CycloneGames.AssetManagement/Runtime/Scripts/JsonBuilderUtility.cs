using System;
using System.Globalization;
using System.Text;

namespace CycloneGames.AssetManagement.Runtime
{
    internal static class JsonBuilderUtility
    {
        public static void AppendProperty(StringBuilder builder, string name, string value, bool appendComma)
        {
            AppendPropertyName(builder, name, appendComma);
            AppendString(builder, value);
        }

        public static void AppendProperty(StringBuilder builder, string name, int value, bool appendComma)
        {
            AppendPropertyName(builder, name, appendComma);
            builder.Append(value);
        }

        public static void AppendProperty(StringBuilder builder, string name, long value, bool appendComma)
        {
            AppendPropertyName(builder, name, appendComma);
            builder.Append(value);
        }

        public static void AppendProperty(StringBuilder builder, string name, bool value, bool appendComma)
        {
            AppendPropertyName(builder, name, appendComma);
            builder.Append(value ? "true" : "false");
        }

        public static void AppendString(StringBuilder builder, string value)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (value == null)
            {
                builder.Append("null");
                return;
            }

            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        private static void AppendPropertyName(StringBuilder builder, string name, bool appendComma)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (appendComma)
            {
                builder.Append(',');
            }

            AppendString(builder, name);
            builder.Append(':');
        }
    }
}
