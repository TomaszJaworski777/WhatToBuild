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
    private readonly bool _loop;
    private int _next;

    public ReplayClient(IEnumerable<string> snapshots, bool loop = false)
    {
        _snapshots = snapshots.ToList();
        _loop = loop;
    }

    public static ReplayClient FromFolder(string folder, bool loop = false) =>
        new(Directory.EnumerateFiles(folder, "*.json").Order().Select(File.ReadAllText), loop);

    public bool IsFinished => !_loop && _next >= _snapshots.Count;

    public Task<string?> GetAllGameDataAsync(CancellationToken ct = default)
    {
        if (_snapshots.Count == 0 || IsFinished)
        {
            return Task.FromResult<string?>(null);
        }

        if (_next >= _snapshots.Count)
        {
            _next = 0;
        }

        return Task.FromResult<string?>(_snapshots[_next++]);
    }
}
