using System.Net;
using System.Net.Sockets;
using System.Text;

const int DiscoveryPort = 15000;
const int ServicePort = 16000;
const string DiscoverRequest = "DISCOVER_REQUEST";
const string DiscoverResponsePrefix = "DISCOVER_RESPONSE;";

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var localIp = GetLocalIPv4() ?? "127.0.0.1";
Console.WriteLine($"Server starting. Local IP: {localIp} Service port: {ServicePort}");

var udpTask = RunUdpDiscoveryAsync(localIp, ServicePort, cts.Token);
var tcpTask = RunTcpServiceAsync(ServicePort, cts.Token);

await Task.WhenAll(udpTask, tcpTask);

static string? GetLocalIPv4()
{
    foreach (var entry in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
    {
        if (entry.AddressFamily == AddressFamily.InterNetwork)
            return entry.ToString();
    }
    return null;
}

static async Task RunUdpDiscoveryAsync(string ip, int servicePort, CancellationToken ct)
{
    using var udp = new UdpClient(DiscoveryPort);
    udp.EnableBroadcast = true;
    Console.WriteLine($"UDP discovery listening on port {DiscoveryPort}");

    while (!ct.IsCancellationRequested)
    {
        try
        {
            var res = await udp.ReceiveAsync(ct);
            var message = Encoding.UTF8.GetString(res.Buffer).Trim();
            Console.WriteLine($"UDP from {res.RemoteEndPoint}: {message}");

            if (message == DiscoverRequest)
            {
                var payload = $"{DiscoverResponsePrefix}{ip};{servicePort}";
                var bytes = Encoding.UTF8.GetBytes(payload);
                await udp.SendAsync(bytes, bytes.Length, res.RemoteEndPoint);
                Console.WriteLine($"Sent discovery response to {res.RemoteEndPoint}");
            }
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex)
        {
            Console.WriteLine($"UDP error: {ex.Message}");
        }
    }

    Console.WriteLine("UDP discovery stopped");
}

static async Task RunTcpServiceAsync(int port, CancellationToken ct)
{
    var listener = new TcpListener(IPAddress.Any, port);
    listener.Start();
    Console.WriteLine($"TCP service listening on port {port}");

    try
    {
        while (!ct.IsCancellationRequested)
        {
            var acceptTask = listener.AcceptTcpClientAsync();
            var completed = await Task.WhenAny(acceptTask, Task.Delay(-1, ct));
            if (completed == acceptTask)
            {
                var client = acceptTask.Result;
                _ = HandleClientAsync(client, ct);
            }
        }
    }
    catch (OperationCanceledException) { }
    finally
    {
        listener.Stop();
        Console.WriteLine("TCP service stopped");
    }
}

static async Task HandleClientAsync(TcpClient client, CancellationToken ct)
{
    var remote = client.Client.RemoteEndPoint;
    Console.WriteLine($"TCP connected: {remote}");
    try
    {
        using var ns = client.GetStream();
        var greeting = Encoding.UTF8.GetBytes("HELLO_FROM_SERVER");
        await ns.WriteAsync(greeting.AsMemory(), ct);

        var buf = new byte[4096];
        var read = await ns.ReadAsync(buf.AsMemory(0, buf.Length), ct);
        if (read > 0)
        {
            var text = Encoding.UTF8.GetString(buf, 0, read);
            Console.WriteLine($"Received from {remote}: {text}");
            var reply = Encoding.UTF8.GetBytes("ECHO_FROM_SERVER:" + text);
            await ns.WriteAsync(reply.AsMemory(), ct);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Client handler error: {ex.Message}");
    }
    finally
    {
        client.Close();
        Console.WriteLine($"TCP disconnected: {remote}");
    }
}
