using NMSL.Backends.Linux;
using NMSL.Core.Cli;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var backend = new MPRISBackend(); // use MPRIS for Linux

        var cli = new DefaultLyricsCli(backend);

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

        await cli.RunAsync(cts.Token);
    }
}