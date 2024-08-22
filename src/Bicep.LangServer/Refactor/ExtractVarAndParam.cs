// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Drawing.Text;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Core.GeoJson;
using Bicep.Core;
using Bicep.Core.CodeAction;
using Bicep.Core.Extensions;
using Bicep.Core.Navigation;
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
using Grpc.Net.Client.Balancer;
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


asdfg DecoratorCodeFixProvider
*/

// asdfg Convert var to param

/*asdfg
 
 * type myMixedTypeArrayType = ('fizz' | 42 | {an: 'object'} | null)[]
 * 
 * 
 
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
    private static Regex regexCompactWhitespace = new("\\s+");

    static string NewLine(SemanticModel semanticModel) => semanticModel.Configuration.Formatting.Data.NewlineKind.ToEscapeSequence();

    public static IEnumerable<(CodeFix fix, (int line, int character) renamePosition)> GetRefactoringFixes(
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

        // Pick a semi-intelligent default name for the new param and variable.
        // Also, adjust the expression we're replacing if a property itself has been selected.

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

        if (semanticModel.Binder.GetNearestAncestor<StatementSyntax>(expressionSyntax) is not StatementSyntax statementWithExtraction)
        {
            yield break;
        }

        var newVarName = FindUnusedName(compilation, expressionSyntax.Span.Position, defaultNewName ?? "newVariable");
        var newParamName = FindUnusedName(compilation, expressionSyntax.Span.Position, defaultNewName ?? "newParameter");

        //asdfg create CreateExtractParameterCodeFix for var?
        var newVarDeclarationSyntax = SyntaxFactory.CreateVariableDeclaration(newVarName, expressionSyntax);
        var newVarDeclarationText = PrettyPrinterV2.PrintValid(newVarDeclarationSyntax, PrettyPrinterV2Options.Default) + NewLine(semanticModel); //asdfg

        var (newVarInsertionPosition, insertBlankLineBeforeNewVar) = FindPositionToInsertNewDeclarationOfType<VariableDeclarationSyntax>(compilationContext, statementWithExtraction.Span.Position);
        var (newParamInsertionPosition, insertBlankLineBeforeNewParam) = FindPositionToInsertNewDeclarationOfType<ParameterDeclarationSyntax>(compilationContext, statementWithExtraction.Span.Position);

        if (insertBlankLineBeforeNewVar)
        {
            newVarDeclarationText = NewLine(semanticModel) + newVarDeclarationText;
        }

        var newVarOffset = newVarInsertionPosition + "var ".Length; //asdfg
        yield return (
            fix: new CodeFix( //asdfg extract common with params
                $"Extract variable",
                isPreferred: false,
                CodeFixKind.RefactorExtract,
                new CodeReplacement(new TextSpan(newVarInsertionPosition, 0), newVarDeclarationText),
                new CodeReplacement(expressionSyntax.Span, newVarName)),
            renamePosition: TextCoordinateConverter.GetPosition(compilationContext.LineStarts, newVarOffset));

        // For the new param's type, try to use the declared type if there is one (i.e. the type of
        //   what we're assigning to), otherwise use the actual calculated type of the expression
        var inferredType = semanticModel.GetTypeInfo(expressionSyntax);
        var declaredType = semanticModel.GetDeclaredType(expressionSyntax);
        var newParamType = NullIfErrorOrAny(declaredType) ?? NullIfErrorOrAny(inferredType);

        // Don't create nullable params - they're not allowed to have default values asdfg test
        var ignoreTopLevelNullability = true;

        // Strict typing for the param doesn't appear useful, providing only loose and medium at the moment
        var stringifiedNewParamTypeLoose = Stringify(newParamType, typeProperty, Strictness.Loose, ignoreTopLevelNullability);
        var stringifiedNewParamTypeMedium = Stringify(newParamType, typeProperty, Strictness.Medium, ignoreTopLevelNullability);

        var multipleParameterTypesAvailable = !string.Equals(stringifiedNewParamTypeLoose, stringifiedNewParamTypeMedium, StringComparison.Ordinal);

        var paramRenamePosition = TextCoordinateConverter.GetPosition(compilationContext.LineStarts, 4);//asdfg

        yield return (
            CreateExtractParameterCodeFix(
                multipleParameterTypesAvailable
                    ? $"Extract parameter of type {GetQuotedText(stringifiedNewParamTypeLoose)}"
                    : "Extract parameter",
                semanticModel, typeProperty, stringifiedNewParamTypeLoose, newParamName, newParamInsertionPosition, expressionSyntax, Strictness.Loose, insertBlankLineBeforeNewParam),
            paramRenamePosition);

        if (multipleParameterTypesAvailable)
        {
            var customTypedParamExtraction = CreateExtractParameterCodeFix(
                $"Extract parameter of type {GetQuotedText(stringifiedNewParamTypeMedium)}",
                semanticModel, typeProperty, stringifiedNewParamTypeMedium, newParamName, newParamInsertionPosition, expressionSyntax, Strictness.Medium, insertBlankLineBeforeNewParam);
            yield return (customTypedParamExtraction, paramRenamePosition);
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
        Strictness strictness,
        bool blankLineBefore)
    {
        var declarationText = CreateNewParameterDeclaration(semanticModel, typeProperty, stringifiedNewParamType, newParamName, expressionSyntax, strictness);
        if (blankLineBefore)
        {
            declarationText = NewLine(semanticModel) + declarationText;
        }

        return new CodeFix(
            title,
            isPreferred: false,
            CodeFixKind.RefactorExtract,
            new CodeReplacement(new TextSpan(definitionInsertionPosition, 0), declarationText),
            new CodeReplacement(expressionSyntax.Span, newParamName));
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

    private static string GetQuotedText(string text)
    {
        return "\""
            + regexCompactWhitespace.Replace(text, " ")
                .TruncateWithEllipses(MaxExpressionLengthInCodeAction)
                .Trim()
            + "\"";
    }

    private static (int offset, bool insertBlankLineBefore) FindPositionToInsertNewDeclarationOfType<T>(CompilationContext compilationContext, int extractionOffset)
        where T : StatementSyntax, ITopLevelNamedDeclarationSyntax
    {
        //asdfg more testing?  Make sure can't crash
        ImmutableArray<int> lineStarts = compilationContext.LineStarts;

        var extractionLine = GetPosition(extractionOffset).line;
        var startSearchingAtLine = extractionLine - 1;

        for (int line = startSearchingAtLine; line >= 0; --line)
        {
            var existingDeclarationStatement = StatementOfTypeAtLine(line);
            if (existingDeclarationStatement != null)
            {
                // Insert on the line right after the existing declaration
                var insertionLine = line + 1;

                // Is there a blank line above this existing statement that we found (excluding its leading nodes)?
                //   If so, assume user probably wants one after as well.
                var beginningOffsetOfExistingDeclaration = existingDeclarationStatement.Span.Position;
                var beginningLineOfExistingDeclaration =
                    GetPosition(beginningOffsetOfExistingDeclaration)
                    .line;
                var insertBlankLineBeforeNewDeclaration = IsLineBeforeEmpty(beginningLineOfExistingDeclaration);

                return (GetOffset(insertionLine, 0), insertBlankLineBeforeNewDeclaration);
            }
        }

        // If no existing declarations of the desired type, insert right before the statement/asdfg containing the extraction expression
        return (GetOffset(extractionLine, 0), false);

        StatementSyntax? StatementOfTypeAtLine(int line) //asdfg does this work if there's whitespace at the beginning of the line?
        {
            var lineOffset = GetOffset(line, 0);
            var statementAtLine = SyntaxMatcher.FindNodesSpanningRange(compilationContext.ProgramSyntax, lineOffset, lineOffset).OfType<T>().FirstOrDefault();
            return statementAtLine;
        }

        bool IsLineBeforeEmpty(int line)
        {
            if (line == 0)
            {
                return false;
            }

            return IsLineEmpty(line - 1);
        }

        bool IsLineEmpty(int line) //asdfg rewrite properly
        {
            var lineSpan = TextCoordinateConverter.GetLineSpan(lineStarts, compilationContext.ProgramSyntax.GetEndPosition(), line);
            for (int i = lineSpan.Position; i <= lineSpan.Position + lineSpan.Length - 1; ++i)
            {
                // asdfg handle inside other scopes e.g. user functions
                if (SyntaxMatcher.FindNodesMatchingOffset(compilationContext.ProgramSyntax, i)
                    .Where(x => x is not ProgramSyntax)
                    .Where(x => x is not Token t || !string.IsNullOrWhiteSpace(t.Text))
                    .Any())
                {
                    return false;
                }
            }
            return true;
        }

        (int line, int character) GetPosition(int offset) => TextCoordinateConverter.GetPosition(lineStarts, offset);
        int GetOffset(int line, int character) => TextCoordinateConverter.GetOffset(lineStarts, line, character);
    }
}
