using System;

namespace Rts.Cli;

/// <summary>Entry point of the headless sim CLI; all the work is in <see cref="CliRunner"/> so tests can call it in-process.</summary>
internal static class Program
{
    private static int Main(string[] args) => CliRunner.Run(args, Console.Out, Console.Error);
}
