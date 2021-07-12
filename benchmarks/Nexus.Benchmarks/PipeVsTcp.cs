using BenchmarkDotNet.Attributes;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Nexus.Benchmarks
{
    [MemoryDiagnoser]
    public class PipeVsTcp
    {
        private Process _pipeProcess;
        private Process _tcpProcess;
        private TcpClient _tcpClient;

        [GlobalSetup]
        public void GlobalSetup()
        {
            // assembly path
            var assemblyPath = Assembly.GetExecutingAssembly().Location;

            // run pipe process
            var pipePsi = new ProcessStartInfo("dotnet")
            {
                Arguments = $"{assemblyPath} pipe",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _pipeProcess = new Process() { StartInfo = pipePsi };
            _pipeProcess.Start();

            // run tcp process
            var tcpPsi = new ProcessStartInfo("dotnet")
            {
                Arguments = $"{assemblyPath} tcp",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _tcpProcess = new Process() { StartInfo = tcpPsi };
            _tcpProcess.Start();

            var tcpListener = new TcpListener(IPAddress.Parse("127.0.0.1"), 55555);
            tcpListener.Start();

            _tcpClient = tcpListener.AcceptTcpClient();
            _tcpClient.NoDelay = true;
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _pipeProcess?.Kill();
            _tcpProcess?.Kill();
        }

        [Params(Program.MIN_LENGTH, 100, 10_000, 1_000_000, Program.MAX_LENGTH)]
        public int N;

        [Benchmark(Baseline = true)]
        public Memory<byte> Pipe()
        {
            var pipeReadStream = _pipeProcess.StandardOutput.BaseStream;
            var pipeWriteStream = _pipeProcess.StandardInput.BaseStream;
            using var owner = MemoryPool<byte>.Shared.Rent(N * sizeof(int));

            return ReadFromStream(pipeReadStream, pipeWriteStream, owner.Memory);
        }

        [Benchmark()]
        public Memory<byte> Tcp()
        {
            var tcpReadStream = _tcpClient.GetStream();
            var pipeWriteStream = _tcpProcess.StandardInput.BaseStream;
            using var owner = MemoryPool<byte>.Shared.Rent(N * sizeof(int));

            return ReadFromStream(tcpReadStream, pipeWriteStream, owner.Memory);
        }

        private Memory<byte> ReadFromStream(Stream readStream, Stream writeStream, Memory<byte> buffer)
        {
            // trigger
            var Nbuffer = BitConverter.GetBytes(N);
            writeStream.Write(Nbuffer);
            writeStream.Flush();

            // receive data
            var remaining = N * sizeof(int);
            var offset = 0;

            while (remaining > 0)
            {
                var span = buffer.Slice(offset, remaining).Span;
                var readBytes = readStream.Read(span);

                if (readBytes == 0)
                    throw new Exception("The child process terminated early.");

                remaining -= readBytes;
                offset += readBytes;
            }

            var intBuffer = MemoryMarshal.Cast<byte, int>(buffer.Span);

            // validate first 3 values
            for (int i = 0; i < Math.Min(N, 3); i++)
            {
                if (intBuffer[i] != i)
                    throw new Exception($"Invalid data received. Data is {intBuffer[i]}, index = {i}.");
            }

            return buffer;
        }
    }
}
