using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

const string MulticastAddress = "239.0.0.222";
const int DiscoveryPort = 15000;
const int ServicePort = 16000;
const string DiscoverRequest = "DISCOVER_REQUEST";
const string DiscoverResponsePrefix = "DISCOVER_RESPONSE_JSON;";

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var localIp = GetPreferredLocalIPv4() ?? "127.0.0.1";
Console.WriteLine($"Server: local IP {localIp}, service port {ServicePort}");

await RunUdpMulticastListenerAsync(localIp, ServicePort, cts.Token);

// Helpers
static string? GetPreferredLocalIPv4()
{
    try
    {
        var entries = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
        foreach (var a in entries)
            if (a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254"))
                return a.ToString();
        foreach (var a in entries)
            if (a.AddressFamily == AddressFamily.InterNetwork)
                return a.ToString();
    }
    catch { }
    return null;
}

static async Task RunUdpMulticastListenerAsync(string localIp, int servicePort, CancellationToken ct)
{
    var multicastIp = IPAddress.Parse(MulticastAddress);
    using var udp = new UdpClient(AddressFamily.InterNetwork);

    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
    udp.JoinMulticastGroup(multicastIp);
    Console.WriteLine($"Server: joined multicast {MulticastAddress}:{DiscoveryPort}");

    while (!ct.IsCancellationRequested)
    {
        try
        {
            var res = await udp.ReceiveAsync(ct);
            var msg = Encoding.UTF8.GetString(res.Buffer).Trim();
            Console.WriteLine($"Server: recv '{msg}' from {res.RemoteEndPoint}");
            if (msg == DiscoverRequest)
            {
                var cfg = new
                {
                    schema = "tcp",
                    ip = localIp,
                    port = servicePort
                };
                var json = JsonSerializer.Serialize(cfg);
                var payload = DiscoverResponsePrefix + json;
                var bytes = Encoding.UTF8.GetBytes(payload);
                await udp.SendAsync(bytes, bytes.Length, res.RemoteEndPoint);
                Console.WriteLine($"Server: sent config to {res.RemoteEndPoint}");
            }
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex) { Console.WriteLine($"Server UDP error: {ex.Message}"); }
    }

    try { udp.DropMulticastGroup(multicastIp); } catch { }
    Console.WriteLine("Server: stopped");
}
