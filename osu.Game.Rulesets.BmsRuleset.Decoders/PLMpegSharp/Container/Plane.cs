namespace PLMpegSharp.Container
{
    /// <summary>
    /// Decoded Video Plane 
    /// </summary>
    public class Plane
    {
        /// <summary>
        /// Plane width
        /// </summary>
        public int Width { get; }

        /// <summary>
        ///  Plane height
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Size of width * height <br/>
        /// Note that different planes have different sizes: <br/>
        /// the Luma plane (Y) is double the size of each of the two Chroma planes <br/>
        /// (Cr, Cb) - i.e. 4 times the byte length. Also note that the size of the plane <br/>
        /// does *not* denote the size of the displayed frame. The sizes of planes are <br/>
        /// always rounded up to the nearest macroblock (16px)
        /// </summary>
        public byte[] Data { get; }

        internal Plane(int width, int height)
        {
            Width = width;
            Height = height;
            Data = new byte[width * height];
        }
    }
}
