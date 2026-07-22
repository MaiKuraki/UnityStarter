using CycloneGames.Hash.Core;
using Unity.Mathematics;

namespace CycloneGames.BehaviorTree.Runtime.DOD
{
    /// <summary>
    /// Versioned, domain-separated hash primitives for DOD scheduler state.
    /// Float values are hashed by their IEEE-754 bit representation.
    /// </summary>
    internal static class BTDataOrientedStateHash
    {
        internal const ulong SchedulerProfile = 0x52454C5544454843UL;

        internal const ulong NodeStatesDomain = 0x4554415453444F4EUL;
        internal const ulong AuxIntsDomain = 0x53544E495855415FUL;
        internal const ulong AuxFloatsDomain = 0x54414F4C46585541UL;
        internal const ulong BlackboardIntsDomain = 0x53544E4942425F5FUL;
        internal const ulong BlackboardFloatsDomain = 0x54414F4C4642425FUL;
        internal const ulong BlackboardBoolsDomain = 0x534C4F4F4242425FUL;
        internal const ulong ActionStatusesDomain = 0x535554415443415FUL;
        internal const ulong ActionGenerationsDomain = 0x4E4547435443415FUL;
        internal const ulong TimingDomain = 0x474E494D49545F5FUL;

        private const ulong HashContract = 0x324853444F445442UL;

        internal static ulong Begin(ulong profile)
        {
            ulong hash = AddUInt64(Fnv1a64.OffsetBasis, HashContract);
            return AddUInt64(hash, profile);
        }

        internal static ulong BeginDomain(ulong hash, ulong domain, int count)
        {
            hash = AddUInt64(hash, domain);
            return AddUInt32(hash, unchecked((uint)count));
        }

        internal static ulong AddByte(ulong hash, byte value)
        {
            unchecked
            {
                return (hash ^ value) * Fnv1a64.Prime;
            }
        }

        internal static ulong AddUInt32(ulong hash, uint value)
        {
            return Fnv1a64.CombineUInt32LittleEndian(hash, value);
        }

        internal static ulong AddUInt64(ulong hash, ulong value)
        {
            return Fnv1a64.CombineUInt64LittleEndian(hash, value);
        }

        internal static ulong AddFloat(ulong hash, float value)
        {
            return AddUInt32(hash, math.asuint(value));
        }
    }
}
