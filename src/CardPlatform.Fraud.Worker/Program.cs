using CardPlatform.Fraud.Worker;

using IHost host = FraudWorkerHost.CreateBuilder(args).Build();

await FraudWorkerHost.InitializeAsync(host);
await host.RunAsync();
