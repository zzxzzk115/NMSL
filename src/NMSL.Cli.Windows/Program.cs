using NMSL.Backends.Windows;
using NMSL.Core.Cli;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var backend = new SMTCBackend(); // use SMTC for Windows

        var cli = new DefaultLyricsCli(backend);

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

        await cli.RunAsync(cts.Token);
    }
}