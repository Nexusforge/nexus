using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Nexus.Core
{
    internal class AggregationFile : IDisposable
    {
        #region Fields

        // derived from https://support.hdfgroup.org/HDF5/doc/H5.format.html#Superblock
        private static byte[] _signature = new byte[] { 0x89, 0x4E, 0x45, 0x58, 0x0D, 0x0A, 0x1A, 0x0A };

        #endregion

        #region Methods

        public static Span<T> Read<T>(string filePath) where T : unmanaged
        {
            // open file
            var fileStream = File.OpenRead(filePath);

            // validate signature
            Span<byte> signature = stackalloc byte[8];
            fileStream.Read(signature);
            AggregationFile.ValidateSignature(signature, _signature);

            // version
            var version = fileStream.ReadByte();

            if (version != 1)
                throw new Exception("Only *.nex files of version 1 are supported.");

            // uncompressed size
            Span<byte> sizeBytes = stackalloc byte[4];
            fileStream.Read(sizeBytes);
            var uncompressedSize = BitConverter.ToInt32(sizeBytes);

            // return data
            using var decompressedStream = new MemoryStream(capacity: uncompressedSize);
            using var decompressionStream = new DeflateStream(fileStream, CompressionMode.Decompress);

            decompressionStream.CopyTo(decompressedStream);

            var span = decompressedStream
                .GetBuffer()
                .AsSpan(0, (int)decompressedStream.Length);

            return MemoryMarshal
                .Cast<byte, T>(span);
        }

        public static void Create<T>(string filePath, ReadOnlySpan<T> buffer) where T : unmanaged
        {
            // target stream
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            var targetStream = File.Open(filePath, FileMode.Create, FileAccess.Write);

            // format signature
            targetStream.Write(_signature);

            // version
            targetStream.WriteByte(1);

            // uncompressed size in bytes
            var byteBuffer = MemoryMarshal.AsBytes(buffer);
            targetStream.Write(BitConverter.GetBytes(byteBuffer.Length));

            // data
            using var compressionStream = new DeflateStream(targetStream, CompressionMode.Compress);
            compressionStream.Write(byteBuffer);
        }

        private static void ValidateSignature(Span<byte> actual, Span<byte> expected)
        {
            if (actual.Length == expected.Length)
            {
                if (actual[0] == expected[0] && actual[1] == expected[1] && actual[2] == expected[2] && actual[3] == expected[3]
                 && actual[4] == expected[4] && actual[5] == expected[5] && actual[6] == expected[6] && actual[7] == expected[7])
                {
                    return;
                }
            }

            throw new Exception("This is not a valid Nexus file.");
        }

        #endregion

        #region IDisposable

        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    //
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
