using System;
using System.Text;
using CycloneGames.Logger.Util;
using NUnit.Framework;

namespace CycloneGames.Logger.Tests.Editor
{
    public sealed class LoggerPoolTests
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

            Assert.AreEqual(0, stats.TotalReturns);
            Assert.AreEqual(1, stats.TotalDiscards);
        }

        [Test]
        public void LogMessagePool_Return_ResetsReferencesAndReturnsNestedBuilder()
        {
            LogMessagePool.ResetStatistics();
            StringBuilderPool.ResetStatistics();

            LogMessage message = LogMessagePool.Get();
            StringBuilder builder = StringBuilderPool.Get();
            builder.Append("builder payload");

            message.Initialize(
                new DateTime(2026, 5, 20, 1, 2, 3, 4),
                LogLevel.Info,
                "original payload",
                builder,
                "Pool",
                "LoggerPoolTests.cs",
                42,
                nameof(LogMessagePool_Return_ResetsReferencesAndReturnsNestedBuilder));

            LogMessagePool.Return(message);
            var messageStats = LogMessagePool.GetStatistics();
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
