using System.Runtime.CompilerServices;

namespace PLMpegSharp.Container
{
    /// <summary>
    /// Decoded Video Frame
    /// </summary>
    public class Frame
    {
        /// <summary>
        /// Timestamp at which the frame takes place in seconds
        /// </summary>
        public double Time { get; internal set; }

        /// <summary>
        /// desired display width of the frame
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// desired display height of the frame
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Luminance plane
        /// </summary>
        public Plane Y { get; }

        /// <summary>
        /// Blue difference chroma plane
        /// </summary>
        public Plane CB { get; }

        /// <summary>
        /// Red difference chroma plane
        /// </summary>
        public Plane CR { get; }

        internal Frame(int width, int height, int lumaWidth, int lumaHeight, int chromaWidth, int chromaHeight)
        {
            Width = width;
            Height = height;
            Y = new(lumaWidth, lumaHeight);
            CR = new(chromaWidth, chromaHeight);
            CB = new(chromaWidth, chromaHeight);
        }

        /// <summary>
        /// Convert the YCrCb data of a frame into interleaved RGBA data. <br/>
        /// The stride specifies the width in bytes of the destination buffer, i.e. the number of
        /// bytes from one line to the next. The stride must be at least (<see cref="Width"/> * 4). <br/>
        /// The destination must have a size of at least (<see cref="Height"/> * <paramref name="stride"/>). <br/>
        /// Note that the alpha component of the dest buffer is always left untouched.
        /// </summary>
        /// <param name="destination"></param>
        /// <param name="stride"></param>
        public void ToRGBA(byte[] destination, int stride)
        {
            const int bpp = 4;
            const int RI = 0, GI = 1, BI = 2;

            int cols = Width >> 1;
            int rows = Height >> 1;
            int yw = Y.Width;
            int cw = CB.Width;
            for (int row = 0; row < rows; row++)
            {
                int c_index = row * cw;
                int y_index = row * 2 * yw;
                int d_index = row * 2 * stride;
                for (int col = 0; col < cols; col++)
                {
                    int y;
                    int cr = CR.Data[c_index] - 128;
                    int cb = CB.Data[c_index] - 128;
                    int r = (cr * 104597) >> 16;
                    int g = (cb * 25674 + cr * 53278) >> 16;
                    int b = (cb * 132201) >> 16;

                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    void PutPixel(int yOffset, int destOffset)
                    {
                        y = ((Y.Data[y_index + yOffset] - 16) * 76309) >> 16;
                        destination[d_index + destOffset + RI] = Util.Clamp(y + r);
                        destination[d_index + destOffset + GI] = Util.Clamp(y - g);
                        destination[d_index + destOffset + BI] = Util.Clamp(y + b);
                    }

                    PutPixel(0, 0);
                    PutPixel(1, bpp);
                    PutPixel(yw, stride);
                    PutPixel(yw + 1, stride + bpp);

                    c_index += 1;
                    y_index += 2;
                    d_index += 2 * bpp;
                }
            }
        }
    }
}
