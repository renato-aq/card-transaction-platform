var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var ledgerDb = postgres.AddDatabase("ledgerdb");

var kafka = builder.AddKafka("kafka")
    .WithKafkaUI();

var ledgerApi = builder.AddDotnetProject("ledger-api", "../CardPlatform.Ledger.Api")
    .WithReference(ledgerDb)
    .WaitFor(ledgerDb)
    .WithReference(kafka)
    .WaitFor(kafka);

builder.AddDotnetProject("authorization-api", "../CardPlatform.Authorization.Api")
    .WithReference(ledgerApi)
    .WaitFor(ledgerApi)
    .WithReference(kafka)
    .WaitFor(kafka);

builder.AddDotnetProject("fraud-worker", "../CardPlatform.Fraud.Worker")
    .WithReference(kafka)
    .WaitFor(kafka);

// Batch job: started on demand from the Aspire dashboard.
builder.AddDotnetProject("settlement-job", "../CardPlatform.Settlement.Job")
    .WithReference(ledgerDb)
    .WaitFor(ledgerDb)
    .WithExplicitStart();

builder.Build().Run();
