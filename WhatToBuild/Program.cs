using WhatToBuild.Clients;
using WhatToBuild.Data;
using WhatToBuild.Game;
using WhatToBuild.Hubs;
using WhatToBuild.Planning;
using WhatToBuild.Recommendations;
using WhatToBuild.Services;
using WhatToBuild.SupportedChampions;

var builder = WebApplication.CreateBuilder(args);

var dataRoot = Path.Combine(AppContext.BaseDirectory, "GameData");
builder.Services.AddSingleton(ChampionRepository.Load(dataRoot));
builder.Services.AddSingleton(ItemRepository.Load(dataRoot));
builder.Services.AddSingleton(NeutralRepository.Load(dataRoot));
builder.Services.AddSingleton(ChampionKits.Load(dataRoot));
builder.Services.AddSingleton(ModelData.Load(dataRoot));
builder.Services.AddSingleton<PlanPreferences>();
builder.Services.AddSingleton<IRecommendationSource, BuildRecommendations>();

string? replayFolder = null;
#if DEBUG
replayFolder = builder.Configuration["GameSource:ReplayFolder"];
#endif

if (string.IsNullOrWhiteSpace(replayFolder))
{
    builder.Services.AddSingleton<ILiveClient, LiveClient>();
}
else
{
    var folder = Path.Combine(AppContext.BaseDirectory, replayFolder);
    builder.Services.AddSingleton<ILiveClient>(ReplayClient.FromFolder(folder, loop: true));
}

builder.Services.AddSingleton<GameTracker>();
builder.Services.AddSingleton<MatchupCalculator>();
builder.Services.AddSingleton<GameStateService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GameStateService>());

builder.Services.AddSignalR();

var app = builder.Build();

app.Urls.Add("http://127.0.0.1:5123");

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});

app.MapGet("/api/state", (GameStateService service) => service.Latest);
app.MapGet("/api/recommendation", (GameStateService service) => service.LatestRecommendation);

app.MapGet("/api/preferences", (PlanPreferences preferences) =>
    new { coreItems = preferences.CoreItems, label = preferences.Label });

app.MapPost("/api/preferences", (PlanPreferences preferences, PreferenceRequest request) =>
{
    if (request.CoreItems is { } coreItems)
    {
        preferences.CoreItems = coreItems;
    }

    if (request.Weights is { Key.Length: > 0 } weights)
    {
        preferences.SetWeights(weights.Key, weights.Reset ? null : new ModelSettings.ObjectiveWeights
        {
            Damage = weights.Damage,
            Burst = weights.Burst,
            Uptime = weights.Uptime,
            Survival = weights.Survival,
        });
    }

    return Results.Ok(new { coreItems = preferences.CoreItems, label = preferences.Label });
});

app.MapHub<GameHub>("/hub");

app.Run();

record PreferenceRequest(int? CoreItems, WeightsRequest? Weights);

/// <summary>Objective weights for one entry of model.json's objectives; <c>Reset</c> goes back to the model's own.</summary>
record WeightsRequest(string Key, double Damage, double Burst, double Uptime, double Survival, bool Reset = false);
