using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MultiboxForever;

/// <summary>
/// TCP host: listens for listeners and broadcasts digit lines to every connected client.
/// </summary>
internal sealed class HostServer : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private int _started;

    public event Action<string>? StatusChanged;
    public event Action<int>? ClientCountChanged;
    public event Action<char>? KeyBroadcast;

    public bool IsRunning => _started == 1;
    public int ClientCount => _clients.Count;

    public async Task StartAsync(int port)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            throw new InvalidOperationException("Host is already running.");
        }

        _cts = new CancellationTokenSource();
        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
        }
        catch
        {
            Interlocked.Exchange(ref _started, 0);
            _cts.Dispose();
            _cts = null;
            _listener = null;
            throw;
        }

        RaiseStatus($"Listening on port {port}. Waiting for listeners…");
        RaiseClientCount();
        _acceptLoop = AcceptLoopAsync(_cts.Token);
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
        {
            return;
        }

        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        try
        {
            _listener?.Stop();
        }
        catch
        {
            // Ignoring stop races during teardown.
        }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        foreach (var id in _clients.Keys)
        {
            if (_clients.TryRemove(id, out var client))
            {
                await client.DisposeAsync();
            }
        }

        _cts?.Dispose();
        _cts = null;
        _listener = null;
        _acceptLoop = null;
        RaiseClientCount();
        RaiseStatus("Host stopped.");
    }

    public async Task BroadcastKeyAsync(char key)
    {
        if (!IsRunning)
        {
            return;
        }

        var payload = Encoding.ASCII.GetBytes(Protocol.FormatKey(key));
        KeyBroadcast?.Invoke(key);

        var stale = new List<Guid>();
        foreach (var pair in _clients)
        {
            try
            {
                await pair.Value.SendAsync(payload);
            }
            catch
            {
                stale.Add(pair.Key);
            }
        }

        foreach (var id in stale)
        {
            if (_clients.TryRemove(id, out var client))
            {
                await client.DisposeAsync();
                RaiseStatus($"Listener disconnected ({ClientCount} remaining).");
            }
        }

        if (stale.Count > 0)
        {
            RaiseClientCount();
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? tcp = null;
            try
            {
                tcp = await _listener!.AcceptTcpClientAsync(cancellationToken);
                var id = Guid.NewGuid();
                var connection = new ClientConnection(id, tcp);
                tcp = null;
                _clients[id] = connection;
                RaiseClientCount();
                RaiseStatus($"Listener connected from {connection.RemoteEndPoint} ({ClientCount} total).");
                _ = WatchClientAsync(connection, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                RaiseStatus($"Accept error: {ex.Message}");
                if (tcp is not null)
                {
                    tcp.Dispose();
                }
            }
        }
    }

    private async Task WatchClientAsync(ClientConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            // Keep the socket until remote closes or host stops. We don't expect inbound data.
            var buffer = new byte[1];
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await connection.Stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
                if (read == 0)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // Disconnect path.
        }
        finally
        {
            if (_clients.TryRemove(connection.Id, out var removed))
            {
                await removed.DisposeAsync();
                RaiseClientCount();
                if (IsRunning)
                {
                    RaiseStatus($"Listener disconnected ({ClientCount} remaining).");
                }
            }
        }
    }

    private void RaiseStatus(string message) => StatusChanged?.Invoke(message);

    private void RaiseClientCount() => ClientCountChanged?.Invoke(ClientCount);

    public async ValueTask DisposeAsync() => await StopAsync();

    private sealed class ClientConnection : IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public ClientConnection(Guid id, TcpClient client)
        {
            Id = id;
            _client = client;
            _client.NoDelay = true;
            Stream = _client.GetStream();
            RemoteEndPoint = _client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        }

        public Guid Id { get; }
        public NetworkStream Stream { get; }
        public string RemoteEndPoint { get; }

        public async Task SendAsync(byte[] payload)
        {
            await _sendLock.WaitAsync();
            try
            {
                await Stream.WriteAsync(payload);
                await Stream.FlushAsync();
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _client.Close();
            }
            catch
            {
            }

            _client.Dispose();
            _sendLock.Dispose();
            await Task.CompletedTask;
        }
    }
}
