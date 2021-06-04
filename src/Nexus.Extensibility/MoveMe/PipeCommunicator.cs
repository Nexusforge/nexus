using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensions
{
    public class PipeCommunicator
    {
        #region Types

        internal class CastMemoryManager<TFrom, TTo> : MemoryManager<TTo>
            where TFrom : struct
            where TTo : struct
        {
            private readonly Memory<TFrom> _from;

            public CastMemoryManager(Memory<TFrom> from) => _from = from;

            public override Span<TTo> GetSpan() => MemoryMarshal.Cast<TFrom, TTo>(_from.Span);

            protected override void Dispose(bool disposing)
            {
                //
            }

            public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

            public override void Unpin() => throw new NotSupportedException();
        }

        class TimeSpanConverter : JsonConverter<TimeSpan>
        {
            public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                return TimeSpan.Parse(reader.GetString());
            }

            public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.ToString());
            }
        }

        #endregion

        #region Fields

        private string[] AVAILABLE_PROTOCOLS = new string[] { "nexus_pipes_v1" };

        private ILogger _logger;

        private string _command;
        private string _arguments;

        private Process _process;
        private Stream _pipeInput;
        private Stream _pipeOutput;

        private JsonSerializerOptions _jsonOptions;

        private SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        #endregion

        #region Constructors

        public PipeCommunicator(string command, string arguments, ILogger logger)
        {
            _command = command;
            _arguments = arguments;
            _logger = logger;

            _jsonOptions = new JsonSerializerOptions();
            _jsonOptions.Converters.Add(new TimeSpanConverter());
            _jsonOptions.Converters.Add(new JsonStringEnumConverter());
        }

        #endregion

        #region Properties

        public bool IsConnected { get; private set; }

        #endregion

        #region Methods

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            // start process
            var psi = new ProcessStartInfo(_command)
            {
                Arguments = _arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _process = new Process() { StartInfo = psi };
            _process.Start();

            _pipeInput = _process.StandardInput.BaseStream;
            _pipeOutput = _process.StandardOutput.BaseStream;
            
            _process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data is not null)
                {
                    try
                    {
                        var logMessage = JsonSerializer.Deserialize<LogMessage>(e.Data, _jsonOptions);
                        _logger.Log(logMessage.LogLevel, logMessage.Message);
                    }
                    catch (Exception)
                    {
                        _logger.LogError(e.Data);
                        this.IsConnected = false;
                    }
                }
            };

            _process.BeginErrorReadLine();

            this.IsConnected = true;

            var request = new ProtocolRequest(AVAILABLE_PROTOCOLS);
            var response = await this.TranceiveAsync<ProtocolRequest, ProtocolResponse>(request, cancellationToken);

            if (response.SelectedProtocol != "nexus_pipes_v1")
                throw new PipeProtocolException("The child process does not support protocol 'nexus_pipes_v1'.");
        }

        public async Task SendAsync<TRequest>(TRequest request, CancellationToken cancellationToken)
            where TRequest : IPipeMessage
        {
            try
            {
                await _semaphore.WaitAsync(cancellationToken);

                if (!this.IsConnected)
                    throw new InvalidOperationException("Cannot communicate in disconnected state.");

                // send request
                var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, _jsonOptions);
                var requestLengthBytes = BitConverter.GetBytes(requestBytes.Length);

                await _pipeInput.WriteAsync(requestLengthBytes);
                await _pipeInput.WriteAsync(requestBytes);
                await _pipeInput.FlushAsync();

                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<TResponse> TranceiveAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken) 
            where TRequest : IPipeMessage
            where TResponse : IPipeMessage
        {
            try
            {
                await _semaphore.WaitAsync(cancellationToken);

                if (!this.IsConnected)
                    throw new InvalidOperationException("Cannot communicate in disconnected state.");

                // send request
                var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, _jsonOptions);
                var requestLengthBytes = BitConverter.GetBytes(requestBytes.Length);

                await _pipeInput.WriteAsync(requestLengthBytes, cancellationToken);
                await _pipeInput.WriteAsync(requestBytes, cancellationToken);
                await _pipeInput.FlushAsync();

                cancellationToken.ThrowIfCancellationRequested();

                // read response
                var responseLengthBuffer = new byte[4];
                await this.ReadInternalAsync<byte>(responseLengthBuffer, cancellationToken);
                var responseLength = BitConverter.ToInt32(responseLengthBuffer);

                if (responseLength == 0)
                {
                    this.IsConnected = false;
                    throw new PipeProtocolException("Invalid number of bytes received.");
                }

                using var response_bytes = MemoryPool<byte>.Shared.Rent(responseLength);
                var responseBuffer = response_bytes.Memory.Slice(0, responseLength);
                await this.ReadInternalAsync(responseBuffer, cancellationToken);
                var reponse = JsonSerializer.Deserialize<TResponse>(response_bytes.Memory.Slice(0, responseLength).Span, _jsonOptions);

                return reponse;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task ReadAsync<T>(Memory<T> buffer, CancellationToken cancellationToken)
           where T : unmanaged
        {
            try
            {
                await _semaphore.WaitAsync(cancellationToken);
                await this.ReadInternalAsync(buffer, cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task ReadInternalAsync<T>(Memory<T> buffer, CancellationToken cancellationToken)
            where T : unmanaged
        {
            while (buffer.Length > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var memory = new CastMemoryManager<T, byte>(buffer).Memory;
                var byteCount = await _pipeOutput.ReadAsync(memory, cancellationToken);
                this.ValidateResponse(byteCount);
                buffer = buffer.Slice(byteCount);
            }
        }

        private void ValidateResponse(int readCount)
        {
            if (readCount == 0)
            {
                this.IsConnected = false;
                _process?.Kill();
                throw new PipeProtocolException("The connection aborted unexpectedly.");
            }
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
                    var request = new ShutdownRequest();

                    try
                    {
                        if (this.IsConnected)
                            _ = this.SendAsync(request, CancellationToken.None);
                    }
                    catch (Exception)
                    {
                        //
                    }

                    //_process?.WaitForExitAsync();
                    this.IsConnected = false;
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
