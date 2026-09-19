using System;

namespace LiteRtLmUnity
{
    public sealed class ImageRequest
    {
        public byte[] JpegData { get; }
        public int Width { get; }
        public int Height { get; }

        public ImageRequest(byte[] jpegData, int width, int height)
        {
            JpegData = jpegData ?? throw new ArgumentNullException(nameof(jpegData));
            Width = width;
            Height = height;
        }
    }
}
