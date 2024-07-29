// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Bicep.Core.CodeAction;
using Bicep.Core.Diagnostics;
using Bicep.Core.Extensions;
using Bicep.Core.Parsing;
using Bicep.Core.Samples;
using Bicep.Core.Syntax;
using Bicep.Core.Text;
using Bicep.Core.UnitTests;
using Bicep.Core.UnitTests.Assertions;
using Bicep.Core.UnitTests.PrettyPrintV2;
using Bicep.Core.UnitTests.Serialization;
using Bicep.Core.UnitTests.Utils;
using Bicep.Core.Workspaces;
using Bicep.LangServer.IntegrationTests.Helpers;
using Bicep.LanguageServer.Extensions;
using Bicep.LanguageServer.Utils;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Bicep.LangServer.IntegrationTests
{
    // TODO:             //asdfg resource/user-defined types


    [TestClass]
    public class ExtractToVariableTests : CodeActionTestBase
    {
        private const string ExtractToVariableTitle = "Introduce variable";

        [DataTestMethod]
        [DataRow("""
            var a = '|b'
            """,
            """
            var newVar = 'b'
            var a = newVar
            """)]
        [DataRow("""
            var a = 'a'
            var b = '|b'
            var c = 'c'
            """,
            """
            var a = 'a'
            var newVar = 'b'
            var b = newVar
            var c = 'c'
            """)]
        [DataRow("""
            var a = 1 + |2
            """,
            """
            var newVar = 2
            var a = 1 + newVar
            """)]
        [DataRow("""
            var a = <<1 + 2>>
            """,
            """
            var newVar = 1 + 2
            var a = newVar
            """)]
        [DataRow("""
            var a = <<1 +>> 2
            """,
            """
            var newVar = 1 + 2
            var a = newVar
            """)]
        [DataRow("""
            var a = 1 |+ 2
            """,
            """
            var newVar = 1 + 2
            var a = newVar
            """)]
        [DataRow("""
            var a = 1 <<+ 2 + 3 >>+ 4
            """,
            """
            var newVar = 1 + 2 + 3 + 4
            var a = newVar
            """)]
        //asdfg issue: should we expand selection?
        [DataRow("""
            param p1 int = 1 + |2
            """,
            """
            var newVar = 2
            param p1 int = 1 + newVar
            """)]
        [DataRow("""
            var a = 1 + 2
            var b = '${a}|{a}'
            """,
            """
            var a = 1 + 2
            var newVar = '${a}{a}'
            var b = newVar
            """,
            DisplayName = "Full interpolated string")]
        [DataRow("""
            // comment 1
            @secure
            // comment 2
            param a = '|a'
            """,
            """
            // comment 1
            var newVar = 'a'
            @secure
            // comment 2
            param a = newVar
            """,
            DisplayName = "Preceding lines")]
        [DataRow("""
            var a = 1
            var b = [
                'a'
                1 + <<2>>
                'c'
            ]
            """,
            """
            var a = 1
            var newVar = 2
            var b = [
                'a'
                1 + newVar
                'c'
            ]
            """,
            DisplayName = "Inside a data structure")]
        [DataRow("""
            // My comment here
            resource storageaccount 'Microsoft.Storage/storageAccounts@2021-02-01' = {
                name: 'name'
                location: |'westus'
                kind: 'StorageV2'
                sku: {
                name: 'Premium_LRS'
                }
            }
            """,
            """
            // My comment here
            var location = 'westus'
            resource storageaccount 'Microsoft.Storage/storageAccounts@2021-02-01' = {
                name: 'name'
                location: location
                kind: 'StorageV2'
                sku: {
                name: 'Premium_LRS'
                }
            }
            """)]
        public async Task Basics(string fileWithCursors, string expectedText)
        {
            await RunExtractToVariableTest(fileWithCursors, expectedText);
        }

        [DataTestMethod]
        [DataRow("""
            var newVar = 'newVar'
            param newVar2 string = '|newVar2'
            """,
            """
            var newVar = 'newVar'
            var newVar3 = 'newVar2'
            param newVar2 string = newVar3
            """,
            DisplayName = "Simple naming conflict")
        ]
        [DataRow("""
            var id = [1, 2, 3]
            param id2 string = 'hello'
            resource id6 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = [
                for (id3, id4) in id: {
                name: 'subnet${id3}'
                properties: {
                    addressPrefix: '10.0.${id4}.0/24'
                    natGateway: {
                    id: '|gatewayId'
                    }
                }
                }
            ]
            output id5 string = id2
            """,
            """
            var id = [1, 2, 3]
            param id2 string = 'hello'
            var id7 = 'gatewayId'
            resource id6 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = [
                for (id3, id4) in id: {
                name: 'subnet${id3}'
                properties: {
                    addressPrefix: '10.0.${id4}.0/24'
                    natGateway: {
                    id: id7
                    }
                }
                }
            ]
            output id5 string = id2
            """,
            DisplayName = "Complex naming conflicts")]
        public async Task ShouldRenameToAvoidConflicts(string fileWithCursors, string expectedText)
        {
            await RunExtractToVariableTest(fileWithCursors, expectedText);
        }

        [TestMethod]
        public async Task ShouldHandleArrays()
        {
            await RunExtractToVariableTest("""
            resource subnets 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = [
              for (item, index) in <<[1, 2, 3]>>: {
                name: 'subnet${index}'
                properties: {
                  addressPrefix: '10.0.${index}.0/24'
                }
              }
            ]
            """,
            """
            var newVar = [1, 2, 3]
            resource subnets 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = [
              for (item, index) in newVar: {
                name: 'subnet${index}'
                properties: {
                  addressPrefix: '10.0.${index}.0/24'
                }
              }
            ]
            """);
        }

        [TestMethod]
        public async Task ShouldHandleObjects()
        {
            await RunExtractToVariableTest("""
                resource resourceWithProperties 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' = if (isWindowsOS && provisionExtensions) {
                  parent: vmName_resource
                  name: 'cse-windows'
                  location: location
                  properties: <<{
                    // Entire properties object selected
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
                  }>>
                }                
                """,
                """
                var properties = {
                  // Entire properties object selected
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
                resource resourceWithProperties 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' = if (isWindowsOS && provisionExtensions) {
                  parent: vmName_resource
                  name: 'cse-windows'
                  location: location
                  properties: properties
                }                
                """);
        }

        [DataTestMethod]
        [DataRow("""
            resource subnets 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = [
                for (item, index) in [1, 2, 3]: {
                name: 'subnet${index}'
                properties: {
                    addressPrefix: '10.|0.${|index}.0/24'
                }
                }
            ]
            """,
            """
            var addressPrefix = '10.0.${index}.0/24'
            resource subnets 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = [
                for (item, index) in [1, 2, 3]: {
                name: 'subnet${index}'
                properties: {
                    addressPrefix: addressPrefix
                }
                }
            ]
            """,
            DisplayName = "Extracting expression with local variable reference - asdfg allowed?")]
        [DataRow("""
            resource vmName_cse_windows 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' = if (isWindowsOS && provisionExtensions) {
                parent: vmName_resource
                name: 'cse-windows'
                location: location
                properties: {
                publisher: 'Microsoft.Compute'
                type: 'CustomScriptExtension'
                typeHandlerVersion: '1.8'
                autoUpgradeMinorVersion: true
                settings: {
                    fileUris: [
                    uri(_artifactsLocation, 'writeblob.ps1${_artifactsLocationSasToken}')
                    ]
                    comma|ndToExecute: commandToExecute
                }
                }
            }
            """,
            """asdfg?""",
            DisplayName = "asdfg: probably nothing by default?")]
        public async Task asdfg_BadSelections_asdfgwhatbehavior(string fileWithCursors, string expectedText)
        {
            await RunExtractToVariableTest(fileWithCursors, expectedText);
        }

        [DataTestMethod]
        [DataRow("""
            param p1 int = 1 + /*comments1*/|2/*comments2*/
            """,
            """
            var newVar = /*comments1*/2/*comments2*/
            param p1 int = 1 + newVar
            """,
            DisplayName = "asdfg bug: Expression with comments")]
        public async Task ExpressionsWithComments(string fileWithCursors, string expectedText)
        {
            await RunExtractToVariableTest(fileWithCursors, expectedText);
        }

        [DataTestMethod]
        [DataRow("""
            resource subnets 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = [
                for (item, index) in [1, 2, 3]: {
                name: '<<subnet${index}'
                properties: {
                    addressPrefix: '10.>>0.${index}.0/24'
                }
                }
            ]
            """,
            DisplayName = "asdfg_InvalidSelections: cursor contains multiple unrelated lines")]
        [DataRow("""
            resource vmName_resource 'Microsoft.Compute/virtualMachines@2019-12-01' = {
              name: vmName
              location: location
              properties: {
                osProfile: {
                 | computerName: vmName
                }
              }
            }
            """)]
        public async Task InvalidSelections(string fileWithCursors)
        {
            // Expect no code actions offered
            await RunExtractToVariableTest(fileWithCursors, null);
        }

        [TestMethod]
        public async Task IfJustPropertyNameSelected_ThenExtractPropertyValue()
        {
            await RunExtractToVariableTest("""
                resource resourceWithProperties 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' = if (isWindowsOS && provisionExtensions) {
                  parent: vmName_resource
                  name: 'cse-windows'
                  location: location
                  properties: {
                    publisher: 'Microsoft.Compute'
                    type: 'CustomScriptExtension'
                    typeHandlerVersion: '1.8'
                    autoUpgradeMinorVersion: true
                    setting|s: { // Property key selected - extract just the value
                      fileUris: [
                        uri(_artifactsLocation, 'writeblob.ps1${_artifactsLocationSasToken}')
                      ]
                      commandToExecute: commandToExecute
                    }
                  }
                }                
                """,
                """
                var settings = {
                  // Property key selected - extract just the value
                  fileUris: [
                    uri(_artifactsLocation, 'writeblob.ps1${_artifactsLocationSasToken}')
                  ]
                  commandToExecute: commandToExecute
                }
                resource resourceWithProperties 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' = if (isWindowsOS && provisionExtensions) {
                  parent: vmName_resource
                  name: 'cse-windows'
                  location: location
                  properties: {
                    publisher: 'Microsoft.Compute'
                    type: 'CustomScriptExtension'
                    typeHandlerVersion: '1.8'
                    autoUpgradeMinorVersion: true
                    settings: settings
                  }
                }                
                """);
        }

        [DataTestMethod]
        [DataRow("""
            resource vmName_resource 'Microsoft.Compute/virtualMachines@2019-12-01' = {
              name: vmName
              location: location
              properties: {
                osProfile: {
                 | computerName: vmName
                }
              }
            }
            """,
            null,
            DisplayName = "Empty selection in object")]
        [DataRow("""
            resource vmName_resource 'Microsoft.Compute/virtualMachines@2019-12-01' = {
              name: vmName
              location: location
              properties: <<{
                osProfile: {
                 computerName: vmName
                }
              }>>
            }
            """,
            """
            var properties = {
              osProfile: {
                computerName: vmName
              }
            }
            resource vmName_resource 'Microsoft.Compute/virtualMachines@2019-12-01' = {
              name: vmName
              location: location
              properties: properties
            }
            """,
            DisplayName = "Full object selected")]
        [DataRow("""
            resource vmName_resource 'Microsoft.Compute/virtualMachines@2019-12-01' = {
              name: vmName
              location: location
              properties: { <<
                osProfile: {
                 computerName: vmName
                }
              }>>
            }
            """,
            """
            var properties = {
                osProfile: {
                 computerName: vmName
                }
              }
            resource vmName_resource 'Microsoft.Compute/virtualMachines@2019-12-01' = {
              name: vmName
              location: location
              properties: properties
            }
            """,
            DisplayName = "Partial object selected")]
        public async Task OnlyPickUpObjectsAndArraysIfNonEmptySelection(string fileWithCursors, string? expectedText)
        {
            // Expect no code actions offered
            await RunExtractToVariableTest(fileWithCursors, expectedText);
        }

        [DataTestMethod]
        [DataRow("""
            resource vmName_resource 'Microsoft.Compute/virtualMachines@2019-12-01' = {
              name: vmName
              location: location
              properties: {
                osProfile: {
                  computerName: vmName
                  myproperty: {
                    abc: [
                      {
                        def: [
                          'ghi'
                          '|jkl'
                        ]
                      }
                    ]
                  }
                }
              }
            }            
            """,
            """
            var newVar = 'jkl'
            resource vmName_resource 'Microsoft.Compute/virtualMachines@2019-12-01' = {
              name: vmName
              location: location
              properties: {
                osProfile: {
                  computerName: vmName
                  myproperty: {
                    abc: [
                      {
                        def: [
                          'ghi'
                          newVar
                        ]
                      }
                    ]
                  }
                }
              }
            }            
            """,
            DisplayName = "Array element, don't pick up property name")]
        [DataRow("""
            resource vmName_resource 'Microsoft.Compute/virtualMachines@2019-12-01' = {
              name: vmName
              location: location
              properties: {
                osProfile: {
                  computerName: vmName
                  myproperty: {
                    abc: <<[
                      {
                        def: [
                          'ghi'
                          'jkl'
                        ]
                      }
                    ]>>
                  }
                }
              }
            }            
            """,
            """
            var abc = [
              {
                def: [
                  'ghi'
                  'jkl'
                ]
              }
            ]
            resource vmName_resource 'Microsoft.Compute/virtualMachines@2019-12-01' = {
              name: vmName
              location: location
              properties: {
                osProfile: {
                  computerName: vmName
                  myproperty: {
                    abc: abc
                  }
                }
              }
            }            
            """,
            DisplayName = "Full property value as array, pick up property name")]      
        public async Task PickUpPropertyName_ButOnlyIfFullPropertyValue(string fileWithCursors, string expectedText)
        {
            await RunExtractToVariableTest(fileWithCursors, expectedText);
        }

        private async Task RunExtractToVariableTest(string fileWithCursors, string? expectedText)
        {
            (var codeActions, var bicepFile) = await RunSyntaxTest(fileWithCursors, '|');
            var extract = codeActions.FirstOrDefault(x => x.Title.StartsWith(ExtractToVariableTitle));

            if (expectedText == null)
            {
                extract.Should().BeNull("should not offer any variable extractions");
            }
            else
            {
                extract.Should().NotBeNull("should contain an action to extract to variable");
                extract!.Kind.Should().Be(CodeActionKind.RefactorExtract);

                var updatedFile = ApplyCodeAction(bicepFile, extract);
                updatedFile.Should().HaveSourceText(expectedText);
            }
        }
    }
}
