using System.CommandLine;
using CodeWalker.Cli;

RootCommand rootCommand = new(description: "CodeWalker CLI - RPF Archive Tool")
{
    ExtractHandler.CreateCommand(),
    ListHandler.CreateCommand(),
    HashHandler.CreateCommand(),
    TreeHandler.CreateCommand(),
    Gen9Handler.CreateCommand(),
    PackHandler.CreateCommand(),
    DiffHandler.CreateCommand(),
};

return rootCommand.Parse(args).Invoke();
