using System;
using System.IO;
using System.Threading;

using NUnit.Framework;

namespace CycloneGames.IO.Tests.Core
{
    public sealed class FileRetryTests
    {
        [Test]
        public void Execute_CustomTransientClassifier_RetriesUntilSuccess()
        {
            int attempts = 0;
            var policy = new FileRetryPolicy(
                3,
                TimeSpan.Zero,
                1.0,
                TimeSpan.Zero,
                _ => true);

            int result = FileRetry.Execute(() =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new IOException("Transient test failure.");
                }

                return 42;
            }, policy);

            Assert.That(result, Is.EqualTo(42));
            Assert.That(attempts, Is.EqualTo(3));
        }

        [Test]
        public void Execute_NonTransientFailure_DoesNotRetry()
        {
            int attempts = 0;
            var policy = new FileRetryPolicy(
                4,
                TimeSpan.Zero,
                1.0,
                TimeSpan.Zero,
                _ => false);

            Assert.Throws<IOException>(() => FileRetry.Execute(() =>
            {
                attempts++;
                throw new IOException("Permanent test failure.");
            }, policy));
            Assert.That(attempts, Is.EqualTo(1));
        }

        [Test]
        public void Policy_DelayBeyondTaskDelayRange_ThrowsDuringConstruction()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FileRetryPolicy(
                2,
                TimeSpan.Zero,
                1.0,
                TimeSpan.FromMilliseconds((double)int.MaxValue + 1.0)));
        }

        [Test]
        public void Execute_PreCancelledToken_DoesNotInvokeOperation()
        {
            int attempts = 0;
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                Assert.Throws<OperationCanceledException>(() => FileRetry.Execute(
                    () =>
                    {
                        attempts++;
                    },
                    cancellationToken: cancellation.Token));
            }

            Assert.That(attempts, Is.Zero);
        }
    }
}
