// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using Bicep.Cli.Arguments;
using Bicep.Cli.Constants;
using Bicep.Cli.Helpers.Deploy;
using Bicep.Cli.Logging;
using Bicep.Core;
using Bicep.Core.Emit;
using Bicep.Core.Semantics;
using Bicep.Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Option = Bicep.Cli.Constants.Option;

namespace Bicep.Cli.Commands;

public class DeployCommand(
    DeploymentRenderer deploymentRenderer,
    IDeploymentProcessor deploymentProcessor,
    AcrUploadDeployClient acrUploadClient,
    ILogger logger,
    IEnvironment environment,
    DiagnosticLogger diagnosticLogger,
    BicepCompiler compiler,
    InputOutputArgumentsResolver inputOutputArgumentsResolver) : DeploymentsCommandsBase<DeployArguments>(logger, diagnosticLogger, compiler, inputOutputArgumentsResolver)
{
    protected override async Task<int> RunInternal(DeployArguments args, SemanticModel model, ParametersResult result, CancellationToken cancellationToken)
    {
        var config = await DeploymentProcessor.GetDeployCommandsConfig(environment, args.AdditionalArguments, result, model.TargetScope);

        if (!string.IsNullOrWhiteSpace(args.Server))
        {
            return await DeployViaAcrUploadAsync(args.Server, config, cancellationToken);
        }

        var success = await deploymentRenderer.RenderDeployment(
            DeploymentRenderer.RefreshInterval,
            (onUpdate) => deploymentProcessor.Deploy(model.Configuration, config, onUpdate, cancellationToken),
            args.OutputFormat ?? DeploymentOutputFormat.Default,
            cancellationToken);

        return success ? 0 : 1;
    }

    private async Task<int> DeployViaAcrUploadAsync(string server, DeployCommandsConfig config, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(server.EndsWith('/') ? server : server + "/", UriKind.Absolute, out var serverUri))
        {
            throw new CommandLineException($"Invalid --server value '{server}'. Expected an absolute URL.");
        }

        var deploymentName = config.UsingConfig.Name ?? "main";
        var repository = SanitizeRepository(deploymentName);
        var tag = $"v{DateTime.UtcNow:yyyyMMddHHmmss}";

        try
        {
            var result = await acrUploadClient.DeployAsync(
                serverUri,
                repository,
                tag,
                BinaryData.FromString(config.Template),
                deploymentName,
                cancellationToken);

            return result.Status == "Succeeded" ? 0 : 1;
        }
        catch (HttpRequestException exception)
        {
            throw new CommandLineException($"Could not reach the AcrUpload server at '{serverUri}'. Ensure the server is running and the --server URL (including port) is correct. {exception.Message}");
        }
    }

    private static string SanitizeRepository(string name)
    {
        var sanitized = new string([.. name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-')]);
        return sanitized.Trim('-', '.', '_') is { Length: > 0 } trimmed ? trimmed : "main";
    }

    internal static System.CommandLine.Command CreateCommand(CommandLineBuilderContext context)
    {
        var command = new System.CommandLine.Command(Constants.Command.Deploy, "[Experimental] Deploys infrastructure using a .bicepparam file.")
        {
            TreatUnmatchedTokensAsErrors = false,
        };

        var inputFileArgument = new System.CommandLine.Argument<string>(Constants.Argument.ParametersFile)
        {
            Description = "The path to the .bicepparam file.",
        };
        var noRestoreOption = new System.CommandLine.Option<bool>(Option.NoRestore)
        {
            Description = "Do not restore modules prior to deploying.",
        };
        var formatOption = new System.CommandLine.Option<DeploymentOutputFormat?>(Option.Format)
        {
            Description = "Output format for deployment results (Default, Json).",
        };
        var serverOption = new System.CommandLine.Option<string?>(Option.Server)
        {
            Description = "[Experimental] Deploy via an AcrUpload server at the given URL: pushes the compiled template to ACR and calls the server's deploy endpoint instead of deploying directly to Azure.",
        };

        command.Add(inputFileArgument);
        command.Add(noRestoreOption);
        command.Add(formatOption);
        command.Add(serverOption);
        command.Validators.Add((System.CommandLine.Parsing.CommandResult result) => CommandLineBuilderContext.ValidateRequiredPositionalArgument(result, inputFileArgument));

        command.SetAction((result, ct) => context.RunCommandAsync(async () =>
        {
            var additionalArguments = CommandLineBuilderContext.ParseAdditionalArguments(result.UnmatchedTokens);
            var args = new DeployArguments(
                result.GetRequiredValue(inputFileArgument),
                result.GetValue(noRestoreOption),
                additionalArguments,
                result.GetValue(formatOption),
                result.GetValue(serverOption));

            return await context.GetCommand<DeployCommand>().RunAsync(args, ct);
        }));

        return command;
    }
}
