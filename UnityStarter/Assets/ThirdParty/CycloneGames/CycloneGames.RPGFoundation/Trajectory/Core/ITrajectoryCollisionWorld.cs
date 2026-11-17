using System;

namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    /// <summary>
    /// Narrow collision adapter boundary. Implementations sweep the segment described by
    /// <see cref="TrajectoryCastQuery"/> and report what it crossed.
    /// </summary>
    public interface ITrajectoryCollisionWorld
    {
        /// <summary>
        /// Sweeps <c>query.From</c> to <c>query.To</c> and writes hits into <paramref name="results"/>.
        ///
        /// <paramref name="results"/> is a loan from the caller's trace buffer. Its length is the hard
        /// write limit; return the number actually written. The span cannot be stored or captured, so
        /// retaining it is a compile error.
        ///
        /// Report a hit only when the swept segment crosses geometry; an endpoint-only test tunnels
        /// through thin colliders. <c>Distance</c> is measured from <c>query.From</c> along
        /// <c>query.Direction</c> within <c>[0, query.Distance]</c>. <c>Position</c> should lie on the
        /// segment, and the solver falls back to <c>From + Direction * Distance</c> when it does not.
        ///
        /// Populate <c>TargetObjectId</c> and/or <c>TargetEntityId</c>: the solver uses them to suppress
        /// repeated pierce contacts. Honor <c>IgnoredTargetEntityId</c> / <c>IgnoredTargetObjectId</c>
        /// when possible, but correctness does not depend on it.
        ///
        /// <see cref="TrajectoryCollisionWorldConformance.VerifySweep"/> exercises the observable parts
        /// of this contract.
        /// </summary>
        int Cast(
            in TrajectoryCastQuery query,
            Span<TrajectoryHit> results);
    }
}
