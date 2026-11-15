using System;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Runtime
{
    /// <summary>
    /// Channel flags with no built-in semantics. The module supplies sixteen numbered slots; the
    /// game layer assigns meaning through constant aliases such as <c>Channel0 = NPC</c>.
    /// </summary>
    [Flags]
    public enum InteractionChannel : ushort
    {
        None      = 0,
        Channel0  = 1 << 0,
        Channel1  = 1 << 1,
        Channel2  = 1 << 2,
        Channel3  = 1 << 3,
        Channel4  = 1 << 4,
        Channel5  = 1 << 5,
        Channel6  = 1 << 6,
        Channel7  = 1 << 7,
        Channel8  = 1 << 8,
        Channel9  = 1 << 9,
        Channel10 = 1 << 10,
        Channel11 = 1 << 11,
        Channel12 = 1 << 12,
        Channel13 = 1 << 13,
        Channel14 = 1 << 14,
        Channel15 = 1 << 15,
        All       = 0xFFFF
    }
}
