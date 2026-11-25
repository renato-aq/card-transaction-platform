using System.Text.Json;
using System.Text.Json.Serialization;

using CardPlatform.Authorization.Api.Endpoints;
using CardPlatform.Authorization.Application;
using CardPlatform.Authorization.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddAuthorizationInfrastructure();
builder.Services.AddAuthorizationApplication();
builder.Services.AddSingleton(TimeProvider.System);

// Enums travel as camelCase strings (e.g. "blocked"), both in requests and responses.
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
    await app.Services.ApplyAuthorizationMigrationsAsync();
}

app.MapDefaultEndpoints();
app.MapCardEndpoints();

await app.RunAsync();

// Exposes the entry point to WebApplicationFactory in integration tests.
public partial class Program;
