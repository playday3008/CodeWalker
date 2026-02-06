using System.CommandLine;
using CodeWalker.Cli;

RootCommand rootCommand = new(description: "CodeWalker CLI - RPF Archive Tool")
{
    ExtractHandler.CreateCommand(),
    ListHandler.CreateCommand(),
    HashHandler.CreateCommand(),
};

return rootCommand.Parse(args).Invoke();
