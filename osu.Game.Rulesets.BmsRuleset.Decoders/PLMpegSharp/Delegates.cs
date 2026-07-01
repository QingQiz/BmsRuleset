namespace PLMpegSharp
{
    /// <summary>
    /// Callback function for when the buffer needs more data
    /// </summary>
    /// <param name="buffer"></param>
    public delegate void BufferLoadCallback(DataBuffer buffer);
}
