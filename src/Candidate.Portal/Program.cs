using Candidate.PortalApp;
using Candidate.PortalApp.Api;
using Candidate.PortalApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpClient();
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection("Gateway"));
builder.Services.AddScoped<AgentApiClient>();
builder.Services.AddSession();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();
app.UseSession();
app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

// Minimal API for profile sharing (demo only)
app.MapGet("/api/profile", (HttpRequest req) =>
{
    var email = req.Query["email"].ToString();
    if (string.IsNullOrWhiteSpace(email) || !ProfileStore.TryGet(email.Trim().ToLowerInvariant(), out var profile))
    {
        return Results.NotFound();
    }
    return Results.Ok(profile);
});
app.MapPost("/api/profile", async (HttpContext ctx) =>
{
    var email = ctx.Session.GetString("CandidateEmail");
    if (string.IsNullOrWhiteSpace(email)) return Results.Unauthorized();
    var profile = await System.Text.Json.JsonSerializer.DeserializeAsync<CandidateProfile>(ctx.Request.Body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    if (profile is null) return Results.BadRequest();
    ProfileStore.Save(email, profile);
    return Results.NoContent();
});

app.Run();
