using PLMpegSharp.Container;

namespace PLMpegSharp
{
    /// <summary>
    /// MPEG-PS Packet demuxer
    /// </summary>
    public class Demux
    {
        #region Private Fields

        private readonly DataBuffer _buffer;
        private readonly Packet _currentPacket;
        private readonly Packet _nextPacket;

        private bool _hasHeaders;
        private bool _hasPackHeader;
        private bool _hasSystemHeader;

        private int _numVideoStreams;
        private PacketStartCode _startCode;

        #endregion

        #region Public Properties

        /// <summary>
        /// Whether pack and system headers have been found. <br/>
        /// This will attempt to read the headers if non are present yet.
        /// </summary>
        public bool HasHeaders
            => _hasHeaders || DecodeHeaders();

        /// <summary>
        /// Whether the file has ended. This will be cleared on <see cref="Rewind"/>.
        /// </summary>
        public bool HasEnded
            => _buffer.HasEnded;

        /// <summary>
        /// The number of video streams found in the system header. <br/>
        /// This will attempt to read the system header if non is present yet.
        /// </summary>
        public int NumVideoStreams
            => HasHeaders ? _numVideoStreams : 0;

        #endregion

        #region Constructors

        /// <summary>
        /// Create a demuxer with a <see cref="DataBuffer"/> as source. This will also attempt to read
        /// the pack and system headers from the buffer.
        /// </summary>
        /// <param name="buffer"></param>
        public Demux(DataBuffer buffer)
        {
            _buffer = buffer;

            _startCode = PacketStartCode.Invalid;

            _currentPacket = new(_buffer);
            _nextPacket = new(_buffer);

            DecodeHeaders();
        }

        #endregion

        #region Private Methods

        private bool DecodeHeaders()
        {
            if (!_hasPackHeader)
            {
                if (_startCode != PacketStartCode.Pack
                    && _buffer.FindStartCode(PacketStartCode.Pack) == PacketStartCode.Invalid)
                {
                    return false;
                }

                _startCode = PacketStartCode.Pack;
                if (!_buffer.Has(64))
                    return false;
                _startCode = PacketStartCode.Invalid;

                if (_buffer.Read(4) != 2)
                    return false;

                DecodeTime(); // system ref time
                _buffer.Skip(1);
                _buffer.Skip(22); // mux_rate * 50
                _buffer.Skip(1);

                _hasPackHeader = true;
            }

            if (!_hasSystemHeader)
            {
                if (_startCode != PacketStartCode.System
                    && _buffer.FindStartCode(PacketStartCode.System) == PacketStartCode.Invalid)
                {
                    return false;
                }

                _startCode = PacketStartCode.System;
                if (!_buffer.Has(56))
                    return false;
                _startCode = PacketStartCode.Invalid;

                _buffer.Skip(16); // header_length
                _buffer.Skip(24); // rate bound
                _buffer.Skip(6);  // num_audio_streams (parsed past, not stored)
                _buffer.Skip(5);  // misc flags
                _numVideoStreams = (int)_buffer.Read(5);

                _hasSystemHeader = true;
            }

            _hasHeaders = true;
            return true;
        }

        private double DecodeTime()
        {
            nuint clock = _buffer.Read(3) << 30;
            _buffer.Skip(1);
            clock |= _buffer.Read(15) << 15;
            _buffer.Skip(1);
            clock |= _buffer.Read(15);
            _buffer.Skip(1);
            return clock / 90000.0;
        }

        private Packet? DecodePacket(PacketStartCode type)
        {
            if (!_buffer.Has(16 << 3))
                return null;

            _startCode = PacketStartCode.Invalid;

            _nextPacket.Type = type;
            _nextPacket.Length = _buffer.Read(16);
            _nextPacket.Length -= (nuint)_buffer.SkipBytes(0xFF); // stuffing

            // skip P-STD
            if (_buffer.Read(2) == 1)
            {
                _buffer.Skip(16);
                _nextPacket.Length -= 2;
            }

            nuint pts_dts_marker = _buffer.Read(2);
            switch (pts_dts_marker)
            {
                case 0:
                    _nextPacket.PTS = Packet.InvalidTS;
                    _buffer.Skip(4);
                    _nextPacket.Length -= 1;
                    break;
                case 2:
                    _nextPacket.PTS = DecodeTime();
                    _nextPacket.Length -= 5;
                    break;
                case 3:
                    _nextPacket.PTS = DecodeTime();
                    _buffer.Skip(40); // skip dts
                    _nextPacket.Length -= 10;
                    break;
                default:
                    return null;
            }

            return GetPacket();
        }

        private Packet? GetPacket()
        {
            if (!_buffer.Has(_nextPacket.Length << 3))
                return null;

            _currentPacket.StartIndex = _buffer.BitIndex >> 3;
            _currentPacket.Length = _nextPacket.Length;
            _currentPacket.Type = _nextPacket.Type;
            _currentPacket.PTS = _nextPacket.PTS;

            _nextPacket.Length = 0;
            return _currentPacket;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Rewinds the internal buffer. See <see cref="DataBuffer.Rewind"/>.
        /// </summary>
        public void Rewind()
        {
            _buffer.Rewind();
            _currentPacket.Length = 0;
            _nextPacket.Length = 0;
            _startCode = PacketStartCode.Invalid;
        }

        /// <summary>
        /// Decode and return the next packet. The returned <see cref="Packet"/> is valid until
        /// the next call to <see cref="Decode"/>.
        /// </summary>
        /// <returns></returns>
        public Packet? Decode()
        {
            if (!HasHeaders)
                return null;

            if (_currentPacket.Length != 0)
            {
                nuint bitsTillNextPacket = _currentPacket.Length << 3;
                if (!_buffer.Has(bitsTillNextPacket))
                    return null;

                _buffer.Skip(bitsTillNextPacket);
                _currentPacket.Length = 0;
            }

            if (_nextPacket.Length != 0)
            {
                return GetPacket();
            }

            if (_startCode != PacketStartCode.Invalid)
            {
                return DecodePacket(_startCode);
            }

            do
            {
                _startCode = _buffer.NextStartCode();
                if (_startCode == PacketStartCode.VideoFirst
                    || _startCode == PacketStartCode.PrivateStream1
                    || (_startCode >= PacketStartCode.AudioFirst &&
                         _startCode <= PacketStartCode.AudioFirst + 4))
                {
                    return DecodePacket(_startCode);
                }
            }
            while (_startCode != PacketStartCode.Invalid);

            return null;
        }

        #endregion
    }
}
