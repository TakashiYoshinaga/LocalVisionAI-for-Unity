using System;

namespace LiteRtLmUnity
{
    public sealed class VisionAiRequest
    {
        public byte[] JpegData { get; }
        public int Width { get; }
        public int Height { get; }

        public VisionAiRequest(byte[] jpegData, int width, int height)
        {
            JpegData = jpegData ?? throw new ArgumentNullException(nameof(jpegData));
            Width = width;
            Height = height;
        }
    }
}
