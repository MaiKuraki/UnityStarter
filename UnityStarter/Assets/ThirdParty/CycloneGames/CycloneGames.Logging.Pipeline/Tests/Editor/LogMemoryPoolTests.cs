using System;
using System.Text;
using CycloneGames.Logging.Internal;
using NUnit.Framework;

namespace CycloneGames.Logging.Pipeline.Tests.Editor
{
    public sealed class LogMemoryPoolTests
    {
        [Test]
        public void StringBuilderPool_GetStringAndReturn_ReturnsContentAndRecordsReturn()
        {
            StringBuilderPool.ResetStatistics();

            StringBuilder builder = StringBuilderPool.Get();
            builder.Append("pooled message");

            string result = StringBuilderPool.GetStringAndReturn(builder);
            var stats = StringBuilderPool.GetStatistics();

            Assert.AreEqual("pooled message", result);
            Assert.AreEqual(1, stats.TotalGets);
            Assert.AreEqual(1, stats.TotalReturns);
        }

        [Test]
        public void StringBuilderPool_Return_DiscardOversizedBuilder()
        {
            StringBuilderPool.ResetStatistics();

            StringBuilderPool.Return(new StringBuilder(4097));
            var stats = StringBuilderPool.GetStatistics();

            Assert.AreEqual(1, stats.TotalReturns);
            Assert.AreEqual(1, stats.TotalDiscards);
        }

        [Test]
        public void LogEventPool_Return_ResetsReferencesAndReturnsNestedBuilder()
        {
            LogEventPool.ResetStatistics();
            StringBuilderPool.ResetStatistics();

            LogEvent message = LogEventPool.Get();
            StringBuilder builder = StringBuilderPool.Get();
            builder.Append("builder payload");

            message.Initialize(
                new DateTime(2026, 5, 20, 1, 2, 3, 4),
                LogSeverity.Info,
                "original payload",
                builder,
                "Pool",
                "LogMemoryPoolTests.cs",
                42,
                nameof(LogEventPool_Return_ResetsReferencesAndReturnsNestedBuilder));

            LogEventPool.Return(message);
            var messageStats = LogEventPool.GetStatistics();
            var builderStats = StringBuilderPool.GetStatistics();

            Assert.IsNull(message.OriginalMessage);
            Assert.IsNull(message.MessageBuilder);
            Assert.IsNull(message.Category);
            Assert.IsNull(message.FilePath);
            Assert.IsNull(message.MemberName);
            Assert.AreEqual(1, messageStats.TotalReturns);
            Assert.AreEqual(1, builderStats.TotalReturns);
        }
    }
}
