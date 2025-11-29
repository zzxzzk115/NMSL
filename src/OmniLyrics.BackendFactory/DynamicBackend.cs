using OmniLyrics.Core;
using OmniLyrics.Backends.CiderV3;
using OmniLyrics.Backends.Linux;
using OmniLyrics.Backends.Mac;

namespace OmniLyrics.Backends.Dynamic;

public class DynamicBackend : IPlayerBackend, IDisposable
{
    private IPlayerBackend? _current;
    private CancellationTokenSource _cts = new();
    private Task? _monitorLoop;

    public event EventHandler<PlayerState>? OnStateChanged;

    public PlayerState? GetCurrentState() => _current?.GetCurrentState();

    public async Task StartAsync(CancellationToken token)
    {
        _monitorLoop = Task.Run(() => MonitorLoopAsync(token), token);
        await SwitchBackendIfNeededAsync();
    }

    private async Task MonitorLoopAsync(CancellationToken globalToken)
    {
        while (!globalToken.IsCancellationRequested)
        {
            await SwitchBackendIfNeededAsync();
            await Task.Delay(1000, globalToken);
        }
    }

    private async Task SwitchBackendIfNeededAsync()
    {
        var (backendName, backend) = await DetectBackendAsync();

        if (_current != null && _current.GetType() == backend.GetType())
            return;

        // Switch
        Console.WriteLine($"[DynamicBackend] Switching backend → {backendName}");

        _cts.Cancel();
        _cts = new();

        _current = backend;
        _current.OnStateChanged += (_, state) => OnStateChanged?.Invoke(this, state);
        await _current.StartAsync(_cts.Token);
    }

    private async Task<(string, IPlayerBackend)> DetectBackendAsync()
    {
        // API-priority
        if (await CiderV3Api.IsAvailableAsync())
            return ("CiderV3", new CiderV3Backend());

        // OS fallback
        if (OperatingSystem.IsLinux())
            return ("MPRIS", new MPRISBackend());

        if (OperatingSystem.IsMacOS())
            return ("MediaControl", new MacOSMediaControlBackend());

        throw new PlatformNotSupportedException();
    }

    // Commands routed to current backend
    public Task PlayAsync() => _current?.PlayAsync() ?? Task.CompletedTask;
    public Task PauseAsync() => _current?.PauseAsync() ?? Task.CompletedTask;
    public Task TogglePlayPauseAsync() => _current?.TogglePlayPauseAsync() ?? Task.CompletedTask;
    public Task NextAsync() => _current?.NextAsync() ?? Task.CompletedTask;
    public Task PreviousAsync() => _current?.PreviousAsync() ?? Task.CompletedTask;
    public Task SeekAsync(TimeSpan pos) => _current?.SeekAsync(pos) ?? Task.CompletedTask;

    public void Dispose()
    {
        _cts.Cancel();
    }
}