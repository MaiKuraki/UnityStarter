using System;
using System.Text;

namespace CycloneGames.UIFramework.Editor
{
    internal static class UIWindowTitleFormatter
    {
        public static string BuildTemplateTitleText(string scriptName)
        {
            string title = scriptName;

            if (title.StartsWith("UIWindow", StringComparison.Ordinal) && title.Length > "UIWindow".Length)
            {
                title = title.Substring("UIWindow".Length);
            }
            else if (title.StartsWith("UI", StringComparison.Ordinal) && title.Length > "UI".Length)
            {
                title = title.Substring("UI".Length);
            }

            if (title.EndsWith("Window", StringComparison.Ordinal) && title.Length > "Window".Length)
            {
                title = title.Substring(0, title.Length - "Window".Length);
            }

            if (string.IsNullOrEmpty(title))
            {
                title = scriptName;
            }

            return SplitPascalCaseTitle(title);
        }

        private static string SplitPascalCaseTitle(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            StringBuilder builder = new StringBuilder(value.Length + 8);
            builder.Append(value[0]);

            for (int i = 1; i < value.Length; i++)
            {
                char current = value[i];
                char previous = value[i - 1];
                char next = i + 1 < value.Length ? value[i + 1] : '\0';

                bool startsWord = char.IsUpper(current) && (char.IsLower(previous) || (next != '\0' && char.IsLower(next)));
                if (startsWord)
                {
                    builder.Append(' ');
                }

                builder.Append(current);
            }

            return builder.ToString();
        }
    }
}
