using System.Collections;
using System.Threading;
using CycloneGames.DataTable.Unity.Editor;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace CycloneGames.DataTable.Tests.Editor.Tools.Luban
{
    public sealed class DataTableLubanRunnerTests
    {
        [UnityTest]
        public IEnumerator RunWithResultAsync_InvalidRequest_FinalizesOnMainThread()
        {
            var mainThreadId = Thread.CurrentThread.ManagedThreadId;
            var request = CreateInvalidRequest();
            DataTableLubanRunResult result = default;
            var completionThreadId = -1;

            yield return DataTableLubanRunner.RunWithResultAsync(request).ToCoroutine(value =>
            {
                result = value;
                completionThreadId = Thread.CurrentThread.ManagedThreadId;
            });

            Assert.IsFalse(result.Success);
            Assert.IsFalse(result.Cancelled);
            StringAssert.Contains("working directory is empty", result.ErrorMessage);
            Assert.AreEqual(mainThreadId, completionThreadId);
            Assert.IsFalse(DataTableLubanRunner.IsRunning);
        }

        [UnityTest]
        public IEnumerator RunWithResultAsync_PreCancelled_ReturnsStructuredCancellationWithoutWriter()
        {
            var request = CreateInvalidRequest();
            DataTableLubanRunResult result = default;

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                yield return DataTableLubanRunner.RunWithResultAsync(request, cancellation.Token)
                    .ToCoroutine(value => result = value);
            }

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Cancelled);
            Assert.IsFalse(result.TimedOut);
            Assert.AreEqual(-1, result.ExitCode);
            Assert.IsFalse(DataTableLubanRunner.IsRunning);
        }

        private static DataTableLubanRunRequest CreateInvalidRequest()
        {
            return new DataTableLubanRunRequest
            {
                WorkingDirectory = string.Empty,
                ScriptName = "missing",
                ScriptExtension = ".bat",
                ScriptPath = string.Empty,
                TimeoutMilliseconds = 1000,
                LogOutputToUnity = false
            };
        }
    }
}
