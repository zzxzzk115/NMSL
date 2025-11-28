using OmniLyrics.Backends.Linux;
using OmniLyrics.Core.Cli;

await LyricsCliRunner.RunAsync(new MPRISBackend(), args);