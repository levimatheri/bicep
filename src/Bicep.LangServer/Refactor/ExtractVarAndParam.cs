// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Bicep.Core;
using Bicep.Core.CodeAction;
using Bicep.Core.Extensions;
using Bicep.Core.Parsing;
using Bicep.Core.PrettyPrintV2;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.Core.Text;
using Bicep.Core.TypeSystem;
using Bicep.Core.TypeSystem.Types;
using Bicep.LanguageServer.CompilationManager;
using Bicep.LanguageServer.Completions;
using Google.Protobuf.WellKnownTypes;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using static Bicep.LanguageServer.Completions.BicepCompletionContext;
using static Bicep.LanguageServer.Refactor.TypeStringifier;
using static Google.Protobuf.Reflection.ExtensionRangeOptions.Types;

namespace Bicep.LanguageServer.Refactor;

/*asdfg
*
	 * A command this code action executes. If a code action
	 * provides an edit and a command, first the edit is
	 * executed and then the command.
	 *
	command?: Command;
*/

// asdfg Convert var to param

/*
Nullable-typed parameters may not be assigned default values. They have an implicit default of 'null' that cannot be overridden.bicep(BCP326):

    var commandToExecute = 'powershell -ExecutionPolicy Unrestricted -File writeblob.ps1'
resource testResource 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' {
                properties: {
                    publisher: 'Microsoft.Compute'
                    type: 'CustomScriptExtension'
                    typeHandlerVersion: '1.8'
                    autoUpgradeMinorVersion: true
                    settings: {
                        fileUris: [
                            uri(_artifactsLocation, 'writeblob.ps1${_artifactsLocationSasToken}')
                        ]
                        commandToExecute: commandToExecute
                    }
                }
            }

EXTRACT settings:

    var commandToExecute = 'powershell -ExecutionPolicy Unrestricted -File writeblob.ps1'

param settings { commandToExecute: string, fileUris: string[] }? = {
fileUris: [
uri(_artifactsLocation, 'writeblob.ps1${_artifactsLocationSasToken}')
]
commandToExecute: commandToExecute
}
resource testResource 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' {
            properties: {
                publisher: 'Microsoft.Compute'
                type: 'CustomScriptExtension'
                typeHandlerVersion: '1.8'
                autoUpgradeMinorVersion: true
                settings: settings
            }
        }

*/

// Provides code actions/fixes for a range in a Bicep document
public static class ExtractVarAndParam
{
    private const int MaxExpressionLengthInCodeAction = 35;
    private static int option = 5; //asdfg
    private static Regex regexCompactWhitespace = new("\\s+");

    static string NewLine(SemanticModel semanticModel) => semanticModel.Configuration.Formatting.Data.NewlineKind.ToEscapeSequence();

    public static IEnumerable<CodeFix> GetSuggestions(
        CompilationContext compilationContext,
        Compilation compilation,
        SemanticModel semanticModel,
        List<SyntaxBase> nodesInRange)
    {
        if (SyntaxMatcher.FindLastNodeOfType<ExpressionSyntax, ExpressionSyntax>(nodesInRange) is not (ExpressionSyntax expressionSyntax, _))
        {
            yield break;
        }

        TypeProperty? typeProperty = null; // asdfg better name
        string? defaultNewName = null;

        // Semi-intelligent default names for new variable
        if (semanticModel.Binder.GetParent(expressionSyntax) is ObjectPropertySyntax propertySyntax
            && propertySyntax.TryGetKeyText() is string propertyName)
        {
            // `{ objectPropertyName: <<expression>> }` // entire property value expression selected
            //   -> default to the name "objectPropertyName"
            defaultNewName = propertyName;
            typeProperty = propertySyntax.TryGetTypeProperty(semanticModel); //asdfg testpoint
        }
        else if (expressionSyntax is ObjectPropertySyntax propertySyntax2
            && propertySyntax2.TryGetKeyText() is string propertyName2)
        {
            // `{ <<objectPropertyName>>: expression }` // property itself is selected
            //   -> default to the name "objectPropertyName"
            defaultNewName = propertyName2;

            // The expression we want to replace is the property value, not the property syntax
            var propertyValueSyntax = propertySyntax2.Value as ExpressionSyntax;
            if (propertyValueSyntax != null)
            {
                expressionSyntax = propertyValueSyntax;
                typeProperty = propertySyntax2.TryGetTypeProperty(semanticModel); //asdfg testpoint
            }
            else
            {
                yield break;
            }
        }
        else if (expressionSyntax is PropertyAccessSyntax propertyAccessSyntax)
        {
            // `object.topPropertyName.propertyName`
            //   -> default to the name "topPropertyNamePropertyName"
            //
            // `object.topPropertyName.propertyName`
            //   -> default to the name "propertyName"
            //
            // More than two levels is less likely to be desirable

            string lastPartName = propertyAccessSyntax.PropertyName.IdentifierName;
            var parent = propertyAccessSyntax.BaseExpression;
            string? firstPartName = parent switch
            {
                PropertyAccessSyntax propertyAccess => propertyAccess.PropertyName.IdentifierName,
                VariableAccessSyntax variableAccess => variableAccess.Name.IdentifierName,
                FunctionCallSyntax functionCall => functionCall.Name.IdentifierName,
                _ => null
            };

            defaultNewName = firstPartName is { } ? firstPartName + lastPartName.UppercaseFirstLetter() : lastPartName;
        }

        if (semanticModel.Binder.GetNearestAncestor<StatementSyntax>(expressionSyntax) is not StatementSyntax statementSyntax)
        {
            yield break;
        }

        var newVarName = FindUnusedName(compilation, expressionSyntax.Span.Position, defaultNewName ?? "newVariable");
        var newParamName = FindUnusedName(compilation, expressionSyntax.Span.Position, defaultNewName ?? "newParameter");

        var varDeclarationSyntax = SyntaxFactory.CreateVariableDeclaration(newVarName, expressionSyntax);
        var varDeclaration = PrettyPrinterV2.PrintValid(varDeclarationSyntax, PrettyPrinterV2Options.Default) + NewLine(semanticModel); //asdfg

        var statementLineNumber = TextCoordinateConverter.GetPosition(compilationContext.LineStarts, statementSyntax.Span.Position).line;
        var definitionInsertionPosition = TextCoordinateConverter.GetOffset(compilationContext.LineStarts, statementLineNumber, 0);

        if (option == 1)
        {
            yield return new CodeFix(
           $"Extract variable",
           isPreferred: false,
           CodeFixKind.RefactorExtract,
           new CodeReplacement(expressionSyntax.Span, newVarName),
           new CodeReplacement(new TextSpan(definitionInsertionPosition, 0), varDeclaration));
        }
        if (option == 2)
        {
            yield return new CodeFix(
           $"Extract variable for {GetQuotedStringifiedType(expressionSyntax)}",
           isPreferred: false,
           CodeFixKind.RefactorExtract,
           new CodeReplacement(expressionSyntax.Span, newVarName),
           new CodeReplacement(new TextSpan(definitionInsertionPosition, 0), varDeclaration));
        }
        if (option == 3)
        {
            yield return new CodeFix(
           $"Extract variable for {GetQuotedStringifiedType(expressionSyntax)}",
           isPreferred: false,
           CodeFixKind.RefactorExtract,
           new CodeReplacement(expressionSyntax.Span, newVarName),
           new CodeReplacement(new TextSpan(definitionInsertionPosition, 0), varDeclaration));
        }
        if (option == 4)
        {
            yield return new CodeFix(
           $"Extract variable for {GetQuotedStringifiedType(expressionSyntax)}",
           isPreferred: false,
           CodeFixKind.RefactorExtract,
           new CodeReplacement(expressionSyntax.Span, newVarName),
           new CodeReplacement(new TextSpan(definitionInsertionPosition, 0), varDeclaration));
        }
        if (option == 5)
        {
            yield return new CodeFix(
           $"Extract variable",
           isPreferred: false,
           CodeFixKind.RefactorExtract,
           new CodeReplacement(expressionSyntax.Span, newVarName),
           new CodeReplacement(new TextSpan(definitionInsertionPosition, 0), varDeclaration));
        }
        if (option == 6)
        {
            yield return new CodeFix(
           $"Extract variable for {GetQuotedStringifiedType(expressionSyntax)}",
           isPreferred: false,
           CodeFixKind.RefactorExtract,
           new CodeReplacement(expressionSyntax.Span, newVarName),
           new CodeReplacement(new TextSpan(definitionInsertionPosition, 0), varDeclaration));
        }
        if (option == 7)
        {
            yield return new CodeFix(
           $"Extract variable for {GetQuotedStringifiedType(expressionSyntax)}",
           isPreferred: false,
           CodeFixKind.RefactorExtract,
           new CodeReplacement(expressionSyntax.Span, newVarName),
           new CodeReplacement(new TextSpan(definitionInsertionPosition, 0), varDeclaration));
        }

        // For the new param's type, try to use the declared type if there is one (i.e. the type of
        //   what we're assigning to), otherwise use the actual calculated type of the expression
        var inferredType = semanticModel.GetTypeInfo(expressionSyntax);
        var declaredType = semanticModel.GetDeclaredType(expressionSyntax);
        var newParamType = NullIfErrorOrAny(declaredType) ?? NullIfErrorOrAny(inferredType);

        var stringifiedNewParamTypeLoose = Stringify(newParamType, typeProperty, Strictness.Loose);
        var stringifiedNewParamTypeMedium = Stringify(newParamType, typeProperty, Strictness.Medium);

        if (option == 1)
        {
            yield return CreateExtractParameterCodeFix(
                "Extract parameter",
                semanticModel, typeProperty, stringifiedNewParamTypeLoose, newParamName, definitionInsertionPosition, expressionSyntax, Strictness.Loose);

            if (!string.Equals(stringifiedNewParamTypeLoose, stringifiedNewParamTypeMedium, StringComparison.Ordinal))
            {
                var customTypedParamExtraction = CreateExtractParameterCodeFix(
                   "Extract parameter with strict typing",
                    semanticModel, typeProperty, stringifiedNewParamTypeMedium, newParamName, definitionInsertionPosition, expressionSyntax, Strictness.Medium);
                yield return customTypedParamExtraction;
            }
        }

        if (option == 2)
        {
            yield return CreateExtractParameterCodeFix(
                $"Extract parameter for {GetQuotedStringifiedType(expressionSyntax)}",
                semanticModel, typeProperty, stringifiedNewParamTypeLoose, newParamName, definitionInsertionPosition, expressionSyntax, Strictness.Loose);

            if (!string.Equals(stringifiedNewParamTypeLoose, stringifiedNewParamTypeMedium, StringComparison.Ordinal))
            {
                var customTypedParamExtraction = CreateExtractParameterCodeFix(
                    $"Create parameter with strict typing for {GetQuotedStringifiedType(expressionSyntax)}",
                    semanticModel, typeProperty, stringifiedNewParamTypeMedium, newParamName, definitionInsertionPosition, expressionSyntax, Strictness.Medium);
                yield return customTypedParamExtraction;
            }
        }

        if (option == 3)
        {
            yield return CreateExtractParameterCodeFix(
                $"Extract parameter for {GetQuotedStringifiedType(expressionSyntax)}",
                semanticModel, typeProperty, stringifiedNewParamTypeLoose, newParamName, definitionInsertionPosition, expressionSyntax, Strictness.Loose);

            if (!string.Equals(stringifiedNewParamTypeLoose, stringifiedNewParamTypeMedium, StringComparison.Ordinal))
            {
                var customTypedParamExtraction = CreateExtractParameterCodeFix(
                    $"Extract parameter with strict typing for {GetQuotedStringifiedType(expressionSyntax)}",
                    semanticModel, typeProperty, stringifiedNewParamTypeMedium, newParamName, definitionInsertionPosition, expressionSyntax, Strictness.Medium);
                yield return customTypedParamExtraction;
            }
        }

        if (option == 4)
        {
            yield return CreateExtractParameterCodeFix(
                $"Extract parameter for {GetQuotedStringifiedType(expressionSyntax)}",
                semanticModel, typeProperty, stringifiedNewParamTypeLoose, newParamName, definitionInsertionPosition, expressionSyntax, Strictness.Loose);

            if (!string.Equals(stringifiedNewParamTypeLoose, stringifiedNewParamTypeMedium, StringComparison.Ordinal))
            {
                var customTypedParamExtraction = CreateExtractParameterCodeFix(
                    $"Extract parameter with strict typing for {GetQuotedStringifiedType(expressionSyntax)}",
                    semanticModel, typeProperty, stringifiedNewParamTypeMedium, newParamName, definitionInsertionPosition, expressionSyntax, Strictness.Medium);
                yield return customTypedParamExtraction;
            }
        }

        if (option == 5)
        {
            yield return CreateExtractParameterCodeFix(
                $"Extract parameter of type {GetQuotedText(stringifiedNewParamTypeLoose)}",
                semanticModel, typeProperty, stringifiedNewParamTypeLoose, newParamName, definitionInsertionPosition, expressionSyntax, Strictness.Loose);

            if (!string.Equals(stringifiedNewParamTypeLoose, stringifiedNewParamTypeMedium, StringComparison.Ordinal))
            {
                var customTypedParamExtraction = CreateExtractParameterCodeFix(
                    $"Extract parameter of type {GetQuotedText(stringifiedNewParamTypeMedium)}",
                    semanticModel, typeProperty, stringifiedNewParamTypeMedium, newParamName, definitionInsertionPosition, expressionSyntax, Strictness.Medium);
                yield return customTypedParamExtraction;
            }
        }

        if (option == 6)
        {
            yield return CreateExtractParameterCodeFix(
                $"Extract parameter of type {GetQuotedText(stringifiedNewParamTypeLoose)}",
                semanticModel, typeProperty, stringifiedNewParamTypeLoose, newParamName, definitionInsertionPosition, expressionSyntax, Strictness.Loose);

            if (!string.Equals(stringifiedNewParamTypeLoose, stringifiedNewParamTypeMedium, StringComparison.Ordinal))
            {
                var customTypedParamExtraction = CreateExtractParameterCodeFix(
                    $"Extract parameter of type {GetQuotedText(stringifiedNewParamTypeMedium)}",
                    semanticModel, typeProperty, stringifiedNewParamTypeMedium, newParamName, definitionInsertionPosition, expressionSyntax, Strictness.Medium);
                yield return customTypedParamExtraction;
            }
        }

        if (option == 7)
        {
            yield return CreateExtractParameterCodeFix(
                $"Extract parameter for {GetQuotedStringifiedType(expressionSyntax)} of type {GetQuotedText(stringifiedNewParamTypeLoose)}",
                semanticModel, typeProperty, stringifiedNewParamTypeLoose, newParamName, definitionInsertionPosition, expressionSyntax, Strictness.Loose);

            if (!string.Equals(stringifiedNewParamTypeLoose, stringifiedNewParamTypeMedium, StringComparison.Ordinal))
            {
                var customTypedParamExtraction = CreateExtractParameterCodeFix(
                    $"Extract parameterfor {GetQuotedStringifiedType(expressionSyntax)} of type {GetQuotedText(stringifiedNewParamTypeMedium)}",
                    semanticModel, typeProperty, stringifiedNewParamTypeMedium, newParamName, definitionInsertionPosition, expressionSyntax, Strictness.Medium);
                yield return customTypedParamExtraction;
            }
        }

        if (option == 8)
        {
            yield return CreateExtractParameterCodeFix(
                $"Extract parameter of type {GetQuotedText(stringifiedNewParamTypeLoose)} for {GetQuotedStringifiedType(expressionSyntax)}",
                semanticModel, typeProperty, stringifiedNewParamTypeLoose, newParamName, definitionInsertionPosition, expressionSyntax, Strictness.Loose);

            if (!string.Equals(stringifiedNewParamTypeLoose, stringifiedNewParamTypeMedium, StringComparison.Ordinal))
            {
                var customTypedParamExtraction = CreateExtractParameterCodeFix(
                    $"Extract parameter of type {GetQuotedText(stringifiedNewParamTypeMedium)} for {GetQuotedStringifiedType(expressionSyntax)}",
                    semanticModel, typeProperty, stringifiedNewParamTypeMedium, newParamName, definitionInsertionPosition, expressionSyntax, Strictness.Medium);
                yield return customTypedParamExtraction;
            }
        }
    }

    private static CodeFix CreateExtractParameterCodeFix(
        string title,
        SemanticModel semanticModel,
        TypeProperty? typeProperty,
        string stringifiedNewParamType,
        string newParamName,
        int definitionInsertionPosition,
        ExpressionSyntax expressionSyntax,
        Strictness strictness)
    {
        var declaration = CreateNewParameterDeclaration(semanticModel, typeProperty, stringifiedNewParamType, newParamName, expressionSyntax, strictness);

        return new CodeFix(
            title,
            isPreferred: false,
            CodeFixKind.RefactorExtract,
            new CodeReplacement(expressionSyntax.Span, newParamName),
            new CodeReplacement(new TextSpan(definitionInsertionPosition, 0), declaration));
    }

    private static string CreateNewParameterDeclaration(
        SemanticModel semanticModel,
        TypeProperty? typeProperty,
        string stringifiedNewParamType,
        string newParamName,
        SyntaxBase defaultValueSyntax,
        Strictness strictness)
    {
        var newParamTypeIdentifier = SyntaxFactory.CreateIdentifierWithTrailingSpace(stringifiedNewParamType);

        var description = typeProperty?.Description;
        SyntaxBase[]? leadingNodes = description == null //asdfg extract
            ? null
            : [
                SyntaxFactory.CreateDecorator(
                    "description"/*asdfg what about sys.?*/,
                    SyntaxFactory.CreateStringLiteral(description)),
                SyntaxFactory.GetNewlineToken()
               ];

        var paramDeclarationSyntax = SyntaxFactory.CreateParameterDeclaration(
            newParamName,
            new TypeVariableAccessSyntax(newParamTypeIdentifier),
            defaultValueSyntax,
            leadingNodes);
        var paramDeclaration = PrettyPrinterV2.PrintValid(paramDeclarationSyntax, PrettyPrinterV2Options.Default) + NewLine(semanticModel);
        return paramDeclaration;
    }

    private static TypeSymbol? NullIfErrorOrAny(TypeSymbol? type) => type is ErrorType || type is AnyType ? null : type;

    private static string FindUnusedName(Compilation compilation, int offset, string preferredName) //asdfg
    {
        var activeScopes = ActiveScopesVisitor.GetActiveScopes(compilation.GetEntrypointSemanticModel().Root, offset);
        for (int i = 1; i < int.MaxValue; ++i)
        {
            var tryingName = $"{preferredName}{(i < 2 ? "" : i)}";
            if (!activeScopes.Any(s => s.GetDeclarationsByName(tryingName).Any()))
            {
                preferredName = tryingName;
                break;
            }
        }

        return preferredName;
    }

    private static string GetQuotedStringifiedType(ExpressionSyntax expressionSyntax)
    {
        return GetQuotedText(SyntaxStringifier.Stringify(expressionSyntax));
    }

    private static string GetQuotedText(string text)
    {
        return "\""
            + regexCompactWhitespace.Replace(text, " ")
                .TruncateWithEllipses(MaxExpressionLengthInCodeAction)
                .Trim()
            + "\"";
    }
}
