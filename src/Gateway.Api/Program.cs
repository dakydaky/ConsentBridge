using Gateway.Domain;
SubmittedAt = DateTime.UtcNow,
PayloadHash = Guid.NewGuid().ToString("N")
};
db.Applications.Add(appRec);
await db.SaveChangesAsync();


// Forward to MockBoard
var client = httpFactory.CreateClient("mockboard");
var resp = await client.PostAsJsonAsync("/v1/mock/applications", new
{
application_id = appRec.Id,
job_external_id = payload.Job.ExternalId,
candidate_id = consent.CandidateId,
payload = payload
});


if (resp.IsSuccessStatusCode)
{
appRec.Status = ApplicationStatus.Accepted;
appRec.Receipt = await resp.Content.ReadAsStringAsync();
await db.SaveChangesAsync();
return Results.Accepted($"/v1/applications/{appRec.Id}", new { id = appRec.Id, status = appRec.Status });
}


appRec.Status = ApplicationStatus.Failed;
await db.SaveChangesAsync();
return Results.StatusCode(502);
});


// Get application
app.MapGet("/v1/applications/{id:guid}", async (Guid id, GatewayDbContext db) =>
{
var appRec = await db.Applications.FindAsync(id);
return appRec is null ? Results.NotFound() : Results.Ok(appRec);
});


// Revoke
app.MapPost("/v1/consents/{id:guid}/revoke", async (Guid id, GatewayDbContext db) =>
{
var consent = await db.Consents.FindAsync(id);
if (consent is null) return Results.NotFound();
consent.Status = ConsentStatus.Revoked;
consent.RevokedAt = DateTime.UtcNow;
await db.SaveChangesAsync();
return Results.NoContent();
});


// HttpClient for MockBoard
builder.Services.AddHttpClient("mockboard", c =>
{
// In docker-compose the service name is mockboard
c.BaseAddress = new Uri(Environment.GetEnvironmentVariable("MOCKBOARD_URL") ?? "http://mockboard:8081");
});


app.Run();