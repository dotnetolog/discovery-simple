using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

const string MulticastAddress = "239.0.0.222";
const int DiscoveryPort = 15000;
const string DiscoverRequest = "DISCOVER_REQUEST";
const string DiscoverResponsePrefix = "DISCOVER_RESPONSE_JSON;";
const int DiscoveryAttempts = 3;
const int DiscoveryTimeoutMs = 1000;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var found = await DiscoverServerMulticastAsync(cts.Token);
if (found is null)
{
    Console.WriteLine("Client: no server discovered");
    return;
}

Console.WriteLine("Client: discovered configuration:");
Console.WriteLine(found);

static async Task<string?> DiscoverServerMulticastAsync(CancellationToken ct)
{
    var multicastIp = IPAddress.Parse(MulticastAddress);

    using var udp = new UdpClient(AddressFamily.InterNetwork);
    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

    udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
    try { udp.JoinMulticastGroup(multicastIp); } catch { }

    var groupEP = new IPEndPoint(multicastIp, DiscoveryPort);
    var reqBytes = Encoding.UTF8.GetBytes(DiscoverRequest);

    for (int attempt = 1; attempt <= DiscoveryAttempts && !ct.IsCancellationRequested; attempt++)
    {
        try
        {
            Console.WriteLine($"Client: discovery attempt {attempt}/{DiscoveryAttempts}");
            await udp.SendAsync(reqBytes, reqBytes.Length, groupEP);

            var receiveTask = udp.ReceiveAsync();
            var delayTask = Task.Delay(DiscoveryTimeoutMs, ct);
            var done = await Task.WhenAny(receiveTask, delayTask);
            if (done == receiveTask)
            {
                var res = receiveTask.Result;
                var msg = Encoding.UTF8.GetString(res.Buffer).Trim();
                Console.WriteLine($"Client: recv '{msg}' from {res.RemoteEndPoint}");
                if (msg.StartsWith(DiscoverResponsePrefix))
                {
                    var json = msg.Substring(DiscoverResponsePrefix.Length);
                    // Optionally validate/parse JSON
                    using var doc = JsonDocument.Parse(json);
                    var schema = doc.RootElement.GetProperty("schema").GetString();
                    var ip = doc.RootElement.GetProperty("ip").GetString();
                    var port = doc.RootElement.GetProperty("port").GetInt32();
                    return JsonSerializer.Serialize(new { schema, ip, port }, new JsonSerializerOptions { WriteIndented = true });
                }
            }
            else
            {
                Console.WriteLine("Client: no response, retrying...");
            }
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex) { Console.WriteLine($"Client UDP error: {ex.Message}"); }
    }

    try { udp.DropMulticastGroup(multicastIp); } catch { }
    return null;
}
