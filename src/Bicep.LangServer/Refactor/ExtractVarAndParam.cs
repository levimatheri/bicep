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


asdfg
var should get name "stgName" but is just "name"

resource stg 'Microsoft.Storage/storageAccounts@2019-04-01' = {
  name: '${storagePrefix}${uniqueString(resourceGroup().id)}'
  location: location
  sku: {
    name: storageSKU
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
  }
}


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

    public static IEnumerable<(CodeFix fix, Position renamePosition)> GetRefactoringFixes(
        CompilationContext compilationContext,
        SemanticModel semanticModel,
        List<SyntaxBase> nodesInRange)
    {
        if (SyntaxMatcher.FindLastNodeOfType<ExpressionSyntax, ExpressionSyntax>(nodesInRange) is not (ExpressionSyntax expressionSyntax, _))
        {
            return [];
        }

        TypeProperty? parentTypeProperty = null; // The property inside a parent object whose value is being extracted, if any
        string? defaultNewName = null;
        string newLine = NewLine(semanticModel); //asdfg still useful?

        // Pick a semi-intelligent default name for the new param and variable.
        // Also, adjust the expression we're replacing if a property itself has been selected.

        if (semanticModel.Binder.GetParent(expressionSyntax) is ObjectPropertySyntax propertySyntax
            && propertySyntax.TryGetKeyText() is string propertyName)
        {
            // `{ objectPropertyName: <<expression>> }` // entire property value expression selected
            //   -> default to the name "objectPropertyName"
            defaultNewName = propertyName;
            parentTypeProperty = propertySyntax.TryGetTypeProperty(semanticModel); //asdfg testpoint
        }
        else if (expressionSyntax is ObjectPropertySyntax propertySyntax2 //asdfg rename
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
                parentTypeProperty = propertySyntax2.TryGetTypeProperty(semanticModel); //asdfg testpoint
            }
            else
            {
                return [];
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

        if (semanticModel.Binder.GetNearestAncestor<StatementSyntax>(expressionSyntax) is not StatementSyntax parentStatement)
        {
            return [];
        }

        return CreateExtractions(
            compilationContext,
            semanticModel,
            parentStatement,
            expressionSyntax,
            parentTypeProperty,
            defaultNewName);
    }

    private static IEnumerable<(CodeFix fix, Position renamePosition)> CreateExtractions(
        CompilationContext compilationContext,
        SemanticModel semanticModel, //asdfg can get from context?
        StatementSyntax parentStatement,
        ExpressionSyntax expressionToExtract,
        TypeProperty? parentTypeProperty,
        string? defaultNewName
    ) {
        var newVarName = FindUnusedName(compilationContext.Compilation, expressionToExtract.Span.Position, defaultNewName ?? "newVariable");
        var newParamName = FindUnusedName(compilationContext.Compilation, expressionToExtract.Span.Position, defaultNewName ?? "newParameter");

        var (newVarInsertionOffset, insertBlankLineBeforeNewVar) = FindOffsetToInsertNewDeclarationOfType<VariableDeclarationSyntax>(
            compilationContext,
            parentStatement.Span.Position);

        //asdfg create CreateExtractParameterCodeFix for var?
        var newVarDeclarationSyntax = SyntaxFactory.CreateVariableDeclaration(newVarName, expressionToExtract);
        var newVarDeclarationText = PrettyPrintDeclaration(newVarDeclarationSyntax, semanticModel);
        var identifierOffsetAsdfg = newVarDeclarationText.IndexOf("var " + newVarName);
        Debug.Assert(identifierOffsetAsdfg >= 0);
        identifierOffsetAsdfg += "var ".Length;

        var newVarDeclaration = new DeclarationASdfg(
            newVarDeclarationText,
           identifierOffsetAsdfg);
        if (insertBlankLineBeforeNewVar)
        {
            newVarDeclaration.Prepend(NewLine(semanticModel));
        }

        //asdfg combine with params
        var definitionInsertionPositionAsdfg = TextCoordinateConverter.GetPosition(compilationContext.LineStarts, newVarInsertionOffset);
        var declarationLineStartsAsdfg = TextCoordinateConverter.GetLineStarts(newVarDeclaration.Text);
        var renameRelativePositionAsdfg = TextCoordinateConverter.GetPosition(declarationLineStartsAsdfg, newVarDeclaration.RenameOffset);
        var renamePositionAsdfg = definitionInsertionPositionAsdfg with
        {
            line = definitionInsertionPositionAsdfg.line + renameRelativePositionAsdfg.line,
            character = definitionInsertionPositionAsdfg.character + renameRelativePositionAsdfg.character
        };

        var varFix = new CodeFix( //asdfg extract common with params
            $"Extract variable",
            isPreferred: false,
            CodeFixKind.RefactorExtract,
            new CodeReplacement(new TextSpan(newVarInsertionOffset, 0), newVarDeclaration.Text),
            new CodeReplacement(expressionToExtract.Span, newVarName));
        Debug.Assert(varFix.Replacements.First().Text.Substring(newVarDeclaration.RenameOffset - "var ".Length, "var ".Length) == "var ", "Rename is set at the wrong position"); //asdfg remove these
        yield return (varFix, renamePositionAsdfg);

        // For the new param's type, try to use the declared type if there is one (i.e. the type of
        //   what we're assigning to), otherwise use the actual calculated type of the expression
        var inferredType = semanticModel.GetTypeInfo(expressionToExtract);
        var declaredType = semanticModel.GetDeclaredType(expressionToExtract);
        var newParamType = NullIfErrorOrAny(declaredType) ?? NullIfErrorOrAny(inferredType);

        // Don't create nullable params - they're not allowed to have default values
        var ignoreTopLevelNullability = true;

        // Strict typing for the param doesn't appear useful, providing only loose and medium at the moment
        var stringifiedNewParamTypeLoose = Stringify(newParamType, parentTypeProperty, Strictness.Loose, ignoreTopLevelNullability);
        var stringifiedNewParamTypeMedium = Stringify(newParamType, parentTypeProperty, Strictness.Medium, ignoreTopLevelNullability);

        var multipleParameterTypesAvailable = !string.Equals(stringifiedNewParamTypeLoose, stringifiedNewParamTypeMedium, StringComparison.Ordinal);

        var (newParamInsertionOffset, insertBlankLineBeforeNewParam) = FindOffsetToInsertNewDeclarationOfType<ParameterDeclarationSyntax>(compilationContext, parentStatement.Span.Position);

        var (looseFix, looseRenamePosition) = CreateExtractParameterCodeFix(
                multipleParameterTypesAvailable
                    ? $"Extract parameter of type {GetQuotedText(stringifiedNewParamTypeLoose)}"
                    : "Extract parameter",
                semanticModel, compilationContext.LineStarts, parentTypeProperty, stringifiedNewParamTypeLoose, newParamName, newParamInsertionOffset, expressionToExtract, Strictness.Loose, insertBlankLineBeforeNewParam);
        //Debug.Assert(looseFix.Replacements.First().Text.Substring(looseRenameOffset - newParamInsertionOffset - "param ".Length, "param ".Length) == "param ", "Rename is set at the wrong position"); //asdfg remove these
        yield return (looseFix, looseRenamePosition);

        if (multipleParameterTypesAvailable)
        {
            var (mediumFix, mediumRenamePosition) = CreateExtractParameterCodeFix(
                $"Extract parameter of type {GetQuotedText(stringifiedNewParamTypeMedium)}",
                semanticModel, compilationContext.LineStarts, parentTypeProperty, stringifiedNewParamTypeMedium, newParamName, newParamInsertionOffset, expressionToExtract, Strictness.Medium, insertBlankLineBeforeNewParam);
            //Debug.Assert(mediumFix.Replacements.First().Text.Substring(mediumRenameOffset - newParamInsertionOffset - "param ".Length, "param ".Length) == "param ", "Rename is set at the wrong position"); //asdfg remove these
            yield return (mediumFix, mediumRenamePosition);
        }
    }

    private class DeclarationASdfg //asdfg can I do this by creating a syntax tree instead?
    {
        public string Text { get; private set; }
        public int RenameOffset { get; private set; }
        //asdfg public (int line, int character) RenamePosition => TextCoordinateConverter.GetPosition(_lineStarts, RenameOffset);

        public DeclarationASdfg(string declarationText, int renameOffset)
        {
            Text = declarationText;
            RenameOffset = renameOffset;
        }

        public void Prepend(string s)
        {
            Text = s + Text;
            RenameOffset += s.Length;
        }
    }

    private static (CodeFix fix, Position renamePosition)/*asdfg type?*/ CreateExtractParameterCodeFix(
        string title,
        SemanticModel semanticModel,
        ImmutableArray<int> lineStarts,
        TypeProperty? typeProperty,
        string stringifiedNewParamType,
        string newParamName,
        int definitionInsertionOffset,
        ExpressionSyntax expressionSyntax,
        Strictness strictness,
        bool blankLineBefore)
    {
        var declaration = CreateNewParameterDeclaration(semanticModel, typeProperty, stringifiedNewParamType, newParamName, expressionSyntax, strictness);

        if (blankLineBefore)
        {
            //asdfg move to CreateNewParameterDeclaration?
            declaration.Prepend(NewLine(semanticModel));
        }

        //asdfg
        var definitionInsertionPosition = TextCoordinateConverter.GetPosition(lineStarts, definitionInsertionOffset);
        var declarationLineStarts = TextCoordinateConverter.GetLineStarts(declaration.Text);
        var renameRelativePosition = TextCoordinateConverter.GetPosition(declarationLineStarts, declaration.RenameOffset);
        var renamePosition = definitionInsertionPosition with
        {
            line = definitionInsertionPosition.line + renameRelativePosition.line,
            character = definitionInsertionPosition.character + renameRelativePosition.character
        };

        var fix = new CodeFix(
            title,
            isPreferred: false,
            CodeFixKind.RefactorExtract,
            new CodeReplacement(new TextSpan(definitionInsertionOffset, 0), declaration.Text),
            new CodeReplacement(expressionSyntax.Span, newParamName));
        return (fix, renamePosition);
    }

    private static DeclarationASdfg CreateNewParameterDeclaration(
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
        var paramDeclarationText = PrettyPrintDeclaration(paramDeclarationSyntax, semanticModel);
        //asdfg better way to do this?  what if param name contains weird characters, requires '@'?
        var identifierOffset = paramDeclarationText.IndexOf("param " + newParamName) + "param ".Length; // asdfg what if -1?
        return new DeclarationASdfg(paramDeclarationText, identifierOffset);
    }

    //asdfg TODO: better would be to create an actual syntax tree, but it would need to be modified to handle special case
    private static string PrettyPrintDeclaration(StatementSyntax paramDeclarationSyntax, SemanticModel semanticModel) //asdfg
    {
        var declarationText = PrettyPrinterV2.PrintValid(paramDeclarationSyntax, PrettyPrinterV2Options.Default) + NewLine(semanticModel);
        var p = new Parser(declarationText);
        var prettyDeclarationText = PrettyPrinterV2.PrintValid(p.Program(), PrettyPrinterV2Options.Default);
        return prettyDeclarationText;
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

    private static (int offset, bool insertBlankLineBefore) FindOffsetToInsertNewDeclarationOfType<T>(CompilationContext compilationContext, int extractionOffset)
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
