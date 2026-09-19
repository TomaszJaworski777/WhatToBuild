namespace WhatToBuild.Clients;

public interface ILiveClient
{
    Task<string?> GetAllGameDataAsync(CancellationToken ct = default);
}

public sealed class LiveClient : ILiveClient, IDisposable
{
    public const int Port = 2999;

    private readonly HttpClient _http;

    public LiveClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, _, _, _) =>
                request.RequestUri is { IsLoopback: true, Port: Port },
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://127.0.0.1:{Port}/liveclientdata/"),
            Timeout = TimeSpan.FromSeconds(2),
        };
    }

    public async Task<string?> GetAllGameDataAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync("allgamedata", ct);

            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}

public sealed class ReplayClient : ILiveClient
{
    private readonly IReadOnlyList<string> _snapshots;
    private int _next;

    public ReplayClient(IEnumerable<string> snapshots)
    {
        _snapshots = snapshots.ToList();
    }

    public static ReplayClient FromFolder(string folder) =>
        new(Directory.EnumerateFiles(folder, "*.json").Order().Select(File.ReadAllText));

    public bool IsFinished => _next >= _snapshots.Count;

    public Task<string?> GetAllGameDataAsync(CancellationToken ct = default) =>
        Task.FromResult(IsFinished ? null : _snapshots[_next++]);
}
