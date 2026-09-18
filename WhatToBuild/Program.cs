using WhatToBuild.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

var app = builder.Build();

app.Urls.Add("http://127.0.0.1:5123");

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<GameHub>("/hub");

app.Run();
