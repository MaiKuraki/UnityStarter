using System;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// A durable publication owned by the current run. A publication may stay
    /// staged until the terminal barrier, or may also implement
    /// <see cref="IBuildDownstreamInputPublication"/> when later build steps
    /// must consume its reversible output before the terminal decision.
    /// </summary>
    public interface IBuildDeferredPublication : IDisposable
    {
        string Id { get; }
        string RecoveryStateRelativePath { get; }
        void Publish();
        void Complete();
    }

    /// <summary>
    /// A publication whose output must become visible to later build steps.
    /// Activation must retain enough durable state for Dispose or recovery to
    /// restore the exact pre-run state until the shared terminal barrier commits.
    /// </summary>
    public interface IBuildDownstreamInputPublication : IBuildDeferredPublication
    {
        void ActivateForDownstream();
    }

    /// <summary>
    /// An activated downstream publication whose transaction-owned workspace
    /// mutations can be hidden while the runner qualifies the source checkout.
    /// The returned scope must restore the exact publication-ready state when
    /// disposed. Implementations must fail closed when either state cannot be
    /// proven and must retain durable recovery evidence across interruption.
    /// </summary>
    public interface IBuildSourceQualificationPublication
        : IBuildDownstreamInputPublication
    {
        IDisposable SuspendForSourceQualification();
    }
}
