using CardPlatform.Fraud.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHostedService<FraudDetectionWorker>();

var host = builder.Build();
host.Run();
