using System.Runtime.CompilerServices;

namespace PLMpegSharp
{
    internal static class Util
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Clamp(int val)
            => (byte)int.Clamp(val, byte.MinValue, byte.MaxValue);
    }
}
