using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhatToBuild.Data;

public static class GameDataJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
