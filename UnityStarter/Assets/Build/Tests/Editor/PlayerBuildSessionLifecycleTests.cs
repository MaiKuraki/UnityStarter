using System;
using System.Collections.Generic;
using Build.Pipeline.Editor;
using NUnit.Framework;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class PlayerBuildSessionLifecycleTests
    {
        [Test]
        public void DisposePlayerBuildSessions_DisposesInReverseBeginOrder()
        {
            var order = new List<string>();
            var sessions = new IDisposable[]
            {
                new RecordingSession("guard", order),
                new RecordingSession("extension", order),
                new RecordingSession("content", order)
            };

            Exception failure = PlayerBuildStep.DisposePlayerBuildSessions(sessions);

            Assert.That(failure, Is.Null);
            Assert.That(order, Is.EqualTo(new[]
            {
                "content",
                "extension",
                "guard"
            }));
        }

        [Test]
        public void DisposePlayerBuildSessions_AggregatesFailuresAndContinuesRestoring()
        {
            var order = new List<string>();
            var guardFailure = new InvalidOperationException("guard restore failed");
            var contentFailure = new InvalidOperationException("content restore failed");
            var sessions = new IDisposable[]
            {
                new RecordingSession("guard", order, guardFailure),
                new RecordingSession("extension", order),
                new RecordingSession("content", order, contentFailure)
            };

            Exception failure = PlayerBuildStep.DisposePlayerBuildSessions(sessions);

            Assert.That(order, Is.EqualTo(new[]
            {
                "content",
                "extension",
                "guard"
            }));
            Assert.That(failure, Is.TypeOf<AggregateException>());
            var aggregate = ((AggregateException)failure).Flatten();
            Assert.That(aggregate.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(aggregate.InnerExceptions, Does.Contain(contentFailure));
            Assert.That(aggregate.InnerExceptions, Does.Contain(guardFailure));
        }

        private sealed class RecordingSession : IDisposable
        {
            private readonly string id;
            private readonly ICollection<string> order;
            private readonly Exception failure;

            internal RecordingSession(
                string id,
                ICollection<string> order,
                Exception failure = null)
            {
                this.id = id;
                this.order = order;
                this.failure = failure;
            }

            public void Dispose()
            {
                order.Add(id);
                if (failure != null)
                {
                    throw failure;
                }
            }
        }
    }
}
