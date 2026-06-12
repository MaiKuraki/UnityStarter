using System;

namespace CycloneGames.Logger
{
    public sealed class CLogAssertionException : Exception
    {
        public string Category { get; }
        public string FilePath { get; }
        public int LineNumber { get; }
        public string MemberName { get; }

        public CLogAssertionException(string message, string category, string filePath, int lineNumber, string memberName)
            : base(message)
        {
            Category = category;
            FilePath = filePath;
            LineNumber = lineNumber;
            MemberName = memberName;
        }
    }
}
