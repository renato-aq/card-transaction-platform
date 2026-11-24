using CardPlatform.Settlement.Job;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHostedService<SettlementJob>();

var host = builder.Build();
host.Run();
