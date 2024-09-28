// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Azure.ResourceManager.Resources.Models;
using Bicep.Core.Diagnostics;
using Bicep.Core.Models;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.Core.Workspaces;

namespace Bicep.Core.Emit;

public class DeployEmitter
{
    private readonly Compilation compilation;
    private record DeploymentScope(string? ManagementGroupId, string? SubscriptionId, string? ResourceGroupName);
    public DeployEmitter(Compilation compilation)
    {
        this.compilation = compilation;
    }

    public DeployResult Emit()
    {
        DeployResult CreateFailedResult() => new(false, this.compilation.GetAllDiagnosticsByBicepFile(), null);

        var model = compilation.GetEntrypointSemanticModel();
        if (model.SourceFileKind != BicepSourceFileKind.DeployFile)
        {
            throw new InvalidOperationException($"Entry-point {model.Root.FileUri} is not a deployment file");
        }

        if (model.GetAllDiagnostics().Any(d => d.IsError()) ||
            model.Root.DeployDeclaration is not { } deployDeclaration ||
            !deployDeclaration.TryGetReferencingSemanticModel().IsSuccess(out var refModel) ||
            refModel is not SemanticModel refSemanticModel)
        {
            return CreateFailedResult();
        };

        var template = new CompilationEmitter(this.compilation).Template(refSemanticModel);

        var body = deployDeclaration.DeclaringDeploySyntax.Body as ObjectSyntax
            ?? throw new InvalidOperationException("Deploy declaration body must be an object syntax");

        var deployProperties = body.ToNamedPropertyDictionary();

        var scopeProperty = deployProperties["scope"].Value;

        if (scopeProperty is not FunctionCallSyntax scopeFunction)
        {
            return CreateFailedResult();
        }

        var scopeArgs = scopeFunction.Arguments
            .Select(arg => (arg.Expression as StringSyntax)?.TryGetLiteralValue()
                ?? throw new InvalidOperationException("Scope function arguments must be string literals"))
            .ToArray();

        DeploymentScope scope = scopeFunction.Name.IdentifierName switch
        {
            "tenant" => new DeploymentScope(ManagementGroupId: null, SubscriptionId: null, ResourceGroupName: null),
            "managementGroup" => new DeploymentScope(ManagementGroupId: scopeArgs[0], SubscriptionId: null, ResourceGroupName: null),
            "subscription" => scopeArgs.Length switch
            {
                0 => new DeploymentScope(ManagementGroupId: null, SubscriptionId: Guid.NewGuid().ToString(), ResourceGroupName: null),
                1 => new DeploymentScope(ManagementGroupId: null, SubscriptionId: scopeArgs[0], ResourceGroupName: null),
                _ => throw new InvalidOperationException("Subscription scope function must have 0 or 1 argument")
            },
            "resourceGroup" => scopeArgs.Length switch
            {
                1 => new DeploymentScope(ManagementGroupId: null, SubscriptionId: null, ResourceGroupName: scopeArgs[0]),
                2 => new DeploymentScope(ManagementGroupId: null, SubscriptionId: scopeArgs[0], ResourceGroupName: scopeArgs[1]),
                _ => throw new InvalidOperationException("Resource group scope function must have 1 or 2 arguments")
            },
            _ => throw new InvalidOperationException("Invalid scope function")
        };

        var deploymentMode = ArmDeploymentMode.Incremental;
        if (deployProperties.TryGetValue("mode", out var modeProperty))
        {
            var modeVal = (modeProperty.Value as StringSyntax)?.TryGetLiteralValue();
            if (Enum.TryParse<ArmDeploymentMode>(modeVal, ignoreCase: true, out var mode))
            {
                deploymentMode = mode;
            }
        }

        var deploymentName = Path.GetFileNameWithoutExtension(refSemanticModel.SourceFile.FileUri.LocalPath);
        //var paramProperties = deployProperties["parameters"].Value;

        if (!deployProperties.TryGetValue("params", out var paramObject) ||
            paramObject.Value is not ObjectSyntax paramObjectSyntax)
        {
            return new DeployResult(
                true,
                this.compilation.GetAllDiagnosticsByBicepFile(),
                new ArmDeploymentDefinition(
                    scope.ManagementGroupId,
                    scope.SubscriptionId,
                    scope.ResourceGroupName,
                    deploymentName,
                    new ArmDeploymentProperties(
                        mode: deploymentMode)
                    {
                        Template = BinaryData.FromObjectAsJson(template.Template)
                    }
                )
            );
        }

        var paramNode = SyntaxToJsonNode(
            paramObjectSyntax.ToNamedPropertyDictionary(), 
            (node) => new JsonObject { { "value", node } });

        return new DeployResult(
                true,
                this.compilation.GetAllDiagnosticsByBicepFile(),
                new ArmDeploymentDefinition(
                    scope.ManagementGroupId,
                    scope.SubscriptionId,
                    scope.ResourceGroupName,
                    deploymentName,
                    new ArmDeploymentProperties(
                        mode: deploymentMode)
                    {
                        Template = BinaryData.FromObjectAsJson(template.Template),
                        Parameters = BinaryData.FromObjectAsJson(paramNode)
                    }
                )
            );
    }

    private JsonNode SyntaxToJsonNode(
        ImmutableDictionary<string, ObjectPropertySyntax> properties, 
        Func<JsonNode, JsonNode>? transform = null)
    {
        var node = new JsonObject();
        foreach (var (name, valueSyntax) in properties)
        {
            var paramNode = SyntaxToJsonNode(valueSyntax.Value);

            node[name] = transform?.Invoke(paramNode) ?? paramNode;
        }

        return node;
    }

    private JsonNode SyntaxToJsonNode(SyntaxBase syntax)
    {
        return syntax switch
        {
            StringSyntax @string => @string.TryGetLiteralValue() ?? throw new InvalidOperationException("String syntax must have a literal value"),
            IntegerLiteralSyntax integer => integer.Value,
            BooleanLiteralSyntax @bool => @bool.Value,
            ArraySyntax array => new JsonArray([..array.Items.Select(SyntaxToJsonNode)]),
            ArrayItemSyntax arrayItem => SyntaxToJsonNode(arrayItem.Value),
            ObjectSyntax @object => SyntaxToJsonNode(@object.ToNamedPropertyDictionary()),
            _ => throw new NotImplementedException($"Unsupported syntax node type {syntax.GetType()}")
        };
    }
}
