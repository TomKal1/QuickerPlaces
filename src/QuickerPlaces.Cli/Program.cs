using System;

namespace QuickerPlaces.Cli;

public static class Program
{
    public static int Main(string[] args) => CliApp.Run(args, Console.Out, CliEnvironment.ForThisMachine());
}
