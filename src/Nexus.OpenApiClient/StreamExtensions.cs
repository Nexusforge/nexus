using System.Runtime.InteropServices;

namespace Nexus.Client
{
    /// <summary>
    /// Contains extension methods for instances of type <see cref="Stream"/>.
    /// </summary>
    public static class StreamExtensions
    {
        /// <summary>
        /// Copies the stream content into an array. 
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <returns>The stream content as byte array.</returns>
        public static Span<T> AsSpan<T>(this Stream stream) where T : struct
        {
            var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);

            var data = memoryStream.ToArray();

            return MemoryMarshal.Cast<byte, T>(data);
        }
    }
}