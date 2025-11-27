using NMSL.Core.Lyrics.Models;

namespace NMSL.Core.Cli;

/// <summary>
/// Base class for building a CLI lyrics display, independent of backend type.
/// Any backend implementing IPlayerBackend can be used (SMTC, MPRIS, etc).
/// </summary>
public abstract class BaseLyricsCli
{
    // Global lock to prevent concurrent console writes
    protected static readonly SemaphoreSlim ConsoleLock = new(1, 1);

    // Lock to prevent concurrent song change processing
    protected static readonly SemaphoreSlim SongChangeLock = new(1, 1);

    protected readonly IPlayerBackend Backend;
    protected readonly LyricsService LyricsService;

    // Playback tracking
    protected List<LyricsLine>? CurrentLyrics = null;
    protected string LastSongId = "";
    protected int LastCenterIndex = -999;

    // Layout
    protected const int HEADER_LINES = 3;
    protected const int LYRICS_BUFFER = 6;
    protected int LyricsStartLine => HEADER_LINES;

    protected BaseLyricsCli(IPlayerBackend backend)
    {
        Backend = backend;
        LyricsService = new LyricsService();

        // Subscribe to backend events
        Backend.OnStateChanged += async (_, state) =>
        {
            await HandleBackendStateChangedAsync(state);
        };
    }

    /// <summary>
    /// Run the CLI display loop.
    /// </summary>
    public async Task RunAsync(CancellationToken token)
    {
        // Start backend (SMTC, MPRIS, etc)
        await Backend.StartAsync(token);

        // Main rendering loop
        while (!token.IsCancellationRequested)
        {
            RenderLyricsFrame();
            await Task.Delay(10, token);
        }
    }

    /// <summary>
    /// Called whenever the backend reports a state change.
    /// Handles song switching, header updates, and lyrics loading.
    /// </summary>
    private async Task HandleBackendStateChangedAsync(PlayerState? state)
    {
        if (state is null || !state.Playing)
            return;

        // Normalize title/artist to detect unique tracks
        string normTitle = (state.Title ?? "").Trim().ToLowerInvariant();
        string normArtist = (state.Artists.FirstOrDefault() ?? "").Trim().ToLowerInvariant();
        string songId = $"{normTitle}|{normArtist}";

        bool shouldLoadLyrics = false;
        string artistsString = "";

        // -----------------------------
        // LOCK: only detect new song, do not run I/O
        // -----------------------------
        await SongChangeLock.WaitAsync();
        try
        {
            if (songId == LastSongId)
                return;

            // Mark as new song
            LastSongId = songId;
            LastCenterIndex = -999;
            CurrentLyrics = null;

            artistsString = state.Artists.Count > 0
                ? string.Join(", ", state.Artists)
                : "Unknown Artist";

            shouldLoadLyrics = true;
        }
        finally
        {
            SongChangeLock.Release();
        }

        if (!shouldLoadLyrics)
            return;

        // Show immediate header ("Searching lyrics…")
        await WriteHeader(
            "Now Playing:",
            $"{artistsString} - {state.Title}",
            "Searching lyrics..."
        );

        // Load lyrics via provider
        var parsed = await LyricsService.SearchLyricsAsync(state);

        if (parsed != null)
        {
            CurrentLyrics = parsed;

            await WriteHeader(
                "Now Playing:",
                $"{artistsString} - {state.Title}",
                ""
            );
        }
        else
        {
            await WriteHeader(
                "Now Playing:",
                $"{artistsString} - {state.Title}",
                "(No lyrics found)"
            );
        }
    }

    /// <summary>
    /// Process current playback state and draw lyrics window.
    /// Called continuously by RunAsync().
    /// </summary>
    private void RenderLyricsFrame()
    {
        var state = Backend.GetCurrentState();
        if (state is null || CurrentLyrics is null)
            return;

        TimeSpan pos = state.Position;

        // Find current lyrics line
        int centerIdx = CurrentLyrics.FindLastIndex(l => l.Timestamp <= pos);
        if (centerIdx < 0 || centerIdx == LastCenterIndex)
            return;

        LastCenterIndex = centerIdx;

        // Determine visible window
        int start = Math.Max(0, centerIdx - 3);
        int end = Math.Min(CurrentLyrics.Count - 1, start + LYRICS_BUFFER - 1);
        start = Math.Max(0, end - (LYRICS_BUFFER - 1));

        var lines = new List<string>();
        for (int i = start; i <= end; i++)
        {
            string text = CurrentLyrics[i].Text;
            lines.Add(i == centerIdx ? $">> {text}" : $"   {text}");
        }

        _ = WriteLyricsBlock(lines, LyricsStartLine);
    }

    // -----------------------------
    // Console writers (thread-safe)
    // -----------------------------

    protected static async Task WriteHeader(string line1, string line2, string line3)
    {
        await ConsoleLock.WaitAsync();
        try
        {
            Console.SetCursorPosition(0, 0);
            string[] lines = { line1, line2, line3 };

            foreach (var line in lines)
            {
                Console.Write(new string(' ', Console.WindowWidth));
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.WriteLine(line);
            }
        }
        finally
        {
            ConsoleLock.Release();
        }
    }

    protected static async Task WriteLyricsBlock(List<string> lines, int startLine)
    {
        await ConsoleLock.WaitAsync();
        try
        {
            Console.SetCursorPosition(0, startLine);

            foreach (var line in lines)
            {
                Console.Write(new string(' ', Console.WindowWidth));
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.WriteLine(line);
            }

            Console.SetCursorPosition(0, startLine + lines.Count);
        }
        finally
        {
            ConsoleLock.Release();
        }
    }
}