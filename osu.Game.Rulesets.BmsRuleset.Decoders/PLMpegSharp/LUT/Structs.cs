namespace PLMpegSharp.LUT
{
    internal struct VLC
    {
        public short index;
        public short value;

        public VLC(short index, short value)
        {
            this.index = index;
            this.value = value;
        }
    }

    internal struct UVLC
    {
        public short index;
        public ushort value;

        public UVLC(short index, ushort value)
        {
            this.index = index;
            this.value = value;
        }
    }
}
