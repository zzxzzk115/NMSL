using OmniLyrics.Core.Lyrics.Models;

public interface ILyricsProvider
{
    List<LyricsLine>? CurrentLyrics { get; }
}