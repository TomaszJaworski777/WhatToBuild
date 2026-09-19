using WhatToBuild.Clients;
using WhatToBuild.Data;
using WhatToBuild.Game;
using WhatToBuild.Hubs;
using WhatToBuild.Recommendations;
using WhatToBuild.Services;

var builder = WebApplication.CreateBuilder(args);

var dataRoot = Path.Combine(AppContext.BaseDirectory, "GameData");
builder.Services.AddSingleton(ChampionRepository.Load(dataRoot));
builder.Services.AddSingleton(ItemRepository.Load(dataRoot));
builder.Services.AddSingleton(NeutralRepository.Load(dataRoot));

string? replayFolder = null;
#if DEBUG
replayFolder = builder.Configuration["GameSource:ReplayFolder"];
#endif

if (string.IsNullOrWhiteSpace(replayFolder))
{
    builder.Services.AddSingleton<ILiveClient, LiveClient>();
    builder.Services.AddSingleton<IRecommendationSource, NoRecommendations>();
}
else
{
    var folder = Path.Combine(AppContext.BaseDirectory, replayFolder);
    builder.Services.AddSingleton<ILiveClient>(ReplayClient.FromFolder(folder, loop: true));
    builder.Services.AddSingleton<IRecommendationSource, SampleRecommendations>();
}

builder.Services.AddSingleton<GameTracker>();
builder.Services.AddSingleton<GameStateService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GameStateService>());

builder.Services.AddSignalR();

var app = builder.Build();

app.Urls.Add("http://127.0.0.1:5123");

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/state", (GameStateService service) => service.Latest);
app.MapGet("/api/recommendation", (GameStateService service) => service.LatestRecommendation);

app.MapHub<GameHub>("/hub");

app.Run();
