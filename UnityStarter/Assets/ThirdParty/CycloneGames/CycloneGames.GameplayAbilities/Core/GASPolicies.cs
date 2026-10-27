using CycloneGames.DeterministicMath;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Controls how ability runtime instances are created and reused.
    /// 
    /// NonInstanced: a single shared instance serves all activations (no per-actor state allowed).
    /// InstancedPerActor: one instance per ASC, reused across activations (default for most abilities).
    /// InstancedPerExecution: a new instance per activation (needed when overlapping executions must not share state).
    /// </summary>
    public enum GASInstancingPolicy : byte
    {
        NonInstanced,
        InstancedPerActor,
        InstancedPerExecution
    }

    /// <summary>
    /// Arithmetic operation applied by a modifier to its target attribute.
    /// Override bypasses all stacking — the attribute's value is set directly
    /// to the modifier's magnitude, ignoring any Add/Multiply/Divide contributions.
    /// </summary>
    public enum GASModifierOp : byte
    {
        Add,
        Multiply,
        Division,
        Override
    }

    /// <summary>
    /// Selects how duration/infinite modifiers combine while aggregating an attribute.
    /// Instant effects always apply their modifiers sequentially to the base value and are not
    /// affected by this mode.
    /// </summary>
    public enum GASModifierAggregationMode : byte
    {
        /// <summary>
        /// Default. Multiply/Division compose as true products: two x2 modifiers yield x4.
        /// Per-stack scaling is a separate axis; see <see cref="GASStackMagnitudePolicy"/>.
        /// </summary>
        MultiplicativeProduct,

        /// <summary>
        /// Multiply and Division form a bias sum: 1 + sum(Mi - 1); two x2 modifiers yield x3.
        /// This is the additive-bucket convention shared by Unreal Engine and most ARPG stat systems.
        /// Per-stack scaling is a separate axis; see <see cref="GASStackMagnitudePolicy"/>.
        /// </summary>
        UnrealBiasSum
    }

    /// <summary>
    /// Selects how one effect's stack count scales its Multiply/Division modifier magnitude.
    /// Additive modifiers always scale linearly (magnitude * stackCount) and Override ignores the
    /// stack count entirely, matching Unreal Engine's ComputeStackedModifierMagnitude.
    /// Orthogonal to <see cref="GASModifierAggregationMode"/>, which only decides how several
    /// modifiers of the same channel combine.
    /// </summary>
    public enum GASStackMagnitudePolicy : byte
    {
        /// <summary>
        /// Default. (magnitude - 1) * stackCount + 1: every stack adds the same delta, so a x1.1 buff
        /// at 5 stacks is x1.5. Prevalent ARPG/MMORPG convention and numerically identical to UE.
        /// </summary>
        LinearBias,

        /// <summary>
        /// magnitude ^ stackCount: every stack compounds, so a x1.1 buff at 5 stacks is x1.61.
        /// Intended for deliberate "double per stack" designs. Q32.32 exhausts its integer range
        /// near 2.1e9, so authoring rejects limits whose worst-case product cannot be represented.
        /// </summary>
        Exponential
    }

    /// <summary>
    /// Ordered modifier evaluation layers for attribute aggregators.
    /// Channel0 is the default path and is enough for most effects. Additional channels let advanced
    /// systems express "evaluate these modifiers after the previous layer" without hard-coded rules.
    /// </summary>
    public enum GASModifierEvaluationChannel : byte
    {
        Channel0,
        Channel1,
        Channel2,
        Channel3,
        Channel4,
        Channel5,
        Channel6,
        Channel7,
        Channel8,
        Channel9
    }

    public static class GASModifierEvaluationChannels
    {
        public const int MAX_CHANNEL_COUNT = 10;

        public static bool IsValid(GASModifierEvaluationChannel channel)
        {
            return (byte)channel < MAX_CHANNEL_COUNT;
        }

        public static GASModifierEvaluationChannel Normalize(GASModifierEvaluationChannel channel)
        {
            return IsValid(channel) ? channel : GASModifierEvaluationChannel.Channel0;
        }
    }

    /// <summary>
    /// Channel aggregation contract. Hosts that keep their own attribute view must route
    /// modifiers through these methods so every view produces identical values.
    /// </summary>
    public static class GASModifierAggregation
    {
        /// <summary>
        /// Mutable per-channel state. Reset before each evaluation pass and fold afterwards in
        /// ascending channel order.
        /// </summary>
        public struct Accumulator
        {
            public FPInt64 Add;
            public FPInt64 Multiply;
            public FPInt64 MultiplicativeBias;
            public FPInt64 DivisionBias;
            public FPInt64 OverrideValue;
            public bool HasOverride;
            public bool HasModifier;

            /// <summary>Returns an accumulator in its reset state.</summary>
            public static Accumulator Create()
            {
                var accumulator = default(Accumulator);
                accumulator.Reset();
                return accumulator;
            }

            public void Reset()
            {
                Add = FPInt64.Zero;
                Multiply = FPInt64.One;
                MultiplicativeBias = FPInt64.One;
                DivisionBias = FPInt64.One;
                OverrideValue = FPInt64.Zero;
                HasOverride = false;
                HasModifier = false;
            }
        }

        /// <summary>
        /// Adds one modifier to its channel accumulator. <paramref name="magnitudeRaw"/> is the
        /// resolved magnitude before stack scaling; stacking and the aggregation mode are applied here.
        /// </summary>
        public static void Accumulate(
            ref Accumulator accumulator,
            GASModifierOp op,
            long magnitudeRaw,
            int stackCount,
            GASStackMagnitudePolicy stackMagnitudePolicy,
            bool unrealBiasSum)
        {
#if DEBUG
            if (!accumulator.HasModifier && accumulator.MultiplicativeBias.RawValue == 0)
            {
                throw new System.InvalidOperationException(
                    "Accumulator was used without Reset() or Create(); an uninitialised Multiply is zero.");
            }
#endif
            accumulator.HasModifier = true;
            var magnitude = FPInt64.FromRaw(magnitudeRaw);
            switch (op)
            {
                case GASModifierOp.Add:
                    accumulator.Add += magnitude * FPInt64.FromInt(stackCount);
                    break;
                case GASModifierOp.Multiply:
                {
                    var stacked = FPInt64.FromRaw(GASFixedValue.ResolveStackScaledMagnitudeRaw(
                        magnitudeRaw,
                        stackCount,
                        stackMagnitudePolicy));
                    if (unrealBiasSum)
                    {
                        accumulator.MultiplicativeBias += stacked - FPInt64.One;
                    }
                    else
                    {
                        accumulator.Multiply *= stacked;
                    }

                    break;
                }
                case GASModifierOp.Division:
                {
                    if (magnitudeRaw == 0)
                    {
                        break;
                    }

                    var stackedDivisor = FPInt64.FromRaw(GASFixedValue.ResolveStackScaledMagnitudeRaw(
                        magnitudeRaw,
                        stackCount,
                        stackMagnitudePolicy));
                    // LinearBias scaling of a sub-1 magnitude can reach exactly zero
                    // ((M-1)*stack+1 == 0); treat it as a no-op divisor instead of dividing by zero.
                    if (stackedDivisor.RawValue == 0)
                    {
                        break;
                    }

                    if (unrealBiasSum)
                    {
                        accumulator.DivisionBias += stackedDivisor - FPInt64.One;
                    }
                    else
                    {
                        accumulator.Multiply /= stackedDivisor;
                    }

                    break;
                }
                case GASModifierOp.Override:
                    accumulator.HasOverride = true;
                    accumulator.OverrideValue = magnitude;
                    break;
            }
        }

        /// <summary>
        /// Folds one channel into the running value. A channel without modifiers returns the input
        /// unchanged; an override channel replaces it.
        /// </summary>
        public static long Fold(in Accumulator accumulator, long inputValueRaw, bool unrealBiasSum)
        {
            if (!accumulator.HasModifier)
            {
                return inputValueRaw;
            }

            if (accumulator.HasOverride)
            {
                return accumulator.OverrideValue.RawValue;
            }

            var inputValue = FPInt64.FromRaw(inputValueRaw);
            if (unrealBiasSum)
            {
                var product = (inputValue + accumulator.Add) * accumulator.MultiplicativeBias;
                return accumulator.DivisionBias.RawValue != 0
                    ? (product / accumulator.DivisionBias).RawValue
                    : product.RawValue;
            }

            return ((inputValue + accumulator.Add) * accumulator.Multiply).RawValue;
        }
    }

    /// <summary>
    /// Controls an effect's lifecycle.
    /// Instant: applied once and immediately consumed (damage, cost).
    /// Duration: active for a fixed number of ticks, then auto-removed (buffs, debuffs, DoTs).
    /// Infinite: active until manually removed (equipment stats, auras, passives).
    /// </summary>
    public enum GASEffectDurationPolicy : byte
    {
        Instant,
        Infinite,
        Duration
    }

}
