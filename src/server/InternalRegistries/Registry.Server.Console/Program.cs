using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;

const string RegistryUrl = "http://localhost:5000";
const string ServiceName = "my-service";
const int ServicePort = 8080;
const int TtlSeconds = 30;

var ip = GetLocalIPv4() ?? "127.0.0.1";
var http = new HttpClient { BaseAddress = new Uri(RegistryUrl) };

async Task RegisterAsync()
{
    var payload = new
    {
        service = ServiceName,
        ip,
        port = ServicePort,
        ttlSeconds = TtlSeconds,
        meta = new Dictionary<string, string> { { "version", "1.0" } }
    };
    await http.PostAsJsonAsync("/register", payload);
}

var listener = new TcpListener(IPAddress.Any, ServicePort);
listener.Start();
Console.WriteLine($"TCP Echo server started on {ip}:{ServicePort}");

_ = Task.Run(async () =>
{
    while (true)
    {
        await RegisterAsync();
        await Task.Delay(TimeSpan.FromSeconds(10));
    }
});

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(async () =>
    {
        var stream = client.GetStream();
        var buffer = new byte[1024];
        while (true)
        {
            var bytes = await stream.ReadAsync(buffer);
            if (bytes == 0) break;
            var msg = Encoding.UTF8.GetString(buffer, 0, bytes);
            Console.WriteLine($"Received: {msg.Trim()}");
            await stream.WriteAsync(buffer.AsMemory(0, bytes));
        }
        client.Close();
    });
}

static string? GetLocalIPv4()
{
    var host = Dns.GetHostEntry(Dns.GetHostName());
    foreach (var ip in host.AddressList)
        if (ip.AddressFamily == AddressFamily.InterNetwork && !ip.ToString().StartsWith("169.254"))
            return ip.ToString();
    return null;
}
