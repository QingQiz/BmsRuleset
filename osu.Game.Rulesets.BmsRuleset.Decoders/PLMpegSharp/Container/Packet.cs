using System;

namespace PLMpegSharp.Container
{
    /// <summary>
    /// Demuxed MPEG PS packet
    /// </summary>
    public class Packet
    {
        public const double InvalidTS = -1;

        /// <summary>
        /// Buffer of which the packets byte data originates
        /// </summary>
        public DataBuffer Buffer { get; }

        /// <summary>
        /// MPEG PES start code
        /// </summary>
        public PacketStartCode Type { get; internal set; }

        /// <summary>
        /// Presentation time stamp (short PTS) of the packet in seconds. <br/>
        /// Note that not all packets have a PTS value, indicated by <see cref="InvalidTS"/>
        /// </summary>
        public double PTS { get; internal set; }

        /// <summary>
        /// Data byte startindex inside the assigned buffer
        /// </summary>
        public nuint StartIndex { get; internal set; }

        /// <summary>
        /// Data byte length
        /// </summary>
        public nuint Length { get; internal set; }

        /// <summary>
        /// Packets byte data
        /// </summary>
        public ReadOnlySpan<byte> Data
            => Buffer.Bytes.Slice((int)StartIndex, (int)Length);

        internal Packet(DataBuffer buffer)
        {
            Buffer = buffer;
            Type = PacketStartCode.Invalid;
        }
    }
}
