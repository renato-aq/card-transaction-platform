using System.Text.Json;
using System.Text.Json.Serialization;

using CardPlatform.Ledger.Api.Endpoints;
using CardPlatform.Ledger.Application;
using CardPlatform.Ledger.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddLedgerInfrastructure();
builder.Services.AddLedgerApplication();
builder.Services.AddSingleton(TimeProvider.System);

// Enums travel as camelCase strings (e.g. "card"), both in requests and responses.
builder.Services.ConfigureHttpJsonOptions(static options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

// Malformed requests (invalid JSON, missing body) get a 400 ProblemDetails instead of an exception, in every environment.
builder.Services.Configure<RouteHandlerOptions>(static options => options.ThrowOnBadRequest = false);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Development only: other environments apply migrations as an explicit deployment step.
    await app.Services.ApplyLedgerMigrationsAsync();
}

app.MapDefaultEndpoints();
app.MapAccountEndpoints();
app.MapTransactionEndpoints();

await app.RunAsync();

// Exposes the entry point to WebApplicationFactory in integration tests.
public partial class Program;
