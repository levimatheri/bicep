// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Immutable;
using Azure.ResourceManager.Resources.Models;
using Bicep.Core.Diagnostics;
using Bicep.Core.Models;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.Core.Workspaces;
using Newtonsoft.Json;

namespace Bicep.Core.Emit;

public record ParametersResult(
    bool Success,
    ImmutableDictionary<BicepSourceFile, ImmutableArray<IDiagnostic>> Diagnostics,
    string? Parameters,
    string? TemplateSpecId,
    TemplateResult? Template);

public record TemplateResult(
    bool Success,
    ImmutableDictionary<BicepSourceFile, ImmutableArray<IDiagnostic>> Diagnostics,
    string? Template,
    string? SourceMap);

public record DeploymentResult(
    bool Success,
    ImmutableDictionary<BicepSourceFile, ImmutableArray<IDiagnostic>> Diagnostics,
    ArmDeploymentDefinition? Definition);

public interface ICompilationEmitter
{
    TemplateResult Template();

    ParametersResult Parameters();

    DeploymentResult Deployment();
}

public class CompilationEmitter : ICompilationEmitter
{
    private readonly Compilation compilation;

    public CompilationEmitter(Compilation compilation)
    {
        this.compilation = compilation;
    }

    public DeploymentResult Deployment()
    {
        var model = compilation.GetEntrypointSemanticModel();
        if (model.SourceFileKind != BicepSourceFileKind.DeployFile)
        {
            throw new InvalidOperationException($"Entry-point {model.Root.FileUri} is not a deployment file");
        }

        if (model.Root.DeployDeclaration is null)
        {
            throw new InvalidOperationException($"Entry-point {model.Root.FileUri} does not contain a deployment declaration");
        }

        var deployDeclaration = model.Root.DeployDeclaration;
        if (!deployDeclaration.TryGetReferencingSemanticModel().IsSuccess(out var bicepModel) && bicepModel is not SemanticModel)
        {
            throw new InvalidOperationException($"Failed to find linked bicep file for deployment file {model.Root.FileUri}");
        }

        var diagnostics = compilation.GetAllDiagnosticsByBicepFile();
        var scope = model.EmitLimitationInfo.DeploymentScopeData;
        var managementGroup = (scope?.ManagementGroupNameProperty as StringSyntax)?.TryGetLiteralValue() ?? null;
        var subscriptionId = (scope?.SubscriptionIdProperty as StringSyntax)?.TryGetLiteralValue() ?? null;
        var resourceGroup = (scope?.ResourceGroupProperty as StringSyntax)?.TryGetLiteralValue() ?? null;
        return new DeploymentResult(
            true,
            diagnostics,
            new ArmDeploymentDefinition(
                managementGroup,
                subscriptionId,
                resourceGroup,
                deployDeclaration.Name,
                new ArmDeploymentProperties(ArmDeploymentMode.Incremental) {
                    Template = BinaryData.FromString(Template((bicepModel as SemanticModel)!)?.Template ?? throw new InvalidOperationException("Failed to generate template")),
                    Parameters = BinaryData.FromString("{}"),
                }
            )
        );

    }

    public ParametersResult Parameters()
    {
        var model = compilation.GetEntrypointSemanticModel();
        if (model.SourceFileKind != BicepSourceFileKind.ParamsFile)
        {
            throw new InvalidOperationException($"Entry-point {model.Root.FileUri} is not a parameters file");
        }

        var diagnostics = compilation.GetAllDiagnosticsByBicepFile();

        using var writer = new StringWriter { NewLine = "\n" };
        var result = new ParametersEmitter(model).Emit(writer);
        if (result.Status != EmitStatus.Succeeded)
        {
            return new(false, diagnostics, null, null, null);
        }

        var parametersData = writer.ToString();
        if (!model.Root.TryGetBicepFileSemanticModelViaUsing().IsSuccess(out var usingModel))
        {
            throw new InvalidOperationException($"Failed to find linked bicep file for parameters file {model.Root.FileUri}");
        }

        switch (usingModel)
        {
            case SemanticModel bicepModel:
                {
                    var templateResult = Template(bicepModel);
                    return new ParametersResult(true, diagnostics, parametersData, null, templateResult);
                }
            case ArmTemplateSemanticModel armTemplateModel:
                {
                    var template = armTemplateModel.SourceFile.GetOriginalSource();
                    var templateResult = new TemplateResult(true, ImmutableDictionary<BicepSourceFile, ImmutableArray<IDiagnostic>>.Empty, template, null);

                    return new ParametersResult(true, diagnostics, parametersData, null, templateResult);
                }
            case TemplateSpecSemanticModel templateSpecModel:
                {
                    return new ParametersResult(true, diagnostics, parametersData, templateSpecModel.SourceFile.TemplateSpecId, null);
                }
            case EmptySemanticModel _:
                {
                    return new ParametersResult(true, diagnostics, parametersData, null, null);
                }
        }

        throw new InvalidOperationException($"Invalid semantic model of type {usingModel.GetType()}");
    }

    public TemplateResult Template()
    {
        var model = this.compilation.GetEntrypointSemanticModel();
        if (model.SourceFileKind != Workspaces.BicepSourceFileKind.BicepFile)
        {
            throw new InvalidOperationException($"Entry-point {model.Root.FileUri} is not a bicep file");
        }

        return Template(model);
    }

    private TemplateResult Template(SemanticModel model)
    {
        var diagnostics = compilation.GetAllDiagnosticsByBicepFile();

        using var writer = new StringWriter { NewLine = "\n" };
        var result = new TemplateEmitter(model).Emit(writer);
        if (result.Status != EmitStatus.Succeeded)
        {
            return new(false, diagnostics, null, null);
        }

        var template = writer.ToString();
        var sourceMap = result.SourceMap is { } ? JsonConvert.SerializeObject(result.SourceMap) : null;

        return new(true, diagnostics, template, sourceMap);
    }
}
