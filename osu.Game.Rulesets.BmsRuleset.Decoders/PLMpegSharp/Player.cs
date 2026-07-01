using PLMpegSharp.Container;

namespace PLMpegSharp
{
    /// <summary>
    /// High level MPEG1 Player
    /// </summary>
    public class Player
    {
        #region Private Fields/Properties

        private readonly Demux _demux;
        private bool _has_decoders;

        private PacketStartCode _videoPacketType;
        private DataBuffer? _videoBuffer;
        private VideoDecoder? _videoDecoder;

        private bool HasDecoders
        {
            get
            {
                if (!_has_decoders)
                    InitDecoders();
                return _has_decoders;
            }
        }

        private VideoDecoder? VideoDecoder
            => HasDecoders ? _videoDecoder : null;

        #endregion

        #region Public Properties

        /// <summary>
        /// Display width of the video stream
        /// </summary>
        public int Width
            => VideoDecoder?.Width ?? 0;

        /// <summary>
        /// Display height of the video stream
        /// </summary>
        public int Height
            => VideoDecoder?.Height ?? 0;

        /// <summary>
        /// Framerate of the video stream in frames per second (FPS)
        /// </summary>
        public double Framerate
            => VideoDecoder?.Framerate ?? 0;

        #endregion

        #region Constructors

        /// <summary>
        /// Create an MPEG1 Player using byte data. <br/>
        /// This assumes the whole file is contained in the data. <br/>
        /// The memory is not copied.
        /// </summary>
        /// <param name="bytes">File memory byte data</param>
        public Player(byte[] bytes)
        {
            _demux = new Demux(DataBuffer.CreateWithMemory(bytes));
            _videoPacketType = PacketStartCode.Invalid;
            InitDecoders();
        }

        #endregion

        #region Private Methods

        private void InitDecoders()
        {
            if (!_demux.HasHeaders)
                return;

            if (_demux.NumVideoStreams > 0)
            {
                _videoPacketType = PacketStartCode.VideoFirst;
                _videoBuffer = DataBuffer.CreateWithCapacity();
                _videoBuffer.LoadCallback = ReadVideoPacket;
            }

            if (_videoBuffer != null)
                _videoDecoder = new VideoDecoder(_videoBuffer);

            _has_decoders = true;
        }

        private void ReadVideoPacket(DataBuffer buffer)
            => ReadPackets();

        private void ReadPackets()
        {
            Packet? packet = _demux.Decode();
            while (packet != null)
            {
                if (packet.Type == _videoPacketType)
                {
                    _videoBuffer?.Write(packet.Data);
                    return;
                }

                packet = _demux.Decode();
            }

            if (_demux.HasEnded)
            {
                _videoBuffer?.SignalEnd();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Decode and return one video frame. Returns null if no frame could be decoded
        /// (either because the source ended or data is corrupt). <br/>
        /// The returned <see cref="Frame"/> is valid until the next call to <see cref="DecodeVideo"/>.
        /// </summary>
        public Frame? DecodeVideo()
            => VideoDecoder?.Decode();

        /// <summary>
        /// Rewind all buffers back to the beginning
        /// </summary>
        public void Rewind()
        {
            VideoDecoder?.Rewind();
            _demux.Rewind();
        }

        #endregion
    }
}
