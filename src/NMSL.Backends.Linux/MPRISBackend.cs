using NMSL.Core;
using Tmds.DBus;

namespace NMSL.Backends.Linux;

/// <summary>
/// MPIRS (D-Bus) Backend, only works on Linux
/// </summary>
public class MPRISBackend : IPlayerBackend
{
    private PlayerState? _lastState;
    public event EventHandler<PlayerState>? OnStateChanged;

    private Player? _player;
    private string? _busName;
    private IDisposable? _propertyWatcher;

    // Timer for incremental playback position simulation.
    // Many MPRIS implementations do not emit position updates.
    private System.Timers.Timer? _positionTimer;
    private DateTime _lastRealTime;

    public PlayerState? GetCurrentState() => _lastState;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var bus = Connection.Session;

        // Watch DBus name changes to detect player appear/disappear
        var dbus = bus.CreateProxy<IDBus>("org.freedesktop.DBus", "/org/freedesktop/DBus");

        await dbus.WatchNameOwnerChangedAsync(args =>
        {
            var name = args.name;
            var oldOwner = args.oldOwner;
            var newOwner = args.newOwner;

            if (!name.StartsWith("org.mpris.MediaPlayer2."))
                return;

            if (!string.IsNullOrEmpty(newOwner) && string.IsNullOrEmpty(oldOwner))
            {
                Console.WriteLine($"Player appeared: {name}");
                _ = ConnectToPlayerAsync(name, cancellationToken);
            }

            if (!string.IsNullOrEmpty(oldOwner) && string.IsNullOrEmpty(newOwner))
            {
                Console.WriteLine($"Player disappeared: {name}");
                if (_busName == name)
                    DisconnectPlayer();
            }
        });

        // Try connect immediately if already running
        var services = await bus.ListServicesAsync();
        var existing = services.FirstOrDefault(n => n.StartsWith("org.mpris.MediaPlayer2."));
        if (existing != null)
        {
            await ConnectToPlayerAsync(existing, cancellationToken);
        }
    }

    private async Task ConnectToPlayerAsync(string busName, CancellationToken cancellationToken)
    {
        var bus = Connection.Session;

        // Cleanup previous connection
        DisconnectPlayer();

        _busName = busName;

        // Create proxy
        var playerProxy = bus.CreateProxy<IPlayer>(_busName, "/org/mpris/MediaPlayer2");

        // Player wrapper instance
        _player = new Player(_busName, playerProxy);

        // Subscribe to property changes from the Player interface
        _propertyWatcher = await playerProxy.WatchPropertiesAsync(HandlePropertyChanged);

        Console.WriteLine($"[MPRIS] Connected to {_busName}");

        // Initialize simulated position tracking
        StartPositionTimer();

        // Initial metadata/status update
        await UpdateStateAsync(null);
    }

    private void DisconnectPlayer()
    {
        _propertyWatcher?.Dispose();
        _propertyWatcher = null;

        _positionTimer?.Stop();
        _positionTimer?.Dispose();
        _positionTimer = null;

        _player = null;
        _busName = null;

        if (_lastState != null)
        {
            _lastState = null;
            OnStateChanged?.Invoke(this, null!);
        }
    }

    private async void HandlePropertyChanged(PropertyChanges changes)
    {
        // The proxy is directly bound to the Player interface, so no interface filtering is required.
        await UpdateStateAsync(changes);
    }

    private async Task UpdateStateAsync(PropertyChanges? changes)
    {
        if (_player == null)
            return;

        var meta = await _player.GetMetadataAsync();
        if (meta == null)
            return;

        // Position retrieved once as baseline, many players do not update it dynamically
        var pos = await _player.GetPositionAsync();
        var status = await _player.GetPlaybackStatusAsync();

        var newState = new PlayerState
        {
            Title = meta.Title,
            Album = meta.Album,
            Position = TimeSpan.FromMicroseconds(pos),
            Playing = status == "Playing",
            SourceApp = _busName ?? "Unknown"
        };

        if (meta.Artists != null)
            newState.Artists.AddRange(meta.Artists);

        if (meta.ArtUrl != null)
            newState.ArtworkUrl = meta.ArtUrl.ToString();

        if (meta.Length.HasValue)
            newState.Duration = meta.Length.Value;

        // Reset reference timestamp for simulated position advancement
        _lastRealTime = DateTime.UtcNow;

        if (!StatesEqual(_lastState, newState))
        {
            _lastState = newState;
            OnStateChanged?.Invoke(this, newState);
        }
    }

    private static bool StatesEqual(PlayerState? a, PlayerState b)
    {
        if (a is null) return false;

        if (a.Artists.Count != b.Artists.Count)
            return false;

        for (int i = 0; i < a.Artists.Count; i++)
            if (a.Artists[i] != b.Artists[i])
                return false;

        return a.Title == b.Title &&
               a.Album == b.Album &&
               a.Duration == b.Duration &&
               a.Playing == b.Playing &&
               a.SourceApp == b.SourceApp;
        // Position is excluded because it is updated continuously by simulation.
    }

    // ---------------------------------------------------------
    // Simulated position incrementation
    // Required because most MPRIS implementations do not emit position updates.
    // ---------------------------------------------------------
    private void StartPositionTimer()
    {
        _positionTimer = new System.Timers.Timer(200); // 200ms interval
        _positionTimer.AutoReset = true;

        _positionTimer.Elapsed += (_, _) =>
        {
            var state = _lastState;
            if (state is null || !state.Playing)
                return;

            var now = DateTime.UtcNow;
            var delta = now - _lastRealTime;
            _lastRealTime = now;

            state.Position += delta;

            if (state.Duration > TimeSpan.Zero &&
                state.Position > state.Duration)
            {
                state.Position = state.Duration;
            }

            OnStateChanged?.Invoke(this, state);
        };

        _positionTimer.Start();
    }

    // ---------------------------
    // Controller commands
    // ---------------------------
    public Task PlayAsync() => _player?.PlayAsync() ?? Task.CompletedTask;

    public Task PauseAsync() => _player?.PauseAsync() ?? Task.CompletedTask;

    public Task TogglePlayPauseAsync() => _player?.PlayPauseAsync() ?? Task.CompletedTask;

    public Task NextAsync() => _player?.NextAsync() ?? Task.CompletedTask;

    public Task PreviousAsync() => _player?.PreviousAsync() ?? Task.CompletedTask;

    public Task SeekAsync(TimeSpan position)
        => _player?.SetPositionAsync("/", (long)position.TotalMicroseconds) ?? Task.CompletedTask;
}