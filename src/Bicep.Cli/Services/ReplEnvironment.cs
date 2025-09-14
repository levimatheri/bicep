// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Azure.Deployments.Core.ErrorResponses;
using Azure.Deployments.Expression.Expressions;
using Azure.Deployments.Templates.Expressions;
using Bicep.Core;
using Bicep.Core.Diagnostics;
using Bicep.Core.Emit;
using Bicep.Core.Extensions;
using Bicep.Core.Parsing;
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
    private readonly Dictionary<string, string> variableDeclarations = new(LanguageConstants.IdentifierComparer);

    public ReplEnvironment(BicepCompiler compiler)
    {
        this.compiler = compiler;
        this.workspace = new Workspace();
        this.fileExplorer = new InMemoryFileExplorer();
        this.replFileUri = IOUri.FromFilePath("/repl.bicepparam");
        this.replContent = new StringBuilder("using none\n");
    }
    
    public ReplEvalResult EvaluateInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return ReplEvalResult.Empty;
        }

        var syntax = new ReplParser(input).Parse();
        if (syntax is VariableDeclarationSyntax varDecl)
        {
            return EvaluateVariableDeclaration(varDecl, input);
        }

        return EvaluateExpression(syntax, input);
    }
    
    private ReplEvalResult EvaluateVariableDeclaration(VariableDeclarationSyntax varDecl, string input)
    {
        var variableName = varDecl.Name.IdentifierName;

        // Store or update the variable declaration
        variableDeclarations[variableName] = input.Trim();

        try
        {
            // Build the complete content with all current variables
            var completeContent = BuildCompleteContent();
            var fullContent = "using none\n" + (string.IsNullOrEmpty(completeContent) ? "" : completeContent);

            var compilationResult = CompileContent(fullContent);
            if (compilationResult.HasErrors)
            {
                // If there's an error, revert the variable change
                variableDeclarations.Remove(variableName);
                return ReplEvalResult.For(compilationResult.Diagnostics);
            }

            // Successfully added/updated variable - sync replContent
            this.replContent.Clear();
            this.replContent.Append(fullContent);

            return ReplEvalResult.Empty;
        }
        catch (Exception ex)
        {
            // If there's an error, revert the variable change
            variableDeclarations.Remove(variableName);
            var diagnostic = DiagnosticBuilder.ForPosition(varDecl)
                .FailedToEvaluateSubject("variable", variableName, ex.Message);
            return ReplEvalResult.For(diagnostic);
        }
    }

    private ReplEvalResult EvaluateExpression(SyntaxBase syntax, string input)
    {
        var tempVarName = $"__temp_eval_{Guid.NewGuid():N}";
        var tempContent = new StringBuilder();
        
        // Add existing variables
        var completeContent = BuildCompleteContent();
        if (!string.IsNullOrEmpty(completeContent))
        {
            tempContent.AppendLine(completeContent);
        }
        
        // Add the expression as a temporary variable
        tempContent.AppendLine($"var {tempVarName} = {input}");
        var fullTempContent = "using none\n" + tempContent.ToString();

        var compilationResult = CompileContent(fullTempContent);
        if (compilationResult.HasErrors)
        {
            return ReplEvalResult.For(compilationResult.Diagnostics.Where(d => d.Level == DiagnosticLevel.Error));
        }

        // Find and evaluate the temporary variable
        if (compilationResult.Model == null)
        {
            return ReplEvalResult.For(DiagnosticBuilder.ForPosition(syntax)
                .FailedToEvaluateSubject("expression", input, "Compilation failed"));
        }

        var variable = compilationResult.Model.Root.VariableDeclarations.FirstOrDefault(v => v.Name == tempVarName);
        if (variable?.DeclaringVariable.Value is not SyntaxBase valueExpression)
        {
            return ReplEvalResult.For(DiagnosticBuilder.ForPosition(syntax)
                .FailedToEvaluateSubject("expression", input, "Unable to find temporary variable"));
        }

        var evaluator = new ReplEvaluator(compilationResult.Model);
        var result = evaluator.EvaluateExpression(valueExpression);
        return result;
    }
    private string BuildCompleteContent()
    {
        if (variableDeclarations.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, variableDeclarations.Values);
    }

    private CompilationResult CompileContent(string content)
    {
        var fileHandle = this.fileExplorer.GetFile(replFileUri);
        try
        {
            fileHandle.Write(content);
            var sourceFile = compiler.SourceFileFactory.CreateBicepParamFile(replFileUri, content);
            workspace.UpsertSourceFile(sourceFile);
            var compilation = compiler.CreateCompilationWithoutRestore(replFileUri, workspace);
            var model = compilation.GetEntrypointSemanticModel();
            var diagnostics = model.GetAllDiagnostics();
            
            var hasErrors = diagnostics.Any(d => d.Level == DiagnosticLevel.Error && d.Source != DiagnosticSource.CoreLinter);
            
            return new CompilationResult(model, diagnostics, hasErrors);
        }
        catch (Exception)
        {
            // If compilation fails, treat it as having errors
            return new CompilationResult(null, [], true);
        }
    }

    private record CompilationResult(SemanticModel? Model, ImmutableArray<IDiagnostic> Diagnostics, bool HasErrors);

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

        public ReplEvalResult EvaluateVariable(string variableName)
        {
            if (!this.variablesByName.TryGetValue(variableName, out var variable))
            {
                throw new InvalidOperationException($"Variable '{variableName}' not found");
            }

            return EvaluateVariable(variable);
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