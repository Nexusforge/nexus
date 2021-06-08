using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensions
{
    public class RpcCommunicator
    {
        #region Fields

        private ILogger _logger;

        private uint _invocationId;
        private string _command;
        private string _arguments;
        private object _lock = new object();

        private Process _process;
        private Stream _pipeInput;
        private Stream _pipeOutput;

        private JsonSerializerOptions _jsonOptions;

        private SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        #endregion

        #region Constructors

        public RpcCommunicator(string command, string arguments, ILogger logger)
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

            var request = new HandshakeRequest();
            var response = await this.InvokeInternalAsync<HandshakeRequest, HandshakeResponse>(request, cancellationToken);

            if (!string.IsNullOrWhiteSpace(response.Error))
                throw new RpcException(response.Error);

            this.IsConnected = true;
        }

        public async Task<T> InvokeAsync<T>(string methodName, object[] args, CancellationToken cancellationToken)
        {
            if (!this.IsConnected)
                throw new InvalidOperationException("Cannot communicate in disconnected state.");

            var request = new Invocation(this.GetInvocationId(), methodName, args);
            var response = await this.InvokeInternalAsync<Invocation, Completion<T>>(request, cancellationToken);

            if (response.Type != response.Type)
                throw new RpcException("The reponse message type does not match the expected message type.");

            if (response.InvocationId != response.InvocationId)
                throw new RpcException("The reponse invocation ID does not match the expected invocation ID.");

            if (response.Result is not null && response.Error is not null)
                throw new RpcException("It is a protocol error for a Callee to send a Completion message carrying both a result and an error.");

            return response.Result;
        }

        public Task SendAsync(string methodName, object[] args, CancellationToken cancellationToken)
        {
            if (!this.IsConnected)
                throw new InvalidOperationException("Cannot communicate in disconnected state.");

            var request = new Invocation(null, methodName, args);
            return this.InternalSendAsync(request, cancellationToken);
        }

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            var request = new Close(null);
            await this.InternalSendAsync(request, cancellationToken);
            this.IsConnected = false;
        }

        private async Task InternalSendAsync<TRequest>(TRequest request, CancellationToken cancellationToken)
        {
            try
            {
                await _semaphore.WaitAsync(cancellationToken);

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

        private async Task<TResponse> InvokeInternalAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken) 
        {
            try
            {
                await _semaphore.WaitAsync(cancellationToken);

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
                    throw new RpcException("Invalid number of bytes received.");
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
            // Cancellation token only works when the child process writes data,
            // otherwise it hangs forever.
            var memory = new CastMemoryManager<T, byte>(buffer).Memory;

            while (memory.Length > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var byteCount = await _pipeOutput.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
                this.ValidateResponse(byteCount);
                memory = memory.Slice(byteCount);
            }
        }

        private void ValidateResponse(int readCount)
        {
            if (readCount == 0)
            {
                this.IsConnected = false;
                _process?.Kill();
                throw new RpcException("The connection aborted unexpectedly.");
            }
        }

        private string GetInvocationId()
        {
            lock (_lock)
            {
                _invocationId++;
                return _invocationId.ToString();
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
                    try
                    {
                        if (this.IsConnected)
                            _ = this.CloseAsync(CancellationToken.None);
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
