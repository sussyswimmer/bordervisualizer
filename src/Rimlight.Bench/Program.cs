using Rimlight.Bench;

// dotnet run -c Release --project src/Rimlight.Bench -- [analyzer|frame|soak] [options]   (--help for all options)
return BenchCli.Run(args, Console.Out, Console.Error);
