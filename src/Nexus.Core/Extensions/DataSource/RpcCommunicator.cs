using Microsoft.Extensions.Logging;
using Nexus.Utilities;
using StreamJsonRpc;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensions
{
    public class RpcCommunicator
    {
        #region Fields

        private static ConcurrentDictionary<Uri, (TcpListener, SemaphoreSlim)> _uriToTcpListenerMap
            = new ConcurrentDictionary<Uri, (TcpListener, SemaphoreSlim)>();

        private SemaphoreSlim _connectSemaphore;
        private TcpListener _tcpListener;
        private Stream _commStream;
        private Stream _dataStream;
        private IJsonRpcServer _rpcServer;

        private ILogger _logger;

        private string _command;
        private string _arguments;

        private Process _process;

        #endregion

        #region Constructors

        public RpcCommunicator(Uri resourceLocator, string command, string arguments, IPAddress listenAddress, ushort listenPort, ILogger logger)
        {
            _command = command;
            _arguments = arguments;
            _logger = logger;

            (_tcpListener, _connectSemaphore) = _uriToTcpListenerMap
                .GetOrAdd(resourceLocator, uri => (new TcpListener(listenAddress, listenPort), new SemaphoreSlim(1, 1)));
        }

        #endregion

        #region Methods

        public async Task<IJsonRpcServer> ConnectAsync(CancellationToken cancellationToken)
        {
            var releaseSempahore = false;

            try
            {
                // start process
                var psi = new ProcessStartInfo(_command)
                {
                    Arguments = _arguments,
                };

                psi.RedirectStandardError = true;

                _process = new Process() { StartInfo = psi };
                _process.Start();

                _process.ErrorDataReceived += (sender, e) => 
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        _logger.LogWarning(e.Data);
                    }
                };

                _process.BeginErrorReadLine();

                // wait for clients to connect
                await _connectSemaphore.WaitAsync(cancellationToken);
                releaseSempahore = true;

                _tcpListener.Start();
                cancellationToken.Register(() => _tcpListener.Stop());

                var filters = new string[] { "comm", "data" };

                Stream commStream = null;
                Stream dataStream = null;

                for (int i = 0; i < 2; i++)
                {
                    var response = await this.GetTcpClientAsync(filters, cancellationToken);

                    if (commStream is null && response.Identifier == "comm")
                        commStream = response.Client.GetStream();

                    else if (dataStream is null && response.Identifier == "data")
                        dataStream = response.Client.GetStream();
                }

                if (commStream is null || dataStream is null)
                    throw new Exception("The RPC server did not connect properly via communication and a data stream. This may indicate that other TCP clients have tried to connect.");

                _commStream = commStream;
                _dataStream = dataStream;

                var messageHandler = new LengthHeaderMessageHandler(commStream, commStream, new JsonMessageFormatter());
                var jsonRpc = new JsonRpc(messageHandler);

                jsonRpc.AddLocalRpcMethod("log", new Action<LogLevel, string>((logLevel, message) =>
                {
                    _logger.Log(logLevel, message);
                }));

                jsonRpc.StartListening();

                _rpcServer = jsonRpc.Attach<IJsonRpcServer>(new JsonRpcProxyOptions()
                {
                    MethodNameTransform = pascalCaseName =>
                    {
                        return char.ToLower(pascalCaseName[0]) + pascalCaseName.Substring(1);
                    }
                });

                return _rpcServer;
            }
            catch
            {
                try
                {
                    _process?.Kill();
                }
                catch {
                    //
                }
                
                throw;
            }
            finally
            {
                _tcpListener.Stop();

                if (releaseSempahore)
                    _connectSemaphore.Release();
            }
        }

        public Task ReadRawAsync<T>(Memory<T> buffer, CancellationToken cancellationToken)
           where T : unmanaged
        {
            var memory = new CastMemoryManager<T, byte>(buffer).Memory;
            return this.InternalReadRawAsync(memory, _dataStream, cancellationToken);
        }

        private async Task InternalReadRawAsync(Memory<byte> buffer, Stream source, CancellationToken cancellationToken)
        {
            while (buffer.Length > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var readCount = await source.ReadAsync(buffer, cancellationToken);

                if (readCount == 0)
                    throw new Exception("The TCP connection closed early.");

                buffer = buffer.Slice(readCount);
            }
        }

        private async Task<(string Identifier, TcpClient Client)> GetTcpClientAsync(string[] filters, CancellationToken cancellationToken)
        {
            var buffer = new byte[4];
            var client = await _tcpListener.AcceptTcpClientAsync();

            await this.InternalReadRawAsync(buffer, client.GetStream(), cancellationToken);

            foreach (var filter in filters)
            {
                if (buffer.SequenceEqual(Encoding.UTF8.GetBytes(filter)))
                    return (filter, client);
            }

            throw new Exception("Invalid stream identifier received.");
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
                        var disposable = _rpcServer as IDisposable;
                        disposable?.Dispose();

                        _commStream?.Dispose();
                        _dataStream?.Dispose();


                        try
                        {
                            _process?.Kill();
                        }
                        catch
                        {
                            //
                        }
                    }
                    catch (Exception)
                    {
                        //
                    }

                    //_process?.WaitForExitAsync();
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
