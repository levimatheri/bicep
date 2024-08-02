// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bicep.Core.Extensions;
using Bicep.Core.Parsing;
using Bicep.Core.PrettyPrintV2;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.Core.TypeSystem;
using Bicep.Core.TypeSystem.Types;
using Bicep.Core.UnitTests.Assertions;
using Bicep.Core.UnitTests.Utils;
using Bicep.LanguageServer.Refactor;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bicep.LangServer.IntegrationTests;

[TestClass]
public class StringizeTypeTests
{
    private static bool debugPrintAllSyntaxNodeTypes = false;

    // asdfg allowed values
    // @secure
    // asdfg string interpolations?

    [DataTestMethod]
    [DataRow(
        "type testType = int",
        "type loose = int",
        "type medium = int",
        "type strict = int")]
    [DataRow(
        "type testType = string",
        "type loose = string",
        "type medium = string",
        "type strict = string")]
    [DataRow(
        "type testType = bool",
        "type loose = bool",
        "type medium = bool",
        "type strict = bool")]
    [DataRow(
        "type testType = 'abc'?",
        "type loose = string?",
        "type medium = string?",
        "type strict = 'abc'?")]
    public void SimpleTypes(string typeDeclaration, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        RunTestFromTypeDeclaration(typeDeclaration, expectedLooseSyntax, expectedMediumStrictSyntax, expectedStrictSyntax);
    }

    [DataTestMethod]
    [DataRow(
        "type testType = 123",
        "type loose = int",
        "type medium = int",
        "type strict = 123")]
    [DataRow(
        "type testType = 'abc'",
        "type loose = string",
        "type medium = string",
        "type strict = 'abc'")]
    [DataRow(
        "type testType = true",
        "type loose = bool",
        "type medium = bool",
        "type strict = true")]
    public void LiteralTypes(string typeDeclaration, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        RunTestFromTypeDeclaration(typeDeclaration, expectedLooseSyntax, expectedMediumStrictSyntax, expectedStrictSyntax);
    }

    [DataTestMethod]
    [DataRow(
        "type testType = object",
        "type loose = object",
        "type medium = object",
        "type strict = object")]
    [DataRow(
        "type testType = {}",
        "type loose = object",
        "type medium = object",
        "type strict = { }")]
    [DataRow(
        "type testType = { empty: { } }",
        "type loose = object",
        "type medium = { empty: object }",
        "type strict = { empty: { } }")]
    [DataRow(
        "type testType = {a:123,b:'abc'}",
        "type loose = object",
        "type medium = { a: int, b: string }",
        "type strict = { a: 123, b: 'abc' }")]
    [DataRow(
        "type testType = { 'my type': 'my string' }",
        "type loose = object",
        "type medium = { 'my type': string }",
        "type strict = { 'my type': 'my string' }")]
    [DataRow(
        "type testType = { 'true': true }",
        "type loose = object",
        "type medium = { 'true': bool }",
        "type strict = { 'true': true }")]
    public void ObjectTypes(string typeDeclaration, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        RunTestFromTypeDeclaration(typeDeclaration, expectedLooseSyntax, expectedMediumStrictSyntax, expectedStrictSyntax);
    }

    [DataTestMethod]
    [DataRow(
        "type testType = 'abc' | 'def' | 'ghi'",
        "type loose = string",
        "type medium = 'abc' | 'def' | 'ghi'",
        "type strict = 'abc' | 'def' | 'ghi'")]
    [DataRow(
        "type testType = 1 | 2 | 3 | -1",
        "type loose = int",
        "type medium = -1 | 1 | 2 | 3",
        "type strict = -1 | 1 | 2 | 3")]
    [DataRow(
        "type testType = true|false",
        "type loose = bool",
        "type medium = false | true",
        "type strict = false | true")]
    [DataRow(
        "type testType = null|true|false",
        "type loose = bool?",
        "type medium = false | null | true",
        "type strict = false | null | true")]
    public void UnionTypes(string typeDeclaration, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        RunTestFromTypeDeclaration(typeDeclaration, expectedLooseSyntax, expectedMediumStrictSyntax, expectedStrictSyntax);
    }

    [DataTestMethod]
    [DataRow(
        "type testType = [ object, array, {}, [] ]",
        "type loose = array",
        "type medium = [ object, array, object, array ]",
        "type strict = [ object, array, {}, [] ]")]
    [DataRow(
        "type testType = [int, string]",
        "type loose = array",
        "type medium = [int, string]",
        "type strict = [int, string]")]
    [DataRow(
        "type testType = [123, 'abc' | 'def']",
        "type loose = array",
        "type medium = [int, 'abc' | 'def']",
        "type strict = [123, 'abc' | 'def']")]
    // Bicep infers a type from literals like "['abc', 'def'] as typed tuples, the user more likely wants "string[]" if all the items are of the same type
    [DataRow(
        "type testType = int[]",
        "type loose = array",
        "type medium = int[]",
        "type strict = int[]")]
    [DataRow(
        "type testType = int[][]",
        "type loose = array",
        "type medium = int[][]",
        "type strict = int[][]")]
    [DataRow(
        "type testType = [ int ]",
        "type loose = array",
        "type medium = int[]",
        "type strict = [ int ]")]
    [DataRow(
        "type testType = [ int, int, int ]",
        "type loose = array",
        "type medium = int[]",
        "type strict = [ int, int, int ]")]
    [DataRow(
        "type testType = [ int?, int?, int? ]",
        "type loose = array",
        "type medium = (int?)[]",
        "type strict = [ int?, int?, int? ]")]
    [DataRow(
        "type testType = [ int?, int, int? ]",
        "type loose = array",
        "type medium = [ int?, int, int? ]",
        "type strict = [ int?, int, int? ]")]
    [DataRow(
        "type testType = [ 'abc'|'def', 'abc'|'def' ]",
        "type loose = array",
        "type medium = ('abc'|'def')[]",
        "type strict = [ 'abc'|'def', 'abc'|'def' ]")]
    [DataRow(
        "type testType = [ 'abc'|'def', 'def'|'ghi' ]",
        "type loose = array",
        "type medium = [ 'abc'|'def', 'def'|'ghi' ]",
        "type strict = [ 'abc'|'def', 'def'|'ghi' ]")]
    public void TupleTypes(string typeDeclaration, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        RunTestFromTypeDeclaration(typeDeclaration, expectedLooseSyntax, expectedMediumStrictSyntax, expectedStrictSyntax);
    }

    [DataTestMethod]
    [DataRow(
        "type testType = string[]",
        "type loose = array",
        "type medium = string[]",
        "type strict = string[]")]
    [DataRow(
        "type testType = (string?)[]",
        "type loose = array",
        "type medium = (string?)[]",
        "type strict = (string?)[]")]
    [DataRow(
        "type testType = 'abc'[]",
        "type loose = array",
        "type medium = string[]",
        "type strict = 'abc'[]")]
    [DataRow(
        "type testType = ('abc'|'def')[]",
        "type loose = array",
        "type medium = ('abc' | 'def')[]",
        "type strict = ('abc' | 'def')[]")]
    public void TypedArrays(string typeDeclaration, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        RunTestFromTypeDeclaration(typeDeclaration, expectedLooseSyntax, expectedMediumStrictSyntax, expectedStrictSyntax);
    }

    [DataTestMethod]
    [DataRow(
        "type testType = array",
        "type loose = array",
        "type medium = array",
        "type strict = array")]
    public void ArrayType(string typeDeclaration, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        RunTestFromTypeDeclaration(typeDeclaration, expectedLooseSyntax, expectedMediumStrictSyntax, expectedStrictSyntax);
    }

    [DataTestMethod]
    [DataRow(
        "type testType = []",
        "type loose = array",
        // Bicep infers an empty array with no items from "[]", the user more likely wants "array"
        "type medium = array",
        "type strict = []")]
    public void EmptyArray(string typeDeclaration, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        RunTestFromTypeDeclaration(typeDeclaration, expectedLooseSyntax, expectedMediumStrictSyntax, expectedStrictSyntax);
    }

    [DataTestMethod]
    [DataRow(
        "type testType = {}",
        "type loose = object",
        "type medium = object",
        "type strict = { }")]
    public void EmptyObject(string typeDeclaration, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        RunTestFromTypeDeclaration(typeDeclaration, expectedLooseSyntax, expectedMediumStrictSyntax, expectedStrictSyntax);
    }

    [DataTestMethod]
    [DataRow(
        "type testType = []",
        "type loose = array",
        "type medium = array",
        "type strict = []")]
    public void EmptyArrays(string typeDeclaration, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        RunTestFromTypeDeclaration(typeDeclaration, expectedLooseSyntax, expectedMediumStrictSyntax, expectedStrictSyntax);
    }

    [DataTestMethod]
    [DataRow(
        "type testType = [testType?]",
        "type loose = array",
        "type medium = (object /* recursive */?)[]", // CONSIDER: question mark before the comment would be better
        "type strict = [object /* recursive */?]")]
    [DataRow(
        "type testType = [string, testType?]",
        "type loose = array",
        "type medium = [string, object /* recursive */?]",
        "type strict = [string, object /* recursive */?]")]
    [DataRow(
        "type testType = [string, testType]?",
        "type loose = array?",
        "type medium = [string, object /* recursive */]?",
        "type strict = [string, object /* recursive */]?")]
    [DataRow(
        "type testType = {t: testType?, a: [testType, testType?]?}",
        "type loose = object",
        "type medium = {a: [object /* recursive */, object /* recursive */?]?, t: object /* recursive */?}",
        "type strict = {a: [object /* recursive */, object /* recursive */?]?, t: object /* recursive */?}")]
    public void RecursiveTypes(string typeDeclaration, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        RunTestFromTypeDeclaration(typeDeclaration, expectedLooseSyntax, expectedMediumStrictSyntax, expectedStrictSyntax);
    }

    [DataTestMethod]
    [DataRow(
        "type testType = string?",
        "type loose = string?",
        "type medium = string?",
        "type strict = string?")]
    [DataRow(
        "type testType = string?",
        "type loose = string?",
        "type medium = string?",
        "type strict = string?")]
    [DataRow(
        "type testType = null|true",
        "type loose = bool?",
        "type medium = bool?",
        "type strict = true?")]
    [DataRow(
        "type testType = null|true|false",
        "type loose = bool?",
        "type medium = false | null | true",
        "type strict = false | null | true")]
    [DataRow(
        "type testType = (null|true)|null",
        "type loose = bool?",
        "type medium = bool?",
        "type strict = true?")]
    [DataRow(
        "type testType = (null|'a')|null|'a'",
        "type loose = string?",
        "type medium = string?",
        "type strict = 'a'?")]
    [DataRow(
        "type testType = (null|'a'|'b')|null|'c'",
        "type loose = string?",
        "type medium = 'a' | 'b' | 'c' | null",
        "type strict = 'a' | 'b' | 'c' | null")]
    [DataRow(
        "type testType = null|(true|false)",
        "type loose = bool?",
        "type medium = false | null | true",
        "type strict = false | null | true")]
    [DataRow(
        "type testType = null|['a', 'b']",
        "type loose = array?",
        "type medium = [string, string]?",
        "type strict = ['a', 'b']?")]
    [DataRow(
        "type testType = null|{a: 'a', b: 1234?}",
        "type loose = object?",
        "type medium = { a: string, b: int? }?",
        "type strict = { a: 'a', b: 1234? }?")]
    [DataRow(
        "type testType = {a: 'a', b: 1234?}?",
        "type loose = object?",
        "type medium = { a: string, b: int? }?",
        "type strict = { a: 'a', b: 1234? }?")]
    [DataRow(
        "type testType = array?",
        "type loose = array?",
        "type medium = array?",
        "type strict = array?")]
    [DataRow(
        "type testType = []?",
        "type loose = array?",
        "type medium = array?",
        "type strict = []?")]
    [DataRow(
        "type testType = object?",
        "type loose = object?",
        "type medium = object?",
        "type strict = object?")]
    [DataRow(
        "type testType = {}?",
        "type loose = object?",
        "type medium = object?",
        "type strict = {}?")]
    public void NullableTypes(string typeDeclaration, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        RunTestFromTypeDeclaration(typeDeclaration, expectedLooseSyntax, expectedMediumStrictSyntax, expectedStrictSyntax);
    }

    [DataTestMethod]
    // "fileUris" property
    [DataRow(
        """
            var _artifactsLocation = 'https://raw.githubusercontent.com/Azure/azure-quickstart-templates/master/101-vm-simple-windows/azuredeploy.json'
            var _artifactsLocationSasToken = '?sas=abcd'
            var commandToExecute = 'powershell -ExecutionPolicy Unrestricted -File writeblob.ps1'

            resource testResource 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' = {
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
            """,
        "fileUris",
        "type loose = array",
        "type medium = string[]",
        "type strict = ['https://raw.githubusercontent.com/Azure/azure-quickstart-templates/master/101-vm-simple-windows/writeblob.ps1?sas=abcd']",
        DisplayName = "virtual machine extensions fileUris property")]
    //
    // "settings" property
    //
    [DataRow(
        """
            var _artifactsLocation = 'https://raw.githubusercontent.com/Azure/azure-quickstart-templates/master/101-vm-simple-windows/azuredeploy.json'
            var _artifactsLocationSasToken = '?sas=abcd'
            var commandToExecute = 'powershell -ExecutionPolicy Unrestricted -File writeblob.ps1'

            resource testResource 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' = {
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
            """,
        "settings",
        "type loose = object",
        "type medium = { commandToExecute: string, fileUris: string[] }",
        "type strict = { commandToExecute: 'powershell -ExecutionPolicy Unrestricted -File writeblob.ps1', fileUris: ['https://raw.githubusercontent.com/Azure/azure-quickstart-templates/master/101-vm-simple-windows/writeblob.ps1?sas=abcd'] }",
        DisplayName = "virtual machine extensions settings property")]
    //
    // "properties" property
    //
    [DataRow(
        """
            var isWindowsOS = true
            var provisionExtensions = true
            param _artifactsLocation string
            @secure()
            param _artifactsLocationSasToken string

            resource testResource 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' = if (isWindowsOS && provisionExtensions) {
              name: 'cse-windows/extension'
              location: 'location'
              properties: {
                publisher: 'Microsoft.Compute'
                type: 'CyustomScriptExtension'
                typeHandlerVersion: '1.8'
                autoUpgradeMinorVersion: true
                settings: {
                  fileUris: [
                    uri(_artifactsLocation, 'writeblob.ps1${_artifactsLocationSasToken}')
                  ]
                  commandToExecute: 'commandToExecute'
                }
              }
            }
            """,
        "properties",
        "type loose = object",
        // asdfg object? /* any */   ??
        // asdfg 'Error' | 'Info' | 'Warning' | null    or     ('Error' | 'Info' | 'Warning')?
        // asdfg so many "?"'s
        """
            type medium = {
              autoUpgradeMinorVersion: bool?
              forceUpdateTag: string?
              instanceView: {
                name: string?
                statuses: {
                  code: string?
                  displayStatus: string?
                  level: 'Error' | 'Info' | 'Warning' | null
                  message: string?
                  time: string?
                }[]?
                substatuses: {
                  code: string?
                  displayStatus: string?
                  level: 'Error' | 'Info' | 'Warning' | null
                  message: string?
                  time: string?
                }[]?
                type: string?
                typeHandlerVersion: string?
              }?
              protectedSettings: object? /* any */
              provisioningState: string
              publisher: string?
              settings: object? /* any */
              type: string?
              typeHandlerVersion: string?
            }
            """,
        """ 
            type strict = {
              autoUpgradeMinorVersion: bool?
              forceUpdateTag: string?
              instanceView: {
                name: string?
                statuses: {
                  code: string?
                  displayStatus: string?
                  level: 'Error' | 'Info' | 'Warning' | null
                  message: string?
                  time: string?
                }[]?
                substatuses: {
                  code: string?
                  displayStatus: string?
                  level: 'Error' | 'Info' | 'Warning' | null
                  message: string?
                  time: string?
                }[]?
                type: string?
                typeHandlerVersion: string?
              }?
              protectedSettings: object? /* any */
              provisioningState: string
              publisher: string?
              settings: object? /* any */
              type: string?
              typeHandlerVersion: string?
            }
            """,
        DisplayName = "virtual machine extensions properties")]
    public void ResourcePropertyTypes(string resourceDeclaration, string resourcePropertyName, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        RunTestFromResourceProperty(resourceDeclaration, resourcePropertyName, expectedLooseSyntax, expectedMediumStrictSyntax, expectedStrictSyntax);
    }

    [DataTestMethod]
    [DataRow(
        """
            type t1 = { abc: int }
            type testType = t1
            """,
        "type loose = object",
        "type medium = { abc: int }", // TODO: better would be "type medium = t1" but Bicep type system doesn't currently support it
        "type strict = { abc: int }" // TODO: better would be "type strict = t1" but Bicep type system doesn't currently support it
        )]
    [DataRow(
        """
            type t1 = {
                a: string
                b: string
            }
            type t2 = t1[]
            type t3 = {
                t1Property: t1
                t2Property: t2
            }
            type testType = [t3]
            """,
        "type loose = array",
        "type medium = { t1Property: { a: string, b: string }, t2Property: { a: string, b: string }[] }[]", // TODO: better would be "type medium = t3[]" but Bicep type system doesn't currently support it
        "type strict = [ { t1Property: { a: string, b: string }, t2Property: { a: string, b: string }[] } ]"  // TODO: better would be "type strict = [ t3 ]" but Bicep type system doesn't currently support it
        )]
    [DataRow(
        """
            type t1 = { a: 'abc', b: 123 }
            type testType = { a: t1, b: [t1, t1] }
            """,
        "type loose = object", // TODO: better: "{ a: t1, b: [t1, t1] }"
        "type medium = { a: { a: string, b: int }, b: { a: string, b: int }[] }", // TODO: better: "{ a: t1, b: [t1, t1] }"
        "type strict = { a: { a: 'abc', b: 123 }, b: [{ a: 'abc', b: 123 }, { a: 'abc', b: 123 }] }")]
    public void NamedTypes(string typeDeclaration, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        RunTestFromTypeDeclaration(typeDeclaration, expectedLooseSyntax, expectedMediumStrictSyntax, expectedStrictSyntax);
    }

    #region Support

    // input is a type declaration statement for type "testType", e.g. "type testType = int"
    private static void RunTestFromTypeDeclaration(string typeDeclaration, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        var compilationResult = CompilationHelper.Compile(typeDeclaration);
        var semanticModel = compilationResult.Compilation.GetEntrypointSemanticModel();
        var declarationSyntax = semanticModel.Root.TypeDeclarations[0].DeclaringSyntax;
        var declaredType = semanticModel.GetDeclaredType(semanticModel.Root.TypeDeclarations.Single(t => t.Name == "testType").Value);
        declaredType.Should().NotBeNull();

        RunTestHelper(null, declaredType!, semanticModel, expectedLooseSyntax, expectedMediumStrictSyntax, expectedStrictSyntax);
    }

    // input is a resource declaration for resource "testResource" and a property name such as "properties" that is exposed anywhere on the resource
    private static void RunTestFromResourceProperty(string resourceDeclaration, string resourcePropertyName, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        var compilationResult = CompilationHelper.Compile(resourceDeclaration);
        var semanticModel = compilationResult.Compilation.GetEntrypointSemanticModel();
        var resourceSyntax = semanticModel.Root.ResourceDeclarations.Single(r => r.Name == "testResource").DeclaringResource;

        var properties = GetAllSyntaxOfType<ObjectPropertySyntax>(resourceSyntax);
        var matchingProperty = properties.Single(p => p.Key is IdentifierSyntax id && id.NameEquals(resourcePropertyName));

        var inferredType = semanticModel.GetTypeInfo(matchingProperty.Value);
        var declaredType = semanticModel.GetDeclaredType(matchingProperty);
        var matchingPropertyType = declaredType is AnyType || declaredType == null ? inferredType : declaredType;
        matchingPropertyType.Should().NotBeNull();

        RunTestHelper(null, matchingPropertyType!, semanticModel, expectedLooseSyntax, expectedMediumStrictSyntax, expectedStrictSyntax);
    }

    private static void RunTestHelper(TypeProperty? typeProperty, TypeSymbol typeSymbol, SemanticModel semanticModel, string expectedLooseSyntax, string expectedMediumStrictSyntax, string expectedStrictSyntax)
    {
        if (debugPrintAllSyntaxNodeTypes)
        {
            DebugPrintAllSyntaxNodeTypes(semanticModel);
        }

        var looseSyntax = StringizeType.Stringize(typeSymbol, typeProperty, StringizeType.Strictness.Loose);
        var mediumStrictSyntax = StringizeType.Stringize(typeSymbol, typeProperty, StringizeType.Strictness.Medium);
        var strictSyntax = StringizeType.Stringize(typeSymbol, typeProperty, StringizeType.Strictness.Strict);

        using (new AssertionScope())
        {
            CompilationHelper.Compile(expectedLooseSyntax).Diagnostics.Should().NotHaveAnyDiagnostics("expected loose syntax should be error-free");
            CompilationHelper.Compile(expectedMediumStrictSyntax).Diagnostics.Should().NotHaveAnyDiagnostics("expected medium strictness syntax should be error-free");
            CompilationHelper.Compile(expectedStrictSyntax).Diagnostics.Should().NotHaveAnyDiagnostics("expected strict syntax should be error-free");
        }

        using (new AssertionScope())
        {
            var actualLooseSyntaxType = $"type loose = {looseSyntax}";
            actualLooseSyntaxType.Should().EqualIgnoringBicepFormatting(expectedLooseSyntax);

            string actualMediumLooseSyntaxType = $"type medium = {mediumStrictSyntax}";
            actualMediumLooseSyntaxType.Should().EqualIgnoringBicepFormatting(expectedMediumStrictSyntax);

            string actualStrictSyntaxType = $"type strict = {strictSyntax}";
            actualStrictSyntaxType.Should().EqualIgnoringBicepFormatting(expectedStrictSyntax);
        }

        // asdfg verify resulting types compile
    }

    private static IEnumerable<TSyntax> GetAllSyntaxOfType<TSyntax>(SyntaxBase syntax) where TSyntax : SyntaxBase
        => SyntaxVisitor.GetAllSyntaxOfType<TSyntax>(syntax);

    private class SyntaxVisitor : CstVisitor
    {
        private readonly List<SyntaxBase> syntaxList = new();

        private SyntaxVisitor()
        {
        }

        public static IEnumerable<TSyntax> GetAllSyntaxOfType<TSyntax>(SyntaxBase syntax) where TSyntax : SyntaxBase
        {
            var visitor = new SyntaxVisitor();
            visitor.Visit(syntax);

            return visitor.syntaxList.OfType<TSyntax>();
        }

        protected override void VisitInternal(SyntaxBase syntax)
        {
            syntaxList.Add(syntax);
            base.VisitInternal(syntax);
        }

    }

    private static void DebugPrintAllSyntaxNodeTypes(SemanticModel semanticModel)
    {
        var allSyntaxNodes = GetAllSyntaxNodesVisitor.Build(semanticModel.Root.Syntax);
        foreach (var node in allSyntaxNodes.Where(s => s is not Token && s is not IdentifierSyntax))
        {
            Trace.WriteLine($"** {node.GetDebuggerDisplay().ReplaceNewlines(" ").TruncateWithEllipses(150)}");
            Trace.WriteLine($"  ... type info: {semanticModel.GetTypeInfo(node).Name}");
            Trace.WriteLine($"  ... declared type: {semanticModel.GetDeclaredType(node)?.Name}");
        }
    }

    private class GetAllSyntaxNodesVisitor : CstVisitor
    {
        private readonly List<SyntaxBase> syntaxList = new();

        public static ImmutableArray<SyntaxBase> Build(SyntaxBase syntax)
        {
            var visitor = new GetAllSyntaxNodesVisitor();
            visitor.Visit(syntax);

            return [..visitor.syntaxList];
        }

        protected override void VisitInternal(SyntaxBase syntax)
        {
            syntaxList.Add(syntax);
            base.VisitInternal(syntax);
        }
    }

    #endregion Support
}

