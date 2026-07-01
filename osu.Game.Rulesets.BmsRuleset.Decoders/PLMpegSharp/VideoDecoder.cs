using PLMpegSharp.Container;
using PLMpegSharp.LUT;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace PLMpegSharp
{
    /// <summary>
    /// MPEG1 video decoder. <br/>
    /// Inspired by Java MPEG-1 Video Decoder and Player by Zoltan Korandi <br/>
    /// https://sourceforge.net/projects/javampeg1video/
    /// </summary>
    public class VideoDecoder
    {
        #region Private fields

        private struct Motion
        {
            public bool fullPX;
            public bool isSet;
            public int rSize;
            public int h;
            public int v;
        }

        private readonly DataBuffer _buffer;

        private readonly int[] _dcPredictor;
        private readonly int[] _blockData;
        private readonly byte[] _intraQuantMatrix;
        private readonly byte[] _nonIntraQuantMatrix;

        private double _framerate;
        private double _time;
        private int _framesDecoded;
        private int _width;
        private int _height;

        private int _mbWidth;
        private int _mbHeight;
        private int _mbSize;

        private int _lumaWidth;
        private int _lumaHeight;

        private int _chromaWidth;
        private int _chromaHeight;

        private PacketStartCode _startCode;
        private VideoPictureType _pictureType;

        private Motion _motionForward;
        private Motion _motionBackward;

        private bool _hasSequenceHeader;

        private int _quantizerScale;
        private bool _sliceBegin;
        private int _macroblockAddress;

        private int _mbRow;
        private int _mbCol;

        private MacroBlockType _macroblockType;
        private bool _macroblockIntra;

        private Frame? _frameCurrent;
        private Frame? _frameForward;
        private Frame? _frameBackward;

        private bool _hasReferenceFrame;
        private bool _assumeNoBFrames;

        #endregion

        #region Public Properties

        /// <summary>
        /// Whether a sequence header was found and we can accurately report on dimensions and framerate.
        /// </summary>
        public bool HasHeader
            => _hasSequenceHeader || DecodeHeader();

        /// <summary>
        /// Framerate in frames per second. (FPS)
        /// </summary>
        public double Framerate
            => HasHeader ? _framerate : 0;

        /// <summary>
        /// Display width
        /// </summary>
        public int Width
            => HasHeader ? _width : 0;

        /// <summary>
        /// Display height
        /// </summary>
        public int Height
            => HasHeader ? _height : 0;

        /// <summary>
        /// Whether in "no delay" mode. When enabled, the decoder assumes that the video does
        /// *not* contain any B-Frames. This is useful for reducing lag when streaming. <br/>
        /// The default is false,
        /// </summary>
        public bool NoDelay
        {
            get => _assumeNoBFrames;
            set => _assumeNoBFrames = value;
        }

        /// <summary>
        /// Current internal time in seconds. <br/>
        /// Setting this is only useful when you manipulate the underlying 
        /// video buffer and want to enforce a correct timestamps.
        /// </summary>
        public double Time
        {
            get => _time;
            set
            {
                _framesDecoded = (int)(_framerate * value);
                _time = value;
            }
        }

        /// <summary>
        /// Whether the file has ended. This will be cleared on <see cref="Rewind"/>.
        /// </summary>
        public bool HasEnded
            => _buffer.HasEnded;

        #endregion

        #region Constructor

        /// <summary>
        /// Create a video decoder with a <see cref="DataBuffer"/> as source.
        /// </summary>
        /// <param name="buffer"></param>
        public VideoDecoder(DataBuffer buffer)
        {
            _buffer = buffer;
            _dcPredictor = new int[3];
            _blockData = new int[64];
            _intraQuantMatrix = new byte[64];
            _nonIntraQuantMatrix = new byte[64];

            _startCode = buffer.FindStartCode(PacketStartCode.Sequence);
            if (_startCode != PacketStartCode.Invalid)
            {
                DecodeSequenceHeader();
            }
        }

        #endregion

        #region Private Methods

        private bool DecodeHeader()
        {
            if (_startCode != PacketStartCode.Sequence)
                _startCode = _buffer.FindStartCode(PacketStartCode.Sequence);

            if (_startCode == PacketStartCode.Invalid)
                return false;

            if (!DecodeSequenceHeader())
                return false;

            return true;
        }

        private bool DecodeSequenceHeader()
        {
            nuint maxHeaderSize = 64 + 2 * 64 * 8; // 64 bit header + 2x 64 byte matrix
            if (!_buffer.Has(maxHeaderSize))
                return false;

            _width = (int)_buffer.Read(12);
            _height = (int)_buffer.Read(12);

            if (_width <= 0 || _height <= 0)
                return false;

            _buffer.Skip(4); // aspect ratio

            _framerate = VideoLUTs.PictureRate[_buffer.Read(4)];

            // bit_rate, marker, buffer_size, constrained bit
            _buffer.Skip(18 + 1 + 10 + 1);

            // Load custom intra quant matrix?
            if (_buffer.Read(1) != 0)
            {
                for (int i = 0; i < 64; i++)
                {
                    _intraQuantMatrix[VideoLUTs.ZigZag[i]] = (byte)_buffer.Read(8);
                }
            }
            else
            {
                Array.Copy(VideoLUTs.IntraQuantMatrix, _intraQuantMatrix, 64);
            }

            // Load custom non intra quant matrix?
            if (_buffer.Read(1) != 0)
            {
                for (int i = 0; i < 64; i++)
                {
                    _nonIntraQuantMatrix[VideoLUTs.ZigZag[i]] = (byte)_buffer.Read(8);
                }
            }
            else
            {
                Array.Copy(VideoLUTs.NonIntraQuantMatrix, _nonIntraQuantMatrix, 64);
            }

            _mbWidth = (_width + 15) >> 4;
            _mbHeight = (_height + 15) >> 4;
            _mbSize = _mbWidth * _mbHeight;

            _lumaWidth = _mbWidth << 4;
            _lumaHeight = _mbHeight << 4;

            _chromaWidth = _mbWidth << 3;
            _chromaHeight = _mbHeight << 3;

            _frameBackward = InitFrame();
            _frameCurrent = InitFrame();
            _frameForward = InitFrame();

            _hasSequenceHeader = true;
            return true;
        }

        private Frame InitFrame()
            => new(_width, _height, _lumaWidth, _lumaHeight, _chromaWidth, _chromaHeight);

        private void DecodeVideoPicture()
        {
            _buffer.Skip(10); // skip temporalReference
            _pictureType = (VideoPictureType)_buffer.Read(3);
            _buffer.Skip(16); // skip vbv_delay

            // D frames or unknown coding type
            if (_pictureType == VideoPictureType.D || !Enum.IsDefined(_pictureType))
                return;

            // Forward full_px, f_code
            if (_pictureType is VideoPictureType.Predictive or VideoPictureType.B)
            {
                _motionForward.fullPX = _buffer.Read(1) != 0;
                int fCode = (int)_buffer.Read(3);
                if (fCode == 0)
                    return; // Ignore picture with zero f_code
                _motionForward.rSize = fCode - 1;
            }

            // Backward full_px, f_code
            if (_pictureType is VideoPictureType.B)
            {
                _motionBackward.fullPX = _buffer.Read(1) != 0;
                int fCode = (int)_buffer.Read(3);
                if (fCode == 0)
                    return; // Ignore picture with zero f_code
                _motionBackward.rSize = fCode - 1;
            }

            Frame? temp = _frameForward;
            if (_pictureType is VideoPictureType.Intra or VideoPictureType.Predictive)
                _frameForward = _frameBackward;

            // Find first slice start code; skip extension and user data
            do
            {
                _startCode = _buffer.NextStartCode();
            }
            while (_startCode is PacketStartCode.Extension or PacketStartCode.UserData);

            // decode all slices
            while (_startCode >= PacketStartCode.SliceFirst && _startCode <= PacketStartCode.SliceLast)
            {
                DecodeSlice((int)_startCode);
                if (_macroblockAddress >= _mbSize - 2)
                    break;
                _startCode = _buffer.NextStartCode();
            }

            // If this is a reference picture rotate the prediction pointers
            if (_pictureType is VideoPictureType.Intra or VideoPictureType.Predictive)
            {
                _frameBackward = _frameCurrent;
                _frameCurrent = temp;
            }
        }

        private void DecodeSlice(int slice)
        {
            _sliceBegin = true;
            _macroblockAddress = (slice - 1) * _mbWidth - 1;

            // Reset motion vectors and DC predictors
            _motionBackward.h = _motionForward.h = 0;
            _motionBackward.v = _motionForward.v = 0;
            Array.Fill(_dcPredictor, 128);

            _quantizerScale = (int)_buffer.Read(5);

            // skip extra
            while (_buffer.Read(1) != 0)
                _buffer.Skip(8);

            do
            {
                DecodeMacroBlock();
            }
            while (_macroblockAddress < _mbSize - 1 && _buffer.PeekNonZero(23));
        }

        private void DecodeMacroBlock()
        {
            // Decode increment
            int increment = 0;
            int t = _buffer.ReadVLC(VideoLUTs.MacroblockAddressIncrement);

            while (t == 34)
            {
                // macroblock_stuffing
                t = _buffer.ReadVLC(VideoLUTs.MacroblockAddressIncrement);
            }
            while (t == 35)
            {
                // macroblock_escape
                increment += 33;
                t = _buffer.ReadVLC(VideoLUTs.MacroblockAddressIncrement);
            }
            increment += t;

            // Process any skipped macroblocks
            if (_sliceBegin)
            {
                // The first increment of each slice is relative to beginning of the
                // previous row, not the previous macroblock
                _sliceBegin = false;
                _macroblockAddress += increment;
            }
            else
            {
                if (_macroblockAddress + increment >= _mbSize)
                    return; // invalid

                if (increment > 1)
                {
                    // Skipped macroblocks reset DC predictors
                    Array.Fill(_dcPredictor, 128);

                    // Skipped macroblocks in P-pictures reset motion vectors
                    if (_pictureType is VideoPictureType.Predictive)
                    {
                        _motionForward.h = 0;
                        _motionForward.v = 0;
                    }
                }

                // Predict skipped macroblocks
                while (increment > 1)
                {
                    _macroblockAddress++;
                    _mbRow = _macroblockAddress / _mbWidth;
                    _mbCol = _macroblockAddress % _mbWidth;

                    PredictMacroblock();
                    increment--;
                }
                _macroblockAddress++;
            }

            _mbRow = _macroblockAddress / _mbWidth;
            _mbCol = _macroblockAddress % _mbWidth;

            if (_mbCol >= _mbWidth || _mbRow >= _mbHeight)
                return; // corrupt stream

            _macroblockType = (MacroBlockType)_buffer.ReadVLC(VideoLUTs.MacroblockType[_pictureType] ?? throw new NullReferenceException($"Invalid picture type: {_pictureType}"));

            _macroblockIntra = _macroblockType.HasFlag(MacroBlockType.Intra);
            _motionForward.isSet = _macroblockType.HasFlag(MacroBlockType.Forward);
            _motionBackward.isSet = _macroblockType.HasFlag(MacroBlockType.Backward);

            // Quantizer scale
            if (_macroblockType.HasFlag(MacroBlockType.HasQuantizer))
            {
                _quantizerScale = (int)_buffer.Read(5);
            }

            if (_macroblockIntra)
            {
                // Intra-coded macroblocks reset motion vectors
                _motionBackward.h = _motionForward.h = 0;
                _motionBackward.v = _motionForward.v = 0;
            }
            else
            {
                // Non-intra macroblocks reset DC predictors
                Array.Fill(_dcPredictor, 128);

                DecodeMotionVectors();
                PredictMacroblock();
            }

            // Decode blocks
            int cbp = _macroblockType.HasFlag(MacroBlockType.CodeBlockPattern)
                ? _buffer.ReadVLC(VideoLUTs.CodeBlockPattern)
                : (_macroblockIntra ? 0x3F : 0);

            for (int block = 0, mask = 0x20; block < 6; block++, mask >>= 1)
            {
                if ((cbp & mask) != 0)
                {
                    DecodeBlock(block);
                }
            }
        }

        private void DecodeMotionVectors()
        {
            if (_motionForward.isSet)
            {
                _motionForward.h = DecodeMotionVector(_motionForward.rSize, _motionForward.h);
                _motionForward.v = DecodeMotionVector(_motionForward.rSize, _motionForward.v);
            }
            else if (_pictureType is VideoPictureType.Predictive)
            {
                // No motion information in P-picture, reset vectors
                _motionForward.h = 0;
                _motionForward.v = 0;
            }

            if (_motionBackward.isSet)
            {
                _motionBackward.h = DecodeMotionVector(_motionBackward.rSize, _motionBackward.h);
                _motionBackward.v = DecodeMotionVector(_motionBackward.rSize, _motionBackward.v);
            }
        }

        private int DecodeMotionVector(int rSize, int motion)
        {
            int fscale = 1 << rSize;
            int m_code = _buffer.ReadVLC(VideoLUTs.Motion);
            int d;

            if ((m_code != 0) && (fscale != 1))
            {
                int r = (int)_buffer.Read((nuint)rSize);
                d = ((int.Abs(m_code) - 1) << rSize) + r + 1;
                if (m_code < 0)
                {
                    d = -d;
                }
            }
            else
            {
                d = m_code;
            }

            motion += d;
            if (motion > (fscale << 4) - 1)
            {
                motion -= fscale << 5;
            }
            else if (motion < ((-fscale) << 4))
            {
                motion += fscale << 5;
            }

            return motion;
        }

        private void PredictMacroblock()
        {
            int fw_h = _motionForward.h;
            int fw_v = _motionForward.v;

            if (_motionForward.fullPX)
            {
                fw_h <<= 1;
                fw_v <<= 1;
            }

            if (_pictureType is VideoPictureType.B)
            {
                int bw_h = _motionBackward.h;
                int bw_v = _motionBackward.v;

                if (_motionBackward.fullPX)
                {
                    bw_h <<= 1;
                    bw_v <<= 1;
                }

                if (_motionForward.isSet)
                {
                    CopyMacroblock(_frameForward, fw_h, fw_v);
                    if (_motionBackward.isSet)
                    {
                        InterpolateMacroblock(_frameBackward, bw_h, bw_v);
                    }
                }
                else
                {
                    CopyMacroblock(_frameBackward, bw_h, bw_v);
                }
            }
            else
            {
                CopyMacroblock(_frameForward, fw_h, fw_v);
            }
        }

        private void CopyMacroblock(Frame? frame, int motionH, int motionV)
        {
            if (frame == null || _frameCurrent == null)
                throw new NullReferenceException("Frames null");

            ProcessMacroblock(frame.Y, _frameCurrent.Y, motionH, motionV, 16, false);
            ProcessMacroblock(frame.CR, _frameCurrent.CR, motionH / 2, motionV / 2, 8, false);
            ProcessMacroblock(frame.CB, _frameCurrent.CB, motionH / 2, motionV / 2, 8, false);
        }

        private void InterpolateMacroblock(Frame? frame, int motionH, int motionV)
        {
            if (frame == null || _frameCurrent == null)
                throw new NullReferenceException("Frames null");

            ProcessMacroblock(frame.Y, _frameCurrent.Y, motionH, motionV, 16, false);
            ProcessMacroblock(frame.CR, _frameCurrent.CR, motionH / 2, motionV / 2, 8, false);
            ProcessMacroblock(frame.CB, _frameCurrent.CB, motionH / 2, motionV / 2, 8, false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BlockSet(byte[] dest, ref int destIndex, int destWidth, ref int sourceIndex, int sourceWidth, int blockSize, Func<byte> op)
        {
            int dest_scan = destWidth - blockSize;
            int source_scan = sourceWidth - blockSize;
            for (int y = 0; y < blockSize; y++)
            {
                for (int x = 0; x < blockSize; x++)
                {
                    dest[destIndex] = op();
                    sourceIndex++;
                    destIndex++;
                }
                sourceIndex += source_scan;
                destIndex += dest_scan;
            }
        }

        private void ProcessMacroblock(Plane source, Plane destination, int motionH, int motionV, int blockSize, bool interpolate)
        {
            int dw = _mbWidth * blockSize;

            int hp = motionH >> 1;
            int vp = motionV >> 1;
            bool odd_h = (motionH & 1) == 1;
            bool odd_v = (motionV & 1) == 1;

            int si = ((_mbRow * blockSize) + vp) * dw + (_mbCol * blockSize) + hp;
            int di = (_mbRow * dw + _mbCol) * blockSize;

            int max_address = dw * (_mbHeight * blockSize - blockSize + 1) - blockSize;
            if (si > max_address || di > max_address)
                return; // corrupt video

            var s = source.Data;
            var d = destination.Data;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void LBlockSet(Func<byte> op) => BlockSet(d, ref di, dw, ref si, dw, blockSize, op);

            switch ((interpolate ? 4 : 0) | (odd_h ? 2 : 0) | (odd_v ? 1 : 0))
            {
                case 0: // None
                    LBlockSet(() => s[si]);
                    break;
                case 1: // OddV
                    LBlockSet(() => (byte)((s[si] + s[si + dw] + 1) >> 1));
                    break;
                case 2: // OddH
                    LBlockSet(() => (byte)((s[si] + s[si + 1] + 1) >> 1));
                    break;
                case 3: // OddV OddH
                    LBlockSet(() => (byte)((s[si] + s[si + 1] + s[si + dw] + s[si + dw + 1] + 2) >> 2));
                    break;
                case 4: // Interpolate
                    LBlockSet(() => (byte)((d[di] + (s[si]) + 1) >> 1));
                    break;
                case 5: // Interpolate OddV
                    LBlockSet(() => (byte)((d[di] + ((s[si] + s[si + dw] + 1) >> 1) + 1) >> 1));
                    break;
                case 6: // Interpolate OddH
                    LBlockSet(() => (byte)((d[di] + ((s[si] + s[si + 1] + 1) >> 1) + 1) >> 1));
                    break;
                case 7: // Interpolate OddV OddH
                    LBlockSet(() => (byte)((d[di] + ((s[si] + s[si + 1] + s[si + dw] + s[si + dw + 1] + 2) >> 2) + 1) >> 1));
                    break;
            }
        }

        private void DecodeBlock(int block)
        {
            if (_frameCurrent == null)
                throw new NullReferenceException("Frames are null");

            int n = 0;
            byte[] quant_matrix;

            // Decode DC coefficient of intra-coded blocks
            if (_macroblockIntra)
            {
                // DC prediction
                int plane_index = block > 3 ? block - 3 : 0;
                int predictor = _dcPredictor[plane_index];
                int dct_size = _buffer.ReadVLC(VideoLUTs.DCTSize[plane_index]);

                // Read DC coeff
                if (dct_size > 0)
                {
                    int differential = (int)_buffer.Read((nuint)dct_size);
                    if ((differential & (1 << (dct_size - 1))) != 0)
                    {
                        _blockData[0] = predictor + differential;
                    }
                    else
                    {
                        _blockData[0] = predictor + (-(1 << dct_size) | (differential + 1));
                    }
                }
                else
                {
                    _blockData[0] = predictor;
                }

                // Save predictor value
                _dcPredictor[plane_index] = _blockData[0];

                // Dequantize + premultiply
                _blockData[0] <<= (3 + 5);

                quant_matrix = _intraQuantMatrix;
                n = 1;
            }
            else
            {
                quant_matrix = _nonIntraQuantMatrix;
            }

            // Decode AC coefficients (+DC for non-intra)
            int level;
            while (true)
            {
                int run;
                ushort coeff = _buffer.ReadUVLC(VideoLUTs.DCTCoefficient);

                if ((coeff == 0x0001) && (n > 0) && (_buffer.Read(1) == 0))
                {
                    // end_of_block
                    break;
                }
                if (coeff == 0xffff)
                {
                    // escape
                    run = (int)_buffer.Read(6);
                    level = (int)_buffer.Read(8);
                    if (level == 0)
                    {
                        level = (int)_buffer.Read(8);
                    }
                    else if (level == 128)
                    {
                        level = (int)_buffer.Read(8) - 256;
                    }
                    else if (level > 128)
                    {
                        level = level - 256;
                    }
                }
                else
                {
                    run = coeff >> 8;
                    level = coeff & 0xff;
                    if (_buffer.Read(1) != 0)
                    {
                        level = -level;
                    }
                }

                n += run;
                if (n < 0 || n >= 64)
                {
                    return; // invalid
                }

                int de_zig_zagged = VideoLUTs.ZigZag[n];
                n++;

                // Dequantize, oddify, clip
                level <<= 1;
                if (!_macroblockIntra)
                {
                    level += (level < 0 ? -1 : 1);
                }
                level = (level * _quantizerScale * quant_matrix[de_zig_zagged]) >> 4;
                if ((level & 1) == 0)
                {
                    level -= level > 0 ? 1 : -1;
                }
                if (level > 2047)
                {
                    level = 2047;
                }
                else if (level < -2048)
                {
                    level = -2048;
                }

                // Save premultiplied coefficient
                _blockData[de_zig_zagged] = level * VideoLUTs.PremultiplierMatrix[de_zig_zagged];
            }

            // Move block to its place
            byte[] d;
            int dw;
            int di;

            if (block < 4)
            {
                d = _frameCurrent.Y.Data;
                dw = _lumaWidth;
                di = (_mbRow * _lumaWidth + _mbCol) << 4;
                if ((block & 1) != 0)
                {
                    di += 8;
                }
                if ((block & 2) != 0)
                {
                    di += _lumaWidth << 3;
                }
            }
            else
            {
                d = (block == 4) ? _frameCurrent.CB.Data : _frameCurrent.CR.Data;
                dw = _chromaWidth;
                di = ((_mbRow * _lumaWidth) << 2) + (_mbCol << 3);
            }

            int si = 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void LBlockSet(Func<byte> op) => BlockSet(d, ref di, dw, ref si, 8, 8, op);

            if (_macroblockIntra)
            {
                // Overwrite (no prediction)
                if (n == 1)
                {
                    byte clamped = Util.Clamp((_blockData[0] + 128) >> 8);
                    LBlockSet(() => clamped);
                    _blockData[0] = 0;
                }
                else
                {
                    IDCT();
                    LBlockSet(() => Util.Clamp(_blockData[si]));
                    Array.Clear(_blockData);
                }
            }
            else
            {
                // Add data to the predicted macroblock
                if (n == 1)
                {
                    int value = (_blockData[0] + 128) >> 8;
                    LBlockSet(() => Util.Clamp(d[di] + value));
                    _blockData[0] = 0;
                }
                else
                {
                    IDCT();
                    LBlockSet(() => Util.Clamp(d[di] + _blockData[si]));
                    Array.Clear(_blockData);
                }
            }
        }

        private void IDCT()
        {
            int b1, b3, b4, b6, b7, tmp1, tmp2, m0,
                x0, x1, x2, x3, x4, y3, y4, y5, y6, y7;

            // Transform columns
            for (int i = 0; i < 8; ++i)
            {
                b1 = _blockData[4 * 8 + i];
                b3 = _blockData[2 * 8 + i] + _blockData[6 * 8 + i];
                b4 = _blockData[5 * 8 + i] - _blockData[3 * 8 + i];
                tmp1 = _blockData[1 * 8 + i] + _blockData[7 * 8 + i];
                tmp2 = _blockData[3 * 8 + i] + _blockData[5 * 8 + i];
                b6 = _blockData[1 * 8 + i] - _blockData[7 * 8 + i];
                b7 = tmp1 + tmp2;
                m0 = _blockData[0 * 8 + i];
                x4 = ((b6 * 473 - b4 * 196 + 128) >> 8) - b7;
                x0 = x4 - (((tmp1 - tmp2) * 362 + 128) >> 8);
                x1 = m0 - b1;
                x2 = (((_blockData[2 * 8 + i] - _blockData[6 * 8 + i]) * 362 + 128) >> 8) - b3;
                x3 = m0 + b1;
                y3 = x1 + x2;
                y4 = x3 + b3;
                y5 = x1 - x2;
                y6 = x3 - b3;
                y7 = -x0 - ((b4 * 473 + b6 * 196 + 128) >> 8);
                _blockData[0 * 8 + i] = b7 + y4;
                _blockData[1 * 8 + i] = x4 + y3;
                _blockData[2 * 8 + i] = y5 - x0;
                _blockData[3 * 8 + i] = y6 - y7;
                _blockData[4 * 8 + i] = y6 + y7;
                _blockData[5 * 8 + i] = x0 + y5;
                _blockData[6 * 8 + i] = y3 - x4;
                _blockData[7 * 8 + i] = y4 - b7;
            }

            // Transform rows
            for (int i = 0; i < 64; i += 8)
            {
                b1 = _blockData[4 + i];
                b3 = _blockData[2 + i] + _blockData[6 + i];
                b4 = _blockData[5 + i] - _blockData[3 + i];
                tmp1 = _blockData[1 + i] + _blockData[7 + i];
                tmp2 = _blockData[3 + i] + _blockData[5 + i];
                b6 = _blockData[1 + i] - _blockData[7 + i];
                b7 = tmp1 + tmp2;
                m0 = _blockData[0 + i];
                x4 = ((b6 * 473 - b4 * 196 + 128) >> 8) - b7;
                x0 = x4 - (((tmp1 - tmp2) * 362 + 128) >> 8);
                x1 = m0 - b1;
                x2 = (((_blockData[2 + i] - _blockData[6 + i]) * 362 + 128) >> 8) - b3;
                x3 = m0 + b1;
                y3 = x1 + x2;
                y4 = x3 + b3;
                y5 = x1 - x2;
                y6 = x3 - b3;
                y7 = -x0 - ((b4 * 473 + b6 * 196 + 128) >> 8);
                _blockData[0 + i] = (b7 + y4 + 128) >> 8;
                _blockData[1 + i] = (x4 + y3 + 128) >> 8;
                _blockData[2 + i] = (y5 - x0 + 128) >> 8;
                _blockData[3 + i] = (y6 - y7 + 128) >> 8;
                _blockData[4 + i] = (y6 + y7 + 128) >> 8;
                _blockData[5 + i] = (x0 + y5 + 128) >> 8;
                _blockData[6 + i] = (y3 - x4 + 128) >> 8;
                _blockData[7 + i] = (y4 - b7 + 128) >> 8;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Rewind the internal buffer. See <see cref="DataBuffer.Rewind"/>.
        /// </summary>
        public void Rewind()
        {
            _buffer.Rewind();
            _time = 0;
            _framesDecoded = 0;
            _hasReferenceFrame = false;
            _startCode = PacketStartCode.Invalid;
        }

        /// <summary>
        /// Decode and return one frame of video and advance the internal time by 1/framerate seconds. <br/>
        /// The returned <see cref="Frame"/> is valid until the next call of <see cref="Decode"/>.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidDataException">Thrown when data is invalid</exception>
        public Frame? Decode()
        {
            if (!HasHeader)
                return null;

            Frame? result = null;

            do
            {
                if (_startCode != PacketStartCode.Picture)
                {
                    _startCode = _buffer.FindStartCode(PacketStartCode.Picture);

                    if (_startCode == PacketStartCode.Invalid)
                    {
                        // If we reached the end of the file and the previously decoded
                        // frame was a reference frame, we still have to return it.
                        if (_hasReferenceFrame
                            && !_assumeNoBFrames
                            && _buffer.HasEnded
                            && (_pictureType is VideoPictureType.Intra or VideoPictureType.Predictive))
                        {
                            _hasReferenceFrame = false;
                            result = _frameBackward;
                            break;
                        }

                        return null;
                    }
                }

                // Make sure we have a full picture in the buffer before attempting to
                // decode it. Sadly, this can only be done by seeking for the start code
                // of the next picture. Also, if we didn't find the start code for the
                // next picture, but the source has ended, we assume that this last
                // picture is in the buffer.

                if (_buffer.HasStartCode(PacketStartCode.Picture) == PacketStartCode.Invalid
                    && !_buffer.HasEnded)
                {
                    return null;
                }

                _buffer.DiscardReadBytes();

                DecodeVideoPicture();

                if (_assumeNoBFrames)
                {
                    result = _frameBackward;
                }
                else if (_pictureType == VideoPictureType.B)
                {
                    result = _frameCurrent;
                }
                else if (_hasReferenceFrame)
                {
                    result = _frameForward;
                }
                else
                {
                    _hasReferenceFrame = true;
                }
            }
            while (result == null);

            if (result == null)
                throw new InvalidDataException();

            result.Time = _time;
            _framesDecoded++;
            _time = _framesDecoded / _framerate;

            return result;
        }

        #endregion
    }
}
