using Microsoft.AspNetCore.SignalR;

namespace WhatToBuild.Hubs;

/// <summary>
/// The connection the web page opens to receive live updates.
/// It is empty because traffic only goes one way: we send messages to the page,
/// and the page never calls anything here.
/// </summary>
public class GameHub : Hub
{
}
