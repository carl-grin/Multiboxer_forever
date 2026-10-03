using System.Net.Sockets;
using System.Text;

namespace MultiboxForever;

/// <summary>
/// TCP listener client: connects to the host and injects received digit keystrokes.
/// </summary>
internal sealed class ListenerClient : IAsyncDisposable
{
    private TcpClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private int _started;

    public event Action<string>? StatusChanged;
    public event Action<char>? KeyReceived;

    public bool IsConnected => _started == 1 && _client?.Connected == true;

    public async Task ConnectAsync(string host, int port)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            throw new InvalidOperationException("Listener is already connected or connecting.");
        }

        _cts = new CancellationTokenSource();
        var client = new TcpClient();
        try
        {
            RaiseStatus($"Connecting to {host}:{port}…");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
            await client.ConnectAsync(host, port, timeoutCts.Token);
            client.NoDelay = true;
            _client = client;
            RaiseStatus($"Connected to {host}:{port}. Waiting for key events…");
            _receiveLoop = ReceiveLoopAsync(_cts.Token);
        }
        catch
        {
            Interlocked.Exchange(ref _started, 0);
            client.Dispose();
            _client = null;
            _cts.Dispose();
            _cts = null;
            throw;
        }
    }

    public async Task DisconnectAsync()
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
            _client?.Close();
        }
        catch
        {
        }

        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _client?.Dispose();
        _client = null;
        _cts?.Dispose();
        _cts = null;
        _receiveLoop = null;
        RaiseStatus("Disconnected.");
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_client is null)
            {
                return;
            }

            using var reader = new StreamReader(_client.GetStream(), Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    RaiseStatus("Host closed the connection.");
                    break;
                }

                if (!Protocol.TryParseKey(line, out var key))
                {
                    RaiseStatus($"Ignored unexpected message: {Truncate(line)}");
                    continue;
                }

                try
                {
                    KeyInjector.SendKey(key);
                    KeyReceived?.Invoke(key);
                }
                catch (Exception ex)
                {
                    RaiseStatus($"Failed to inject {Protocol.DescribeKey(key)}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                RaiseStatus($"Connection lost: {ex.Message}");
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            RaiseStatus($"Receive error: {ex.Message}");
        }
        finally
        {
            if (Interlocked.Exchange(ref _started, 0) == 1)
            {
                try
                {
                    _client?.Close();
                }
                catch
                {
                }

                _client?.Dispose();
                _client = null;
            }
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 32 ? value : value[..32] + "…";

    private void RaiseStatus(string message) => StatusChanged?.Invoke(message);

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
