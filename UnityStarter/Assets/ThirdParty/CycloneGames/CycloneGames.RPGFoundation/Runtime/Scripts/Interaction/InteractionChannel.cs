using System;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    /// <summary>
    /// Semantics-free interaction channel flags. The framework provides 16 numbered slots;
    /// games define their own meaning via constant aliases in game-layer code.
    /// <example>
    /// <code>
    /// public static class MyGameChannels
    /// {
    ///     public const InteractionChannel NPC  = InteractionChannel.Channel0;
    ///     public const InteractionChannel Item = InteractionChannel.Channel1;
    /// }
    /// </code>
    /// </example>
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
