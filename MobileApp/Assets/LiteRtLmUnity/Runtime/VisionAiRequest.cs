using System;

namespace LiteRtLmUnity
{
    public sealed class VisionAiRequest
    {
        public byte[] JpegData { get; }
        public int Width { get; }
        public int Height { get; }
        public string Prompt { get; }

        public VisionAiRequest(byte[] jpegData, int width, int height, string prompt)
        {
            JpegData = jpegData ?? throw new ArgumentNullException(nameof(jpegData));
            Width = width;
            Height = height;
            Prompt = prompt ?? string.Empty;
        }
    }
}
