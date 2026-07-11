namespace CycloneGames.DeterministicMath
{
    /// <summary>
    /// Intrinsic Euler angle rotation order.
    /// The second rotation happens in the frame of the first, and the third in the frame of the second.
    /// </summary>
    public enum EulerOrder
    {
        XYZ, XZY, YXZ, YZX, ZXY, ZYX
    }
}
