// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Cli.Arguments;
using Bicep.Cli.Helpers;
using Bicep.Cli.Logging;
using Bicep.Cli.Services;
using Bicep.Core;
using Bicep.IO.Abstraction;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;

namespace Bicep.Cli.Commands;

public class ConsoleCommand(
    ILogger logger,
    OutputWriter writer) : ICommand
{
    public async Task<int> RunAsync(ConsoleArguments args, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        PrintIntro();

        while (!cancellationToken.IsCancellationRequested)
        {
            writer.WriteToStdout("> ");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var input = Console.ReadLine()?.Trim()?.ToLowerInvariant();

                if (input is null || input == "exit")
                {
                    break;
                }

                if (input == "clear")
                {
                    Console.Clear();
                    continue;
                }

                if (input == "help")
                {
                    writer.WriteLineToStdout("TBD.");
                    continue;
                }

                writer.WriteLineToStdout($"You entered: {input}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogError("Operation was cancelled.");
                return 1;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while processing your input.");
                writer.WriteLineToStdout($"Error: {ex.Message}");
                return 1;
            }
        }

        return 0;
    }

    private void PrintIntro()
    {
        writer.WriteLineToStdout("Welcome to the Bicep REPL:");
        writer.WriteLineToStdout("Type Bicep expressions and statements to evaluate them.");
        writer.WriteLineToStdout("Type 'help' to learn more, 'clear' to clear the console, and 'exit' to quit.");
        writer.WriteLineToStdout(string.Empty);
    }
}
