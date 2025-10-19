using System.Net.Sockets;
using System.Text;
using System.Text.Json;

const string RegistryUrl = "http://localhost:5000";
const string ServiceName = "my-service";
const int TcpConnectTimeoutMs = 3000;

using var http = new HttpClient { BaseAddress = new Uri(RegistryUrl) };

var discovered = await DiscoverAsync(ServiceName);
if (discovered is null)
{
    Console.WriteLine("No service discovered.");
    return;
}

Console.WriteLine($"Discovered service: {discovered.Ip}:{discovered.Port} (ttl {discovered.Ttl}s)");

var connected = await TryTcpConnectAsync(discovered.Ip, discovered.Port);
Console.WriteLine(connected ? "Connected successfully." : "Failed to connect.");

async Task<Discovered?> DiscoverAsync(string service)
{
    var res = await http.GetAsync($"/discover?service={Uri.EscapeDataString(service)}");
    if (!res.IsSuccessStatusCode) return null;

    var json = await res.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(json);
    var arr = doc.RootElement;

    if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
        return null;

    var first = arr[0];
    var ip = first.GetProperty("ip").GetString() ?? "";
    var port = first.GetProperty("port").GetInt32();
    var ttl = first.GetProperty("ttlSeconds").GetInt32();

    if (string.IsNullOrWhiteSpace(ip)) return null;

    return new Discovered(ip, port, ttl);
}

async Task<bool> TryTcpConnectAsync(string ip, int port)
{
    try
    {
        using var tcp = new TcpClient();
        var connectTask = tcp.ConnectAsync(ip, port);
        var completed = await Task.WhenAny(connectTask, Task.Delay(TcpConnectTimeoutMs));

        if (completed != connectTask || !tcp.Connected)
            return false;

        using var ns = tcp.GetStream();
        var msg = Encoding.UTF8.GetBytes("HELLO_FROM_CLIENT\n");
        await ns.WriteAsync(msg.AsMemory());

        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"TCP error: {ex.Message}");
        return false;
    }
}

record Discovered(string Ip, int Port, int Ttl);
