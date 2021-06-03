using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
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

        private string _command;
        private string _arguments;

        private Process _process;
        private Stream _pipeInput;
        private Stream _pipeOutput;

        private JsonSerializerOptions _jsonOptions;

        private SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        #endregion

        #region Constructors

        public PipeCommunicator(string command, string arguments)
        {
            _command = command;
            _arguments = arguments;

            _jsonOptions = new JsonSerializerOptions();
            _jsonOptions.Converters.Add(new TimeSpanConverter());
            _jsonOptions.Converters.Add(new JsonStringEnumConverter());
        }

        #endregion

        #region Properties

        public bool IsConnected { get; private set; }

        #endregion

        #region Methods

        public void Connect()
        {
            // start process
            var psi = new ProcessStartInfo(_command)
            {
                Arguments = _arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true
            };

            _process = new Process() { StartInfo = psi };
            _process.Start();

            _pipeInput = _process.StandardInput.BaseStream;
            _pipeOutput = _process.StandardOutput.BaseStream;

            this.IsConnected = true;
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
                var base64Bytes = this.ToBase64Bytes(requestBytes.Length);

                await _pipeInput.WriteAsync(base64Bytes);
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
                var requestLengthBase64 = this.ToBase64Bytes(requestBytes.Length);

                await _pipeInput.WriteAsync(requestLengthBase64, cancellationToken);
                await _pipeInput.WriteAsync(requestBytes, cancellationToken);
                await _pipeInput.FlushAsync();

                cancellationToken.ThrowIfCancellationRequested();

                // read response length
                var responseLengthBytes = new byte[8];
                var responseLengthCount = await _pipeOutput.ReadAsync(responseLengthBytes, cancellationToken);
                this.ValidateResponse(responseLengthCount, expectedCount: 8);

                var responseLength = this.FromBase64Bytes(responseLengthBytes);

                if (responseLength == 0)
                {
                    this.IsConnected = false;
                    throw new PipeProtocolException("Invalid number of bytes received.");
                }

                // read response
                using var responseBytes = MemoryPool<byte>.Shared.Rent(responseLength);
                var responseCount = await _pipeOutput.ReadAsync(responseBytes.Memory, cancellationToken);
                this.ValidateResponse(responseCount, expectedCount: responseLength);

                cancellationToken.ThrowIfCancellationRequested();

                var reponse = JsonSerializer.Deserialize<TResponse>(responseBytes.Memory.Span.Slice(0, responseCount), _jsonOptions);
                return reponse;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private void ValidateResponse(int readCount, int expectedCount)
        {
            if (readCount == 0)
            {
                this.IsConnected = false;
                _process?.Kill();
                throw new PipeProtocolException("The connection aborted unexpectedly.");
            }
            else if (readCount != expectedCount)
            {
                throw new PipeProtocolException("Invalid number of bytes received.");
            }
        }

        private int FromBase64Bytes(byte[] base64Bytes)
        {
            var base64 = Encoding.ASCII.GetString(base64Bytes);
            var normalBytes = Convert.FromBase64String(base64);

            if (!BitConverter.IsLittleEndian)
                Array.Reverse(normalBytes);

            return BitConverter.ToInt32(normalBytes);
        }

        private byte[] ToBase64Bytes(int length)
        {
            var normalBytes = BitConverter.GetBytes(length);

            if (!BitConverter.IsLittleEndian)
                Array.Reverse(normalBytes);

            var base64 = Convert.ToBase64String(normalBytes);
            return Encoding.ASCII.GetBytes(base64);
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
                    _ = this.SendAsync(request, CancellationToken.None);
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
