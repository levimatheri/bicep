// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Bicep.Cli.Arguments;
using Bicep.Cli.Helpers;
using Bicep.Cli.Logging;
using Bicep.Cli.Services;
using Bicep.Core;
using Bicep.Core.Parsing;
using Bicep.Core.Syntax;
using Bicep.Core.Utils;
using Bicep.IO.Abstraction;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;

namespace Bicep.Cli.Commands;

public class ConsoleCommand(
    ILogger logger,
    OutputWriter writer,
    ReplEnvironment replEnvironment) : ICommand
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

                var input = Console.ReadLine()?.Trim();
                var inputLower = input?.ToLowerInvariant();

                if (input is null || inputLower is null || inputLower == "exit")
                {
                    break;
                }

                if (inputLower == "clear")
                {
                    Console.Clear();
                    continue;
                }

                if (inputLower == "help")
                {
                    writer.WriteLineToStdout("TBD.");
                    continue;
                }

                Trace.WriteLine($"You entered: {input}");

                var result = replEnvironment.EvaluateInput(input);

                if (result.HasValue)
                {
                    writer.WriteLineToStdout($"{result.Value}");
                }
                else if (result.Diagnostics.Any())
                {
                    foreach (var diagnostic in result.Diagnostics)
                    {
                        writer.WriteLineToStdout($"{diagnostic.Message}");
                    }
                }
                else
                {
                    // Nothing to show (e.g., successful variable declaration)
                    continue;
                }
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
