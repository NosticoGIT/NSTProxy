using NSTProxy;

const int ListenPort = 5000;

var store = new EndpointMappingStore();

// -- Web Server ---------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls($"http://localhost:{ListenPort}");

builder.Services.AddWindowsService();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();

// GET / - list all active endpoint mappings
app.MapGet("/", () =>
{
    var mappings = store.GetAll();
    return Results.Ok(new
    {
        endpoints = new object[]
        {
            new { url = $"http://localhost:{ListenPort}/TEXT", resource = "textprint.txt", type = "File" }
        }
        .Concat(mappings.Select(m => (object)new
        {
            url      = $"http://localhost:{ListenPort}/{m.EndpointName}",
            resource = m.ResourceName,
            type     = m.ResourceType.ToString()
        }))
    });
});

// POST /TEXT - append raw data to textprint.txt for testing without a printer
var textPrintPath = Path.Combine(AppContext.BaseDirectory, "textprint.txt");
app.MapPost("/TEXT", async (HttpContext context) =>
{
    using var ms = new MemoryStream();
    await context.Request.Body.CopyToAsync(ms);
    var data = ms.ToArray();

    if (data.Length == 0)
        return Results.BadRequest(new { error = "Request body is empty." });

    var contentType = context.Request.ContentType ?? "";
    if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                var raw = dataElement.GetString() ?? "";
                try
                {
                    data = Convert.FromBase64String(raw);
                }
                catch (FormatException)
                {
                    data = System.Text.Encoding.Latin1.GetBytes(raw);
                }
                if (data.Length == 0)
                    return Results.BadRequest(new { error = "The \"data\" field is empty." });
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not valid JSON — fall through and send raw bytes
        }
    }

    try
    {
        await File.AppendAllTextAsync(textPrintPath,
            $"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss} ({data.Length} bytes) ---{Environment.NewLine}" +
            System.Text.Encoding.Latin1.GetString(data) +
            Environment.NewLine);
        return Results.Ok(new { message = $"Appended {data.Length} bytes to textprint.txt." });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

// POST /{endpointName} - forward raw body to the mapped resource
app.MapPost("/{endpointName}", async (string endpointName, HttpContext context) =>
{
    var mapping = store.Get(endpointName);
    if (mapping is null)
        return Results.NotFound(new { error = $"No resource mapped to '/{endpointName}'." });

    using var ms = new MemoryStream();
    await context.Request.Body.CopyToAsync(ms);
    var data = ms.ToArray();

    if (data.Length == 0)
        return Results.BadRequest(new { error = "Request body is empty." });

    // If the client sent JSON, extract the "data" field as raw bytes
    var contentType = context.Request.ContentType ?? "";
    if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                var raw = dataElement.GetString() ?? "";
                try
                {
                    data = Convert.FromBase64String(raw);
                }
                catch (FormatException)
                {
                    data = System.Text.Encoding.Latin1.GetBytes(raw);
                }
                if (data.Length == 0)
                    return Results.BadRequest(new { error = "The \"data\" field is empty." });
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not valid JSON — fall through and send raw bytes
        }
    }

    try
    {
        var success = ResourceManager.SendData(mapping, data);
        return success
            ? Results.Ok(new { message = $"Sent {data.Length} bytes to {mapping.ResourceType} '{mapping.ResourceName}'." })
            : Results.Json(new { error = $"Failed to send data to '{mapping.ResourceName}'." }, statusCode: 500);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

// -- Start --------------------------------------------------------------------
if (!Environment.UserInteractive)
{
    // Running as a Windows Service — just run and block until stopped
    await app.RunAsync();
    return;
}

// Start web server in background
var cts = new CancellationTokenSource();
_ = app.RunAsync(cts.Token);
await Task.Delay(600);

// -- Interactive Console Menu --------------------------------------------------
Console.Clear();
Console.WriteLine("========================================");
Console.WriteLine("  NSTProxy");
Console.WriteLine($"  Listening on http://localhost:{ListenPort}");
Console.WriteLine("========================================");
Console.WriteLine();

var running = true;
while (running)
{
    Console.WriteLine("-- Menu ------------------------------------");
    Console.WriteLine("  1. List available resources");
    Console.WriteLine("  2. List active endpoints");
    Console.WriteLine("  3. Add endpoint mapping");
    Console.WriteLine("  4. Remove endpoint mapping");
    Console.WriteLine("  5. Exit");
    Console.WriteLine("--------------------------------------------");
    Console.Write("Choice: ");

    switch (Console.ReadLine()?.Trim())
    {
        case "1": ListResources(); break;
        case "2": ListEndpoints(); break;
        case "3": AddMapping();    break;
        case "4": RemoveMapping(); break;
        case "5": running = false; break;
        default:  Console.WriteLine("  Invalid choice."); break;
    }
    Console.WriteLine();
}

cts.Cancel();
Console.WriteLine("Server stopped.");

// -- Menu Actions -------------------------------------------------------------

void ListResources()
{
    var resources = ResourceManager.DiscoverAll();
    if (resources.Count == 0) { Console.WriteLine("  No resources found."); return; }

    Console.WriteLine();
    for (var i = 0; i < resources.Count; i++)
        Console.WriteLine($"  [{i + 1}] ({resources[i].Type}) {resources[i].Name}");
}

void ListEndpoints()
{
    var mappings = store.GetAll();
    if (mappings.Count == 0) { Console.WriteLine("  No endpoints configured."); return; }

    Console.WriteLine();
    for (var i = 0; i < mappings.Count; i++)
    {
        var m = mappings[i];
        Console.WriteLine($"  [{i + 1}] POST http://localhost:{ListenPort}/{m.EndpointName}  ->  ({m.ResourceType}) {m.ResourceName}");
    }
}

void AddMapping()
{
    var resources = ResourceManager.DiscoverAll();
    if (resources.Count == 0) { Console.WriteLine("  No resources available."); return; }

    Console.WriteLine();
    Console.WriteLine("  Available resources:");
    for (var i = 0; i < resources.Count; i++)
        Console.WriteLine($"    [{i + 1}] ({resources[i].Type}) {resources[i].Name}");

    Console.Write("  Select resource #: ");
    if (!int.TryParse(Console.ReadLine()?.Trim(), out var idx) || idx < 1 || idx > resources.Count)
    { Console.WriteLine("  Invalid selection."); return; }

    var selected = resources[idx - 1];

    Console.Write("  Endpoint name (e.g. printer1): ");
    var name = Console.ReadLine()?.Trim().TrimStart('/');
    if (string.IsNullOrWhiteSpace(name))
    { Console.WriteLine("  Name cannot be empty."); return; }

    var mapping = new EndpointMapping
    {
        EndpointName = name,
        ResourceType = selected.Type,
        ResourceName = selected.Name
    };

    Console.WriteLine(store.TryAdd(mapping)
        ? $"  Mapped: POST http://localhost:{ListenPort}/{name}  ->  ({selected.Type}) {selected.Name}"
        : $"  Error: Endpoint '/{name}' already exists.");
}

void RemoveMapping()
{
    var mappings = store.GetAll();
    if (mappings.Count == 0) { Console.WriteLine("  No endpoints to remove."); return; }

    Console.WriteLine();
    for (var i = 0; i < mappings.Count; i++)
        Console.WriteLine($"  [{i + 1}] /{mappings[i].EndpointName}  ->  {mappings[i].ResourceName}");

    Console.Write("  Select endpoint # to remove: ");
    if (!int.TryParse(Console.ReadLine()?.Trim(), out var idx) || idx < 1 || idx > mappings.Count)
    { Console.WriteLine("  Invalid selection."); return; }

    var target = mappings[idx - 1];
    Console.WriteLine(store.TryRemove(target.EndpointName)
        ? $"  Removed /{target.EndpointName}"
        : "  Failed to remove.");
}
