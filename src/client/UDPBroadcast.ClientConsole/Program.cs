using System.Net;
using System.Net.Sockets;
using System.Text;

const int DiscoveryPort = 15000;
const string DiscoverRequest = "DISCOVER_REQUEST";
const string DiscoverResponsePrefix = "DISCOVER_RESPONSE;";
const int DiscoveryAttempts = 3;
const int DiscoveryTimeoutMs = 1000;
const int TcpConnectTimeoutMs = 2000;

// Optional preconfigured endpoint. Leave empty to always use discovery.
string configuredIp = "";
int configuredPort = 16000;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

if (!string.IsNullOrWhiteSpace(configuredIp))
{
    Console.WriteLine($"Trying configured endpoint {configuredIp}:{configuredPort}");
    if (await TryTcpConnectAsync(configuredIp, configuredPort, cts.Token))
        return;
    Console.WriteLine("Configured endpoint failed, proceeding to discovery");
}

var found = await DiscoverServerAsync(cts.Token);
if (found is null)
{
    Console.WriteLine("No server discovered");
    return;
}

Console.WriteLine($"Discovered server {found.Value.Address}:{found.Value.Port}");
await TryTcpConnectAsync(found.Value.Address, found.Value.Port, cts.Token);

static async Task<(string Address, int Port)?> DiscoverServerAsync(CancellationToken ct)
{
    using var udp = new UdpClient();
    udp.EnableBroadcast = true;
    var broadcastEP = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
    var reqBytes = Encoding.UTF8.GetBytes(DiscoverRequest);

    for (int attempt = 1; attempt <= DiscoveryAttempts && !ct.IsCancellationRequested; attempt++)
    {
        try
        {
            Console.WriteLine($"Discovery attempt {attempt}/{DiscoveryAttempts}");
            await udp.SendAsync(reqBytes, reqBytes.Length, broadcastEP);

            var receiveTask = udp.ReceiveAsync();
            var delayTask = Task.Delay(DiscoveryTimeoutMs, ct);
            var done = await Task.WhenAny(receiveTask, delayTask);
            if (done == receiveTask)
            {
                var res = receiveTask.Result;
                var msg = Encoding.UTF8.GetString(res.Buffer).Trim();
                Console.WriteLine($"UDP response from {res.RemoteEndPoint}: {msg}");
                if (msg.StartsWith(DiscoverResponsePrefix))
                {
                    var payload = msg.Substring(DiscoverResponsePrefix.Length);
                    var parts = payload.Split(';');
                    if (parts.Length == 2 && int.TryParse(parts[1], out var port))
                        return (parts[0], port);
                }
            }
            else
            {
                Console.WriteLine("No response, retrying...");
            }
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex)
        {
            Console.WriteLine($"Discovery error: {ex.Message}");
        }
    }

    return null;
}

static async Task<bool> TryTcpConnectAsync(string ip, int port, CancellationToken ct)
{
    try
    {
        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(ip, port);
        var timeoutTask = Task.Delay(TcpConnectTimeoutMs, ct);
        var finished = await Task.WhenAny(connectTask, timeoutTask);
        if (finished != connectTask || !client.Connected)
        {
            Console.WriteLine("TCP connect timed out or failed");
            return false;
        }

        using var ns = client.GetStream();
        var buf = new byte[4096];
        var read = await ns.ReadAsync(buf, 0, buf.Length, ct);
        if (read > 0)
        {
            Console.WriteLine($"Server greeting: {Encoding.UTF8.GetString(buf, 0, read)}");
            var hello = Encoding.UTF8.GetBytes("HELLO_FROM_CLIENT");
            await ns.WriteAsync(hello, 0, hello.Length, ct);

            read = await ns.ReadAsync(buf, 0, buf.Length, ct);
            if (read > 0)
                Console.WriteLine($"Server reply: {Encoding.UTF8.GetString(buf, 0, read)}");
        }

        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"TCP connection error: {ex.Message}");
        return false;
    }
}
