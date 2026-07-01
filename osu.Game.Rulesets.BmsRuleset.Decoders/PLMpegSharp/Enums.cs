namespace PLMpegSharp
{
    public enum PacketStartCode : int
    {
        Invalid = -1,
        Picture = 0x00,
        SliceFirst = 0x01,
        SliceLast = 0xAF,
        Reserved0 = 0xB0,
        Reserved1 = 0xB1,
        UserData = 0xB2,
        Sequence = 0xB3,
        SequenceError = 0xB4,
        Extension = 0xB5,
        Reserved2 = 0xB6,
        SequenceEnd = 0xB7,
        GroupOfPictures = 0xB8,
        ProgramEnd = 0xB9,
        Pack = 0xBA,
        System = 0xBB,
        ProgramStreamMap = 0xBC,
        PrivateStream1 = 0xBD,
        PaddingStream = 0xBE,
        PrivateStream2 = 0xBF,
        AudioFirst = 0xC0,
        AudioLast = 0xDF,
        VideoFirst = 0xE0,
        VideoLast = 0xEF,
        ECMStream = 0xF0,
        EMMStream = 0xF1,
        H2220 = 0xF2,
        ISO13818Stream = 0xF2,
        ISO13522Stream = 0xF3,
        H2221A = 0xF4,
        H2221B = 0xF5,
        H2221C = 0xF6,
        H2221D = 0xF7,
        H2221E = 0xF8,
        AncillaryStream = 0xF9,
        Reserved3 = 0xFA,
        Reserved4 = 0xFB,
        Reserved5 = 0xFC,
        Reserved6 = 0xFD,
        Reserved7 = 0xFE,
        ProgramStreamDirectory = 0xFF
    }

    public enum VideoPictureType : int
    {
        None = 0,
        Intra = 1,
        Predictive = 2,
        B = 3,
        D = 4,
    }

    public enum MacroBlockType : ushort
    {
        Intra = 0x01,
        CodeBlockPattern = 0x02,
        Backward = 0x04,
        Forward = 0x08,
        HasQuantizer = 0x10
    }
}
