var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var ledgerDb = postgres.AddDatabase("ledgerdb");
var authorizationDb = postgres.AddDatabase("authorizationdb");
var fraudDb = postgres.AddDatabase("frauddb");

var kafka = builder.AddKafka("kafka")
    .WithDataVolume()
    .WithKafkaUI();

var ledgerApi = builder.AddDotnetProject("ledger-api", "../CardPlatform.Ledger.Api")
    .WithReference(ledgerDb)
    .WaitFor(ledgerDb)
    .WithReference(kafka)
    .WaitFor(kafka);

builder.AddDotnetProject("authorization-api", "../CardPlatform.Authorization.Api")
    .WithReference(authorizationDb)
    .WaitFor(authorizationDb)
    .WithReference(ledgerApi)
    .WaitFor(ledgerApi)
    .WithReference(kafka)
    .WaitFor(kafka);

builder.AddDotnetProject("fraud-worker", "../CardPlatform.Fraud.Worker")
    .WithReference(fraudDb)
    .WaitFor(fraudDb)
    .WithReference(kafka)
    .WaitFor(kafka);

// Batch job: started on demand from the Aspire dashboard.
builder.AddDotnetProject("settlement-job", "../CardPlatform.Settlement.Job")
    .WithReference(ledgerDb)
    .WaitFor(ledgerDb)
    .WithExplicitStart();

builder.Build().Run();
