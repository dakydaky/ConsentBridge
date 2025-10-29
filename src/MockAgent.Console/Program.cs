using MockAgent.ConsoleApp;
using MockAgent.ConsoleApp.Api;
using MockAgent.ConsoleApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddHttpClient();
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection("Gateway"));
builder.Services.AddSingleton<DemoState>();
builder.Services.AddScoped<AgentApiClient>();
builder.Services.AddSession();
builder.Services.AddSingleton<SseHub>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();

app.UseAuthorization();

app.UseSession();
app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

// SSE stream for push updates
app.MapGet("/events", async (HttpContext ctx, SseHub hub, CancellationToken ct) =>
{
    await hub.SubscribeAsync(ctx.Response, ct);
});

// Webhook receiver (dev-only)
app.MapPost("/webhooks/consent", (SseHub hub) =>
{
    // Simply broadcast an update event; clients will pull fresh dashboard data
    hub.Broadcast("update");
    return Results.Ok(new { ok = true });
});

app.Run();
