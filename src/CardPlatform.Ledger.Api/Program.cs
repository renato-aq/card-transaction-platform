using CardPlatform.Ledger.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddLedgerInfrastructure();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Development only: other environments apply migrations as an explicit deployment step.
    await app.Services.ApplyLedgerMigrationsAsync();
}

app.MapDefaultEndpoints();

await app.RunAsync();

// Exposes the entry point to WebApplicationFactory in integration tests.
public partial class Program;
