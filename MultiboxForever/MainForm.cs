using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MultiboxForever;

internal sealed class MainForm : Form
{
    private readonly RadioButton _hostModeRadio;
    private readonly RadioButton _listenerModeRadio;
    private readonly TextBox _hostIpBox;
    private readonly NumericUpDown _portBox;
    private readonly Button _primaryButton;
    private readonly Label _statusLabel;
    private readonly Label _detailLabel;
    private readonly Label _lastKeyLabel;
    private readonly Label _clientCountLabel;
    private readonly Label _hostIpLabel;
    private readonly Label _keysHintLabel;

    private readonly HostServer _host = new();
    private readonly ListenerClient _listener = new();
    private readonly KeyboardHook _hook = new();

    private bool _busy;

    public MainForm()
    {
        Text = "Multibox Forever";
        MinimumSize = new Size(480, 448);
        Size = new Size(520, 488);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Font = new Font("Segoe UI", 9.75f);

        var machineIpLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 28,
            Text = DescribeLocalAddresses(),
            Font = new Font("Segoe UI Semibold", 11f),
            Padding = new Padding(16, 0, 16, 4)
        };

        var title = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 36,
            Text = "Multibox Forever",
            Font = new Font("Segoe UI Semibold", 16f),
            Padding = new Padding(16, 12, 16, 0)
        };

        var subtitle = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 40,
            Text = "Broadcast Ctrl+Alt key chords from one PC and inject the bare keys on others over your LAN.",
            Padding = new Padding(16, 0, 16, 8)
        };

        var modePanel = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(16, 4, 16, 4) };
        _hostModeRadio = new RadioButton
        {
            Text = "Host (broadcaster)",
            AutoSize = true,
            Checked = true,
            Location = new Point(16, 10)
        };
        _listenerModeRadio = new RadioButton
        {
            Text = "Listener",
            AutoSize = true,
            Location = new Point(200, 10)
        };
        _hostModeRadio.CheckedChanged += (_, _) => UpdateModeUi();
        _listenerModeRadio.CheckedChanged += (_, _) => UpdateModeUi();
        modePanel.Controls.Add(_hostModeRadio);
        modePanel.Controls.Add(_listenerModeRadio);

        var fieldsPanel = new Panel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(16, 4, 16, 4) };
        _hostIpLabel = new Label { Text = "Host IP", AutoSize = true, Location = new Point(16, 8) };
        _hostIpBox = new TextBox
        {
            Width = 200,
            Location = new Point(16, 30),
            Text = "127.0.0.1",
            PlaceholderText = "e.g. 192.168.1.20"
        };
        var portLabel = new Label { Text = "Port", AutoSize = true, Location = new Point(240, 8) };
        _portBox = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 65535,
            Value = Protocol.DefaultPort,
            Width = 100,
            Location = new Point(240, 30)
        };
        fieldsPanel.Controls.Add(portLabel);
        fieldsPanel.Controls.Add(_portBox);
        fieldsPanel.Controls.Add(_hostIpLabel);
        fieldsPanel.Controls.Add(_hostIpBox);

        var actionPanel = new Panel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(16, 4, 16, 4) };
        _primaryButton = new Button
        {
            Text = "Start host",
            Width = 140,
            Height = 32,
            Location = new Point(16, 8)
        };
        _primaryButton.Click += async (_, _) => await OnPrimaryClickAsync();
        actionPanel.Controls.Add(_primaryButton);

        var statusPanel = new Panel { Dock = DockStyle.Top, Height = 120, Padding = new Padding(16, 4, 16, 4) };
        var statusCaption = new Label { Text = "Status", AutoSize = true, Location = new Point(16, 4), Font = new Font("Segoe UI Semibold", 9.75f) };
        _statusLabel = new Label
        {
            AutoSize = false,
            Width = 460,
            Height = 40,
            Location = new Point(16, 26),
            Text = "Idle — choose Host or Listener, then start."
        };
        _detailLabel = new Label
        {
            AutoSize = false,
            Width = 460,
            Height = 22,
            Location = new Point(16, 68),
            Text = $"Default port: {Protocol.DefaultPort}"
        };
        _clientCountLabel = new Label
        {
            AutoSize = false,
            Width = 460,
            Height = 22,
            Location = new Point(16, 90),
            Text = "Listeners connected: 0"
        };
        statusPanel.Controls.Add(statusCaption);
        statusPanel.Controls.Add(_statusLabel);
        statusPanel.Controls.Add(_detailLabel);
        statusPanel.Controls.Add(_clientCountLabel);

        var footerPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 12) };
        _lastKeyLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 24,
            Text = "Last key: —"
        };
        _keysHintLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text =
                "Host chords: Ctrl+Alt+0…9, Ctrl+Alt+- (Minus), Ctrl+Alt+= (Plus on US layout).\n" +
                "Listener injects only that key — modifiers are stripped. Visible helper window; no stealth."
        };
        footerPanel.Controls.Add(_keysHintLabel);
        footerPanel.Controls.Add(_lastKeyLabel);

        Controls.Add(footerPanel);
        Controls.Add(statusPanel);
        Controls.Add(actionPanel);
        Controls.Add(fieldsPanel);
        Controls.Add(modePanel);
        Controls.Add(machineIpLabel);
        Controls.Add(subtitle);
        Controls.Add(title);

        _host.StatusChanged += msg => Ui(() => _statusLabel.Text = msg);
        _host.ClientCountChanged += count => Ui(() => _clientCountLabel.Text = $"Listeners connected: {count}");
        _host.KeyBroadcast += key => Ui(() => _lastKeyLabel.Text = $"Last key broadcast: {Protocol.DescribeKey(key)}");

        _listener.StatusChanged += msg => Ui(() =>
        {
            _statusLabel.Text = msg;
            RefreshPrimaryButtonText();
        });
        _listener.KeyReceived += key => Ui(() => _lastKeyLabel.Text = $"Last key injected: {Protocol.DescribeKey(key)}");

        _hook.ChordPressed += key => _ = OnHostChordAsync(key);

        FormClosing += async (_, e) =>
        {
            if (_busy)
            {
                e.Cancel = true;
                return;
            }

            await SafeShutdownAsync();
        };

        UpdateModeUi();
    }

    private void UpdateModeUi()
    {
        var hostMode = _hostModeRadio.Checked;
        var running = hostMode ? _host.IsRunning : _listener.IsConnected;

        _hostIpLabel.Visible = !hostMode;
        _hostIpBox.Visible = !hostMode;
        _hostIpBox.Enabled = !running && !hostMode;
        _portBox.Enabled = !running;
        _hostModeRadio.Enabled = !running;
        _listenerModeRadio.Enabled = !running;
        _clientCountLabel.Visible = hostMode;

        if (!running)
        {
            _statusLabel.Text = hostMode
                ? "Ready to host. Start listening, then press Ctrl+Alt+key on this PC."
                : "Ready to listen. Enter the host IP and connect.";
            _detailLabel.Text = hostMode
                ? $"This PC will accept TCP connections on port {_portBox.Value}."
                : $"Will connect to {_hostIpBox.Text}:{_portBox.Value}.";
            _lastKeyLabel.Text = "Last key: —";
            if (hostMode)
            {
                _clientCountLabel.Text = "Listeners connected: 0";
            }
        }

        RefreshPrimaryButtonText();
    }

    private void RefreshPrimaryButtonText()
    {
        if (_hostModeRadio.Checked)
        {
            _primaryButton.Text = _host.IsRunning ? "Stop host" : "Start host";
        }
        else
        {
            _primaryButton.Text = _listener.IsConnected ? "Disconnect" : "Connect";
        }
    }

    private async Task OnPrimaryClickAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _primaryButton.Enabled = false;
        try
        {
            if (_hostModeRadio.Checked)
            {
                if (_host.IsRunning)
                {
                    await StopHostAsync();
                }
                else
                {
                    await StartHostAsync();
                }
            }
            else
            {
                if (_listener.IsConnected)
                {
                    await _listener.DisconnectAsync();
                }
                else
                {
                    await StartListenerAsync();
                }
            }
        }
        finally
        {
            _busy = false;
            _primaryButton.Enabled = true;
            UpdateModeUi();
        }
    }

    private async Task StartHostAsync()
    {
        var port = (int)_portBox.Value;
        try
        {
            await _host.StartAsync(port);
            _hook.Install();
            _detailLabel.Text = $"Hosting on all interfaces, port {port}. Local tip: 127.0.0.1 or this PC’s LAN IP.";
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            _statusLabel.Text = $"Port {port} is already in use. Choose another port or stop the other app.";
            await _host.StopAsync();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Could not start host: {ex.Message}";
            await SafeStopHostAsync();
        }
    }

    private async Task StopHostAsync()
    {
        _hook.Uninstall();
        await _host.StopAsync();
    }

    private async Task SafeStopHostAsync()
    {
        try
        {
            _hook.Uninstall();
            await _host.StopAsync();
        }
        catch
        {
        }
    }

    private async Task StartListenerAsync()
    {
        var host = _hostIpBox.Text.Trim();
        var port = (int)_portBox.Value;

        if (string.IsNullOrWhiteSpace(host))
        {
            _statusLabel.Text = "Enter the host PC’s IP address.";
            return;
        }

        if (!IPAddress.TryParse(host, out _))
        {
            // Allow hostnames too, but warn on clearly broken tokens.
            if (host.Any(ch => char.IsWhiteSpace(ch)))
            {
                _statusLabel.Text = "Host IP looks invalid. Use something like 192.168.1.20.";
                return;
            }
        }

        try
        {
            await _listener.ConnectAsync(host, port);
            _detailLabel.Text = $"Listening for {Protocol.SupportedKeysDescription}.";
        }
        catch (FormatException)
        {
            _statusLabel.Text = "Invalid host address. Use an IPv4 address such as 192.168.1.20.";
        }
        catch (SocketException ex)
        {
            _statusLabel.Text = ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => $"Connection refused at {host}:{port}. Is the host running?",
                SocketError.TimedOut => $"Timed out reaching {host}:{port}. Check IP, port, and firewall.",
                SocketError.HostUnreachable or SocketError.NetworkUnreachable =>
                    $"Host unreachable ({host}). Confirm both PCs are on the same LAN.",
                _ => $"Could not connect: {ex.Message}"
            };
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = $"Timed out connecting to {host}:{port}.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Could not connect: {ex.Message}";
        }
    }

    private async Task OnHostChordAsync(char key)
    {
        if (!_host.IsRunning)
        {
            return;
        }

        try
        {
            await _host.BroadcastKeyAsync(key);
        }
        catch (Exception ex)
        {
            Ui(() => _statusLabel.Text = $"Broadcast failed: {ex.Message}");
        }
    }

    private async Task SafeShutdownAsync()
    {
        try
        {
            _hook.Dispose();
            await _host.DisposeAsync();
            await _listener.DisposeAsync();
        }
        catch
        {
        }
    }

    private static string DescribeLocalAddresses()
    {
        try
        {
            var ipv4 = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                .Where(nic => nic.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(address => address.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                .Distinct()
                .ToList();

            var usable = ipv4.Where(address => !IsAutomaticPrivate(address)).ToList();
            var shown = usable.Count > 0 ? usable : ipv4;
            return shown.Count == 0
                ? "This PC: no LAN IPv4 address found"
                : "This PC: " + string.Join(", ", shown.Select(address => address.ToString()));
        }
        catch (Exception)
        {
            return "This PC: could not read the IP address";
        }
    }

    private static bool IsAutomaticPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    private void Ui(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }
}
