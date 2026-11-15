using System.Text;
using MasterHub.Cli;

// A console program of its own: the app is a windowed program without a console, so a CLI cannot live inside it.
Console.OutputEncoding = Encoding.UTF8;
return await new CliRunner().RunAsync(args, Console.Out, Console.Error);
