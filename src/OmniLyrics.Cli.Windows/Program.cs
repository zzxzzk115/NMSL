using OmniLyrics.Backends.Windows;
using OmniLyrics.Core.Cli;

await LyricsCliRunner.RunAsync(new SMTCBackend(), args);