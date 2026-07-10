using System.Text;

namespace CycloneGames.Logger
{
    internal static class InvariantText
    {
        internal static void AppendInt32(StringBuilder builder, int value)
        {
            if (value == 0)
            {
                builder.Append('0');
                return;
            }

            uint magnitude;
            if (value < 0)
            {
                builder.Append('-');
                magnitude = unchecked((uint)(-(long)value));
            }
            else
            {
                magnitude = (uint)value;
            }

            int divisor = 1;
            while (magnitude / (uint)divisor >= 10U && divisor <= 100000000)
            {
                divisor *= 10;
            }

            do
            {
                uint digit = magnitude / (uint)divisor;
                builder.Append((char)('0' + digit));
                magnitude %= (uint)divisor;
                divisor /= 10;
            }
            while (divisor != 0);
        }
    }
}
