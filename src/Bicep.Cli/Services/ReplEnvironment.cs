// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Azure.Deployments.Core.ErrorResponses;
using Azure.Deployments.Expression.Expressions;
using Azure.Deployments.Templates.Expressions;
using Bicep.Core;
using Bicep.Core.Diagnostics;
using Bicep.Core.Emit;
using Bicep.Core.Extensions;
using Bicep.Core.Semantics;
using Bicep.Core.SourceGraph;
using Bicep.Core.Syntax;
using Bicep.IO.Abstraction;
using Bicep.IO.InMemory;
using Newtonsoft.Json.Linq;

namespace Bicep.Cli.Services;

public class ReplEnvironment
{
    private readonly BicepCompiler compiler;
    private readonly Workspace workspace;
    private readonly InMemoryFileExplorer fileExplorer;
    private readonly IOUri replFileUri;
    private readonly StringBuilder replContent;
    // private Compilation currentCompilation;

    public ReplEnvironment(BicepCompiler compiler)
    {
        this.compiler = compiler;
        this.workspace = new Workspace();
        this.fileExplorer = new InMemoryFileExplorer();
        this.replFileUri = IOUri.FromFilePath("/repl.bicepparam");
        this.replContent = new StringBuilder("using none\n");
    }

    public ReplEvalResult EvaluateExpression(SyntaxBase expression)
    {
        var tempVarName = $"__temp_eval_{Guid.NewGuid():N}";
        var tempContent = new StringBuilder(replContent.ToString());

        // Handle variable declarations vs expressions differently
        string targetVarName;
        if (expression is VariableDeclarationSyntax varDecl)
        {
            // Add variable declaration directly
            tempContent.AppendLine(expression.ToString());
            targetVarName = varDecl.Name.IdentifierName;
        }
        else
        {
            // Wrap expression in temporary variable
            tempContent.AppendLine($"var {tempVarName} = {expression}");
            targetVarName = tempVarName;
        }

        // Temporarily update the file content for evaluation
        var fileHandle = this.fileExplorer.GetFile(replFileUri);
        fileHandle.Write(tempContent.ToString());

        try
        {
            // Update workspace and create new compilation
            var sourceFile = compiler.SourceFileFactory.CreateBicepParamFile(replFileUri, tempContent.ToString());
            workspace.UpsertSourceFile(sourceFile);
            var compilation = compiler.CreateCompilationWithoutRestore(replFileUri, workspace);
            var model = compilation.GetEntrypointSemanticModel();

            if (model
                .GetAllDiagnostics()
                .Any(d => d.Source != DiagnosticSource.CoreLinter && d.Level == DiagnosticLevel.Error))
            {
                // Return the first diagnostic encountered
                return ReplEvalResult.For(model.GetAllDiagnostics());
            }

            if (expression is VariableDeclarationSyntax)
            {
                this.replContent.AppendLine(expression.ToString());
                // Don't restore original content - we want to keep the new variable
                return ReplEvalResult.Empty;
            }

            // Find and evaluate the target variable
            var variable = model.Root.VariableDeclarations.FirstOrDefault(v => v.Name == targetVarName);
            if (variable?.DeclaringVariable.Value is not SyntaxBase valueExpression)
            {
                return ReplEvalResult.For(DiagnosticBuilder.ForPosition(expression)
                    .FailedToEvaluateSubject("expression", expression.ToString(), "Unable to find variable"));
            }

            var evaluator = new ReplEvaluator(model);
            var result = evaluator.EvaluateExpression(valueExpression);

            return result;
        }
        finally
        {
            // Always sync file with current replContent state
            fileHandle.Write(replContent.ToString());
        }
    }

    private class ReplEvaluator
    {
        private readonly ExpressionConverter converter;
        private readonly ImmutableDictionary<string, VariableSymbol> variablesByName;
        private readonly ConcurrentDictionary<VariableSymbol, ReplEvalResult> varResults = new();

        public ReplEvaluator(SemanticModel semanticModel)
        {
            var emitterContext = new EmitterContext(semanticModel);
            converter = new ExpressionConverter(emitterContext);

            this.variablesByName = semanticModel.Root.VariableDeclarations
                .GroupBy(x => x.Name, LanguageConstants.IdentifierComparer)
                .ToImmutableDictionary(x => x.Key, x => x.First(), LanguageConstants.IdentifierComparer);
        }

        public ReplEvalResult EvaluateExpression(SyntaxBase expression)
        {
            try
            {
                var context = GetExpressionEvaluationContext();
                var intermediate = converter.ConvertToIntermediateExpression(expression);
                var result = converter.ConvertExpression(intermediate).EvaluateExpression(context);

                return ReplEvalResult.For(result);
            }
            catch (Exception ex)
            {
                return ReplEvalResult.For(DiagnosticBuilder.ForPosition(expression)
                    .FailedToEvaluateSubject("expression", expression.ToString(), ex.Message));
            }
        }

        private ReplEvaluationContext GetExpressionEvaluationContext()
        {
            var helper = new TemplateExpressionEvaluationHelper
            {
                OnGetVariable = (name, _) =>
                {
                    if (this.variablesByName.TryGetValue(name, out var variable))
                    {
                        return this.EvaluateVariable(variable).Value ?? throw new InvalidOperationException($"Variable '{name}' has an invalid value.");
                    }

                    throw new InvalidOperationException($"Variable {name} not found");
                }
            };

            return new(helper, this);
        }

        private ReplEvalResult EvaluateVariable(VariableSymbol variable)
        {
            return varResults.GetOrAdd(variable, v =>
            {
                try
                {
                    var context = GetExpressionEvaluationContext();
                    var intermediate = converter.ConvertToIntermediateExpression(v.DeclaringVariable.Value);
                    var result = converter.ConvertExpression(intermediate).EvaluateExpression(context);

                    return ReplEvalResult.For(result);
                }
                catch (Exception ex)
                {
                    return ReplEvalResult.For(DiagnosticBuilder.ForPosition(v.DeclaringVariable)
                        .FailedToEvaluateSubject("variable", v.Name, ex.Message));
                }
            });
        }
    }

    public class ReplEvalResult
    {
        private ReplEvalResult(JToken? value, IEnumerable<IDiagnostic>? diagnostics)
        {
            Value = value;
            Diagnostics = diagnostics?.ToList() ?? [];
        }

        public JToken? Value { get; }
        public IReadOnlyList<IDiagnostic> Diagnostics { get; }

        public static ReplEvalResult For(JToken value) => new(value, null);
        public static ReplEvalResult For(IDiagnostic diagnostic) => new(null, new[] { diagnostic });
        public static ReplEvalResult For(IEnumerable<IDiagnostic> diagnostics) => new(null, diagnostics);
        public static ReplEvalResult Empty => new(null, null);
        public bool HasValue => Value is not null && !Diagnostics.Any();
    }
    
    private class ReplEvaluationContext : IEvaluationContext
    {
        private readonly TemplateExpressionEvaluationHelper evaluationHelper;
        private readonly ReplEvaluator evaluator;

        public ReplEvaluationContext(TemplateExpressionEvaluationHelper evaluationHelper, ReplEvaluator evaluator)
            : this(evaluationHelper, evaluator, evaluationHelper.EvaluationContext.Scope)
        {
        }

        private ReplEvaluationContext(TemplateExpressionEvaluationHelper evaluationHelper, ReplEvaluator evaluator, ExpressionScope scope)
        {
            this.evaluationHelper = evaluationHelper;
            this.evaluator = evaluator;
            this.Scope = scope;
        }

        public bool IsShortCircuitAllowed => evaluationHelper.EvaluationContext.IsShortCircuitAllowed;

        public ExpressionScope Scope { get; }

        public bool AllowInvalidProperty(Exception exception, FunctionExpression functionExpression, FunctionArgument[] functionParametersValues, JToken[] selectedProperties) =>
            evaluationHelper.EvaluationContext.AllowInvalidProperty(exception, functionExpression, functionParametersValues, selectedProperties);

        public JToken EvaluateFunction(FunctionExpression functionExpression, FunctionArgument[] parameters, IEvaluationContext context, TemplateErrorAdditionalInfo? additionalnfo)
        {
            return evaluationHelper.EvaluationContext.EvaluateFunction(functionExpression, parameters, this, additionalnfo);
        }

        public bool ShouldIgnoreExceptionDuringEvaluation(Exception exception) =>
            this.evaluationHelper.EvaluationContext.ShouldIgnoreExceptionDuringEvaluation(exception);

        public IEvaluationContext WithNewScope(ExpressionScope scope) => new ReplEvaluationContext(this.evaluationHelper, this.evaluator, scope);

    }
}