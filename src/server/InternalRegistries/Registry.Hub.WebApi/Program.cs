using System.Collections.Concurrent;


var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5000"); // barcha interfeyslar uchun tinglaydi
var app = builder.Build();

var store = new ConcurrentDictionary<string, List<StoredRegistration>>();

// Ro'yxatdan o'tkazish
app.MapPost("/register", (RegistrationDto reg) =>
{
    var expires = DateTime.UtcNow.AddSeconds(Math.Max(5, reg.TtlSeconds));
    var entry = new StoredRegistration(reg.Service, reg.Ip, reg.Port, expires, reg.Meta);

    store.AddOrUpdate(reg.Service,
        _ => new List<StoredRegistration> { entry },
        (_, list) =>
        {
            list.RemoveAll(e => e.Ip == reg.Ip && e.Port == reg.Port);
            list.Add(entry);
            return list;
        });

    return Results.Ok(new { status = "registered", expiresAt = expires });
});

// Deregistratsiya
app.MapPost("/deregister", (RegistrationDto reg) =>
{
    if (store.TryGetValue(reg.Service, out var list))
    {
        list.RemoveAll(e => e.Ip == reg.Ip && e.Port == reg.Port);
        if (list.Count == 0) store.TryRemove(reg.Service, out _);
    }
    return Results.Ok(new { status = "deregistered" });
});

// Discover
app.MapGet("/discover", (string service) =>
{
    if (!store.TryGetValue(service, out var list)) return Results.Json(Array.Empty<object>());
    var now = DateTime.UtcNow;
    var active = list
        .Where(e => e.ExpiresAt > now)
        .Select(e => new
        {
            e.Service,
            e.Ip,
            e.Port,
            ttlSeconds = (int)(e.ExpiresAt - now).TotalSeconds,
            e.Meta
        })
        .ToArray();
    return Results.Json(active);
});

// Avtomatik tozalash
var cleanupCts = new CancellationTokenSource();
_ = Task.Run(async () =>
{
    while (!cleanupCts.Token.IsCancellationRequested)
    {
        var now = DateTime.UtcNow;
        foreach (var key in store.Keys)
        {
            if (store.TryGetValue(key, out var list))
            {
                list.RemoveAll(e => e.ExpiresAt <= now);
                if (list.Count == 0) store.TryRemove(key, out _);
            }
        }
        await Task.Delay(TimeSpan.FromSeconds(5), cleanupCts.Token);
    }
}, cleanupCts.Token);

app.Lifetime.ApplicationStopping.Register(() => cleanupCts.Cancel());
app.Run();


record RegistrationDto(string Service, string Ip, int Port, int TtlSeconds, Dictionary<string, string>? Meta);
record StoredRegistration(string Service, string Ip, int Port, DateTime ExpiresAt, Dictionary<string, string>? Meta);