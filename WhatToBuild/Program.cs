using WhatToBuild.Hubs;

var builder = WebApplication.CreateBuilder(args);

// SignalR is how the backend pushes updates to the web page without the page
// having to ask for them. Registering it here makes it available below.
builder.Services.AddSignalR();

var app = builder.Build();

// Only listen on the local machine. The app has no login and no protection,
// so it must not be reachable from the network.
app.Urls.Add("http://127.0.0.1:5123");

// Serve the web page out of wwwroot. UseDefaultFiles makes "/" load index.html.
app.UseDefaultFiles();
app.UseStaticFiles();

// The address the web page connects to for live updates.
app.MapHub<GameHub>("/hub");

app.Run();
