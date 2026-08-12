using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Temporarily removes transaction-owned downstream inputs from the source
    /// checkout without teaching version-control providers any path exclusions.
    /// </summary>
    internal sealed class BuildSourceQualificationSuspensionScope : IDisposable
    {
        private readonly List<IDisposable> suspensions;
        private bool disposed;

        private BuildSourceQualificationSuspensionScope(
            List<IDisposable> suspensions)
        {
            this.suspensions = suspensions;
        }

        internal static BuildSourceQualificationSuspensionScope Begin(
            IReadOnlyList<IBuildDeferredPublication> publications)
        {
            if (publications == null)
            {
                throw new ArgumentNullException(nameof(publications));
            }

            var acquired = new List<IDisposable>(publications.Count);
            try
            {
                for (int index = publications.Count - 1; index >= 0; index--)
                {
                    if (!(publications[index]
                          is IBuildSourceQualificationPublication participant))
                    {
                        continue;
                    }

                    IDisposable suspension =
                        participant.SuspendForSourceQualification();
                    if (suspension == null)
                    {
                        throw new InvalidOperationException(
                            $"Source-qualification publication '{participant.Id}' returned a null suspension scope.");
                    }

                    acquired.Add(suspension);
                }

                return new BuildSourceQualificationSuspensionScope(acquired);
            }
            catch (Exception suspensionFailure)
            {
                Exception restorationFailure = DisposeAll(acquired);
                if (restorationFailure != null)
                {
                    throw new AggregateException(
                        "Transaction-owned workspace mutation suspension failed and previously suspended publications could not all be restored.",
                        suspensionFailure,
                        restorationFailure);
                }

                ExceptionDispatchInfo.Capture(suspensionFailure).Throw();
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Exception failure = DisposeAll(suspensions);
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private static Exception DisposeAll(IReadOnlyList<IDisposable> values)
        {
            Exception failure = null;
            for (int index = values.Count - 1; index >= 0; index--)
            {
                try
                {
                    values[index].Dispose();
                }
                catch (Exception exception)
                {
                    failure = failure == null
                        ? exception
                        : new AggregateException(
                            "Multiple transaction-owned workspace mutations failed to restore.",
                            failure,
                            exception);
                }
            }

            return failure;
        }
    }
}
