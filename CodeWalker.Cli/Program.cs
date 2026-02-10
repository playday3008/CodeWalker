using System;
using System.CommandLine;
using System.Threading;

using CodeWalker.Cli;

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

RootCommand rootCommand = new(description: "CodeWalker CLI - RPF Archive Tool")
{
    ExtractHandler.CreateCommand(cts.Token),
    ListHandler.CreateCommand(cts.Token),
    HashHandler.CreateCommand(cts.Token),
    TreeHandler.CreateCommand(cts.Token),
    Gen9Handler.CreateCommand(cts.Token),
    PackHandler.CreateCommand(cts.Token),
    DiffHandler.CreateCommand(cts.Token),
    ExportHandler.CreateCommand(cts.Token),
    StatHandler.CreateCommand(cts.Token),
    SearchHandler.CreateCommand(cts.Token),
    ValidateHandler.CreateCommand(cts.Token),
    InspectHandler.CreateCommand(cts.Token),
};

try
{
    return rootCommand.Parse(args).Invoke();
}
catch (OperationCanceledException)
{
    return 130;
}
