using BenchmarkDotNet.Running;
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;

// results are here: https://stackoverflow.com/questions/68347138/ipc-performance-anonymous-pipe-vs-socket/68347139#68347139
namespace Nexus.Benchmarks
{
    public class Program
    {
        public const int MIN_LENGTH = 1;
        public const int MAX_LENGTH = 10_000_000;

        static void Main(string[] args)
        {
            if (!args.Any())
            {
                var summary = BenchmarkRunner.Run<PipeVsTcp>();
            }
            else
            {
                var data = MemoryMarshal
                     .AsBytes<int>(
                         Enumerable
                             .Range(0, MAX_LENGTH)
                             .ToArray())
                     .ToArray();

                using var readStream = Console.OpenStandardInput();

                if (args[0] == "pipe")
                {
                    using var pipeStream = Console.OpenStandardOutput();
                    RunChildProcess(readStream, pipeStream, data);
                }

                else if (args[0] == "tcp")
                {
                    var tcpClient = new TcpClient()
                    {
                        NoDelay = true
                    };

                    tcpClient.Connect("localhost", 55555);
                    var tcpStream = tcpClient.GetStream();
                    RunChildProcess(readStream, tcpStream, data);
                }

                else
                {
                    throw new Exception("Invalid argument (args[0]).");
                }
            }
        }

        static void RunChildProcess(Stream readStream, Stream writeStream, byte[] data)
        {
            // wait for start signal
            Span<byte> buffer = stackalloc byte[4];

            while (true)
            {
                var length = readStream.Read(buffer);

                if (length == 0)
                    throw new Exception($"The host process terminated early.");

                var N = BitConverter.ToInt32(buffer);

                // write
                writeStream.Write(data, 0, N * sizeof(int));
            }
        }
    }
}
