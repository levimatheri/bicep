// DECISION: settings3: object? /* actual type: any? */
// Unsupported union: widen to string? (comment)

// [Jonny]
// Extracted value is in a var statement and has no declared type: the type will be based on the value. You might get recursive types or unions if the value contains a reference to a parameter, but you can pull the type clause from the parameter declaration.
var blah1 = [{ foo: 'bar' }, { foo: 'baz' }]

// Extracted value is in a param statement (or something else with an explicit type declaration): you may be able to use the declared type syntax of the enclosing statement rather than working from the type backwards to a declaration.
param p1 { intVal: int }
param p2 object = p1
param newParameter {} = p2
var v1 = newParameter

param newParameter2 [{ foo: string }, { foo: string }] = [{ foo: 'bar' }, { foo: 'baz' }]
var blah = newParameter2

// Extracted value is in a resource body: definite possibility of complex structures, recursion, and a few type constructs that aren't fully expressible in Bicep syntax (e.g., "open" enums like 'foo' | 'bar' | string). Resource-derived types might be a good solution here, but they're still behind a feature flag

resource vmext 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' = {
  name: 'vmext/extension'
  location: 'location'
  properties: {
    publisher: 'Microsoft.Compute'
    type: 'CustomScriptExtension'
    typeHandlerVersion: '1.8'
    autoUpgradeMinorVersion: true
    settings: {
      fileUris: [
        uri(_artifactsLocation, 'writeblob.ps1${_artifactsLocationSasToken}') // <<<<<<<<<<<<<<<<<<<<<<<<<<<< ISSUE - expecting string[]
      ]
      commandToExecute: 'commandToExecute'
    }
  }
}

// [Anthony]
// If pulling from a var - do we have any existing logic or heuristic to do this? There may be different expectations - for example, it wouldn't be particularly useful to convert:
//
// var foo = { intVal: 2 }
// to:
// param foo { intVal: 2}

// more likely the user would instead expect:
// param foo { intVal: int }

var foo = { intVal: 2 }

// var blah = [{foo: 'bar'}, {foo: 'baz'}]
// I would expect the user to more likely want:
// param blah {foo: string }[]
// rather than:
// param blah {foo: 'bar' | 'baz'}[]
// or:
// param blah ({foo: 'bar'}|{foo: 'baz'})[]

var blah = [{ foo: 'bar' }, { foo: 'baz' }]

// ISSUE - "any"

var isWindowsOS = true
var provisionExtensions = true
param _artifactsLocation string
@secure()
param _artifactsLocationSasToken string

param properties {
  autoUpgradeMinorVersion: bool?
  forceUpdateTag: string?
  instanceView: {
    name: string?
    statuses: {
      code: string?
      displayStatus: string?
      level: 'Error' | 'Info' | 'Warning'?
      message: string?
      time: string?
    }[]?
    substatuses: {
      code: string?
      displayStatus: string?
      level: 'Error' | 'Info' | 'Warning'?
      message: string?
      time: string?
    }[]?
    type: string?
    typeHandlerVersion: string?
  }?
  protectedSettings: any?
  provisioningState: string
  publisher: string?
  settings: any?
  type: string?
  typeHandlerVersion: string?
}? = {
  // <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<< EXTRACT PROPERTIES
  publisher: 'Microsoft.Compute'
  type: 'CustomScriptExtension'
  typeHandlerVersion: '1.8'
  autoUpgradeMinorVersion: true
  settings: {
    fileUris: [
      uri(_artifactsLocation, 'writeblob.ps1${_artifactsLocationSasToken}')
    ]
    commandToExecute: 'commandToExecute'
  }
}
resource vmextAny 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' = if (isWindowsOS && provisionExtensions) {
  name: 'vmextAny/extension'
  location: 'location'
  properties: properties
}

// ISSUE - unsuppored unions

param properties2 {
  additionalCapabilities: { ultraSSDEnabled: bool? }?
  availabilitySet: { id: string? }?
  billingProfile: { maxPrice: int? }?
  diagnosticsProfile: { bootDiagnostics: { enabled: bool?, storageUri: string? }? }?
  evictionPolicy: 'Deallocate' | 'Delete' | string?
  hardwareProfile: {
    vmSize:
      | 'Basic_A0'
      | 'Basic_A1'
      | 'Basic_A2'
      | 'Basic_A3'
      | 'Basic_A4'
      | 'Standard_A0'
      | 'Standard_A1'
      | 'Standard_A10'
      | 'Standard_A11'
      | 'Standard_A1_v2'
      | 'Standard_A2'
      | 'Standard_A2_v2'
      | 'Standard_A2m_v2'
      | 'Standard_A3'
      | 'Standard_A4'
      | 'Standard_A4_v2'
      | 'Standard_A4m_v2'
      | 'Standard_A5'
      | 'Standard_A6'
      | 'Standard_A7'
      | 'Standard_A8'
      | 'Standard_A8_v2'
      | 'Standard_A8m_v2'
      | 'Standard_A9'
      | 'Standard_B1ms'
      | 'Standard_B1s'
      | 'Standard_B2ms'
      | 'Standard_B2s'
      | 'Standard_B4ms'
      | 'Standard_B8ms'
      | 'Standard_D1'
      | 'Standard_D11'
      | 'Standard_D11_v2'
      | 'Standard_D12'
      | 'Standard_D12_v2'
      | 'Standard_D13'
      | 'Standard_D13_v2'
      | 'Standard_D14'
      | 'Standard_D14_v2'
      | 'Standard_D15_v2'
      | 'Standard_D16_v3'
      | 'Standard_D16s_v3'
      | 'Standard_D1_v2'
      | 'Standard_D2'
      | 'Standard_D2_v2'
      | 'Standard_D2_v3'
      | 'Standard_D2s_v3'
      | 'Standard_D3'
      | 'Standard_D32_v3'
      | 'Standard_D32s_v3'
      | 'Standard_D3_v2'
      | 'Standard_D4'
      | 'Standard_D4_v2'
      | 'Standard_D4_v3'
      | 'Standard_D4s_v3'
      | 'Standard_D5_v2'
      | 'Standard_D64_v3'
      | 'Standard_D64s_v3'
      | 'Standard_D8_v3'
      | 'Standard_D8s_v3'
      | 'Standard_DS1'
      | 'Standard_DS11'
      | 'Standard_DS11_v2'
      | 'Standard_DS12'
      | 'Standard_DS12_v2'
      | 'Standard_DS13'
      | 'Standard_DS13-2_v2'
      | 'Standard_DS13-4_v2'
      | 'Standard_DS13_v2'
      | 'Standard_DS14'
      | 'Standard_DS14-4_v2'
      | 'Standard_DS14-8_v2'
      | 'Standard_DS14_v2'
      | 'Standard_DS15_v2'
      | 'Standard_DS1_v2'
      | 'Standard_DS2'
      | 'Standard_DS2_v2'
      | 'Standard_DS3'
      | 'Standard_DS3_v2'
      | 'Standard_DS4'
      | 'Standard_DS4_v2'
      | 'Standard_DS5_v2'
      | 'Standard_E16_v3'
      | 'Standard_E16s_v3'
      | 'Standard_E2_v3'
      | 'Standard_E2s_v3'
      | 'Standard_E32-16_v3'
      | 'Standard_E32-8s_v3'
      | 'Standard_E32_v3'
      | 'Standard_E32s_v3'
      | 'Standard_E4_v3'
      | 'Standard_E4s_v3'
      | 'Standard_E64-16s_v3'
      | 'Standard_E64-32s_v3'
      | 'Standard_E64_v3'
      | 'Standard_E64s_v3'
      | 'Standard_E8_v3'
      | 'Standard_E8s_v3'
      | 'Standard_F1'
      | 'Standard_F16'
      | 'Standard_F16s'
      | 'Standard_F16s_v2'
      | 'Standard_F1s'
      | 'Standard_F2'
      | 'Standard_F2s'
      | 'Standard_F2s_v2'
      | 'Standard_F32s_v2'
      | 'Standard_F4'
      | 'Standard_F4s'
      | 'Standard_F4s_v2'
      | 'Standard_F64s_v2'
      | 'Standard_F72s_v2'
      | 'Standard_F8'
      | 'Standard_F8s'
      | 'Standard_F8s_v2'
      | 'Standard_G1'
      | 'Standard_G2'
      | 'Standard_G3'
      | 'Standard_G4'
      | 'Standard_G5'
      | 'Standard_GS1'
      | 'Standard_GS2'
      | 'Standard_GS3'
      | 'Standard_GS4'
      | 'Standard_GS4-4'
      | 'Standard_GS4-8'
      | 'Standard_GS5'
      | 'Standard_GS5-16'
      | 'Standard_GS5-8'
      | 'Standard_H16'
      | 'Standard_H16m'
      | 'Standard_H16mr'
      | 'Standard_H16r'
      | 'Standard_H8'
      | 'Standard_H8m'
      | 'Standard_L16s'
      | 'Standard_L32s'
      | 'Standard_L4s'
      | 'Standard_L8s'
      | 'Standard_M128-32ms'
      | 'Standard_M128-64ms'
      | 'Standard_M128ms'
      | 'Standard_M128s'
      | 'Standard_M64-16ms'
      | 'Standard_M64-32ms'
      | 'Standard_M64ms'
      | 'Standard_M64s'
      | 'Standard_NC12'
      | 'Standard_NC12s_v2'
      | 'Standard_NC12s_v3'
      | 'Standard_NC24'
      | 'Standard_NC24r'
      | 'Standard_NC24rs_v2'
      | 'Standard_NC24rs_v3'
      | 'Standard_NC24s_v2'
      | 'Standard_NC24s_v3'
      | 'Standard_NC6'
      | 'Standard_NC6s_v2'
      | 'Standard_NC6s_v3'
      | 'Standard_ND12s'
      | 'Standard_ND24rs'
      | 'Standard_ND24s'
      | 'Standard_ND6s'
      | 'Standard_NV12'
      | 'Standard_NV24'
      | 'Standard_NV6'
      | string?
  }?
  host: { id: string? }?
  instanceView: {
    bootDiagnostics: {
      consoleScreenshotBlobUri: string
      serialConsoleLogBlobUri: string
      status: {
        code: string?
        displayStatus: string?
        level: 'Error' | 'Info' | 'Warning'?
        message: string?
        time: string?
      }
    }?
    computerName: string?
    disks: {
      encryptionSettings: {
        diskEncryptionKey: { secretUrl: string, sourceVault: { id: string? } }?
        enabled: bool?
        keyEncryptionKey: { keyUrl: string, sourceVault: { id: string? } }?
      }[]?
      name: string?
      statuses: {
        code: string?
        displayStatus: string?
        level: 'Error' | 'Info' | 'Warning'?
        message: string?
        time: string?
      }[]?
    }[]?
    extensions: {
      name: string?
      statuses: {
        code: string?
        displayStatus: string?
        level: 'Error' | 'Info' | 'Warning'?
        message: string?
        time: string?
      }[]?
      substatuses: {
        code: string?
        displayStatus: string?
        level: 'Error' | 'Info' | 'Warning'?
        message: string?
        time: string?
      }[]?
      type: string?
      typeHandlerVersion: string?
    }[]?
    hyperVGeneration: 'V1' | 'V2' | string?
    maintenanceRedeployStatus: {
      isCustomerInitiatedMaintenanceAllowed: bool?
      lastOperationMessage: string?
      lastOperationResultCode: 'MaintenanceAborted' | 'MaintenanceCompleted' | 'None' | 'RetryLater'?
      maintenanceWindowEndTime: string?
      maintenanceWindowStartTime: string?
      preMaintenanceWindowEndTime: string?
      preMaintenanceWindowStartTime: string?
    }?
    osName: string?
    osVersion: string?
    platformFaultDomain: int?
    platformUpdateDomain: int?
    rdpThumbPrint: string?
    statuses: {
      code: string?
      displayStatus: string?
      level: 'Error' | 'Info' | 'Warning'?
      message: string?
      time: string?
    }[]?
    vmAgent: {
      extensionHandlers: {
        status: {
          code: string?
          displayStatus: string?
          level: 'Error' | 'Info' | 'Warning'?
          message: string?
          time: string?
        }?
        type: string?
        typeHandlerVersion: string?
      }[]?
      statuses: {
        code: string?
        displayStatus: string?
        level: 'Error' | 'Info' | 'Warning'?
        message: string?
        time: string?
      }[]?
      vmAgentVersion: string?
    }?
  }
  licenseType: string?
  networkProfile: { networkInterfaces: { id: string?, properties: { primary: bool? }? }[]? }?
  osProfile: {
    adminPassword: string?
    adminUsername: string?
    allowExtensionOperations: bool?
    computerName: string?
    customData: string?
    linuxConfiguration: {
      disablePasswordAuthentication: bool?
      provisionVMAgent: bool?
      ssh: { publicKeys: { keyData: string?, path: string? }[]? }?
    }?
    requireGuestProvisionSignal: bool?
    secrets: {
      sourceVault: { id: string? }?
      vaultCertificates: { certificateStore: string?, certificateUrl: string? }[]?
    }[]?
    windowsConfiguration: {
      additionalUnattendContent: {
        componentName: string?
        content: string?
        passName: string?
        settingName: 'AutoLogon' | 'FirstLogonCommands'?
      }[]?
      enableAutomaticUpdates: bool?
      provisionVMAgent: bool?
      timeZone: string?
      winRM: { listeners: { certificateUrl: string?, protocol: 'Http' | 'Https'? }[]? }?
    }?
  }?
  priority: 'Low' | 'Regular' | 'Spot' | string?
  provisioningState: string
  proximityPlacementGroup: { id: string? }?
  storageProfile: {
    dataDisks: {
      caching: 'None' | 'ReadOnly' | 'ReadWrite'?
      createOption: 'Attach' | 'Empty' | 'FromImage' | string
      diskIOPSReadWrite: int
      diskMBpsReadWrite: int
      diskSizeGB: int?
      image: { uri: string? }?
      lun: int
      managedDisk: {
        diskEncryptionSet: { id: string? }?
        id: string?
        storageAccountType: 'Premium_LRS' | 'StandardSSD_LRS' | 'Standard_LRS' | 'UltraSSD_LRS' | string?
      }?
      name: string?
      toBeDetached: bool?
      vhd: { uri: string? }?
      writeAcceleratorEnabled: bool?
    }[]?
    imageReference: {
      exactVersion: string
      id: string?
      offer: string?
      publisher: string?
      sku: string?
      version: string?
    }?
    osDisk: {
      caching: 'None' | 'ReadOnly' | 'ReadWrite'?
      createOption: 'Attach' | 'Empty' | 'FromImage' | string
      diffDiskSettings: { option: 'Local' | string?, placement: 'CacheDisk' | 'ResourceDisk' | string? }?
      diskSizeGB: int?
      encryptionSettings: {
        diskEncryptionKey: { secretUrl: string, sourceVault: { id: string? } }?
        enabled: bool?
        keyEncryptionKey: { keyUrl: string, sourceVault: { id: string? } }?
      }?
      image: { uri: string? }?
      managedDisk: {
        diskEncryptionSet: { id: string? }?
        id: string?
        storageAccountType: 'Premium_LRS' | 'StandardSSD_LRS' | 'Standard_LRS' | 'UltraSSD_LRS' | string?
      }?
      name: string?
      osType: 'Linux' | 'Windows'?
      vhd: { uri: string? }?
      writeAcceleratorEnabled: bool?
    }?
  }?
  virtualMachineScaleSet: { id: string? }?
  vmId: string
}? = {
  // <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<< EXTRACT PROPERTIES
  diagnosticsProfile: {
    bootDiagnostics: {
      storageUri: reference(existingStorageAccount.id, '2018-02-01').primaryEndpoints.blob
    }
  }
}
resource vmUnsupportedUnion 'Microsoft.Compute/virtualMachines@2019-12-01' = {
  name: 'vmUnsupportedUnion'
  location: 'eastus'
  properties: properties2
}

resource existingStorageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = { name: 'storageaccountname' }

resource vm 'Microsoft.Compute/virtualMachines@2019-12-01' = {
  name: 'vm'
  location: 'eastus'
  properties: {
    diagnosticsProfile: {
      bootDiagnostics: {
        storageUri: reference(storageAccount.id, '2018-02-01').primaryEndpoints.blob
      }
    }
  }
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = { name: 'storageaccountname' }

type superComplexType = {
  p: string
  i: 123 | 456
}

param p { *: superComplexType } = {
  a: { p: 'mystring', i: 123 } // <-- want to extract this value as param
}

param super superComplexType
var v = super

param pp { *: superComplexType } = {
  a: { p: 'mystring', i: 123 }
}
