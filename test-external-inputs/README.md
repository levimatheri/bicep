# External Input Resolver Implementation

## Overview

This implementation adds support for resolving `externalInput` functions in Bicep parameter files using external resolver plugins via JSON-RPC.

## Implementation Summary

### Core Infrastructure (Bicep.Core)

1. **ExternalInputResolveStatus.cs** - Enum tracking resolution states (Unknown, Failed, Succeeded)

2. **ExternalInputResolverModels.cs** - Request/Response models for JSON-RPC communication:
   - `ResolveExternalInputRequest` - Request with kind and optional config
   - `ResolvedValue` - Single resolved value with optional additional info
   - `ResolveExternalInputResponse` - Response containing array of resolved values

3. **IExternalInputResolver.cs** - Interface for resolver implementations

4. **ExternalInputResolverClient.cs** - JSON-RPC client that:
   - Starts external resolver process
   - Connects via named pipes
   - Sends/receives JSON-RPC messages using StreamJsonRpc protocol
   - Manages process lifecycle

5. **ExternalInputResolver.cs** - Main resolver service that:
   - Caches resolution results per function call
   - Manages resolver client lifecycle
   - Supports wildcard matching for resolver configuration (e.g., `ev2.*` matches `ev2.binding`)
   - Returns resolution status and resolved values

6. **ExternalInputHelper.cs** - Helper methods to extract external input calls from semantic models

### Configuration

7. **ExternalInputResolverConfiguration.cs** - Extended with:
   - `TryGetResolverForKind()` - Supports wildcard pattern matching
   - `IsWildcardMatch()` - Pattern matching logic for kinds like `ev2.*`

### Diagnostics

8. **DiagnosticBuilder.cs** - Added new diagnostic codes:
   - `BCP444` - External input requires resolution
   - `BCP445` - External input resolution failed
   - `BCP446` - No resolver configured (warning)
   - `BCP447` - Resolver executable not found
   - `BCP448` - Type mismatch between resolved value and parameter type

### Semantic Model Integration

9. **SemanticModel.cs** - Added:
   - `GetExternalInputResolutionDiagnostics()` - Checks resolution status and validates types
   - Helper methods to extract external input kind and validate resolved values

### CLI Integration

10. **BuildParamsCommand.cs** - Modified to:
    - Extract external input calls from params files
    - Create resolver if configured
    - Resolve external inputs before compilation
    - Include resolution diagnostics in output

## Configuration Format

In `bicepconfig.json`:

```json
{
  "externalInputResolverConfig": {
    "ev2.*": {
      "target": "path/to/resolver.exe",
      "parameters": {
        "configPath": "/path/to/config"
      }
    }
  }
}
```

- **Keys**: External input kinds (supports wildcards like `ev2.*`)
- **target**: Path to the resolver executable (JSON-RPC server)
- **parameters**: Resolver-specific configuration parameters

## Usage

### In Bicep Params File

```bicepparam
using 'main.bicep'

param appName = externalInput('ev2.binding')
param location = 'eastus'
```

### Running build-params Command

```bash
bicep build-params main.bicepparam
```

The CLI will:
1. Parse the params file and detect `externalInput` calls
2. Check `bicepconfig.json` for resolver configuration
3. Start the configured resolver process
4. Send JSON-RPC `resolve` request for each external input
5. Receive resolved values
6. Validate types against parameter declarations
7. Generate parameters.json with resolved values or show diagnostics

## JSON-RPC Protocol

### Request

```json
{
  "jsonrpc": "2.0",
  "method": "resolve",
  "params": {
    "kind": "ev2.binding",
    "config": { /* optional config */ }
  },
  "id": 1
}
```

### Response

```json
{
  "jsonrpc": "2.0",
  "result": {
    "resolvedValues": [
      {
        "value": "resolved-value-here",
        "additionalInfo": { /* optional metadata */ }
      }
    ]
  },
  "id": 1
}
```

## Testing

To test with the provided Ev2Rpc resolver:

1. Navigate to `test-external-inputs/`
2. Ensure `Ev2Rpc.exe` is built and running on pipe `StreamJsonRpcSamplePipe`
3. Run: `bicep build-params main.bicepparam`

## Architecture Notes

- **Non-async semantic model**: Follows module restoration pattern where resolution happens before semantic model construction
- **Status-based diagnostics**: Uses cached resolution results to avoid re-resolving
- **Wildcard support**: Allows configuring one resolver for multiple related kinds (e.g., `ev2.*`)
- **Process management**: Resolver process is started on-demand and properly disposed
- **Error handling**: Resolution failures produce diagnostics but don't crash compilation

## Future Enhancements

- Support for multiple resolved values per external input
- Advanced type validation for resolved values
- Resolver timeout configuration
- Resolver retry logic
- Support for resolver discovery/registration
- Language server integration for real-time resolution
