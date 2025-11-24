// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Bicep.Core.Configuration;
using Bicep.Core.Diagnostics;
using Bicep.IO.Abstraction;
using Newtonsoft.Json.Linq;

namespace Bicep.Core.ExternalInputs;

public class ExternalInputDispatcher
{
    private readonly IFileSystem fileSystem;
    private readonly ExternalInputResolverConfiguration resolverConfig;
    private readonly Dictionary<string, ExternalInputRpcPlugin> rpcPluginCache = new();

    public ExternalInputDispatcher(
        IFileSystem fileSystem,
        ExternalInputResolverConfiguration resolverConfig)
    {
        this.fileSystem = fileSystem;
        this.resolverConfig = resolverConfig;
    }

    public async Task<ExternalInputRpcResponse> Resolve(
        string kind,
        object config,
        CancellationToken cancellationToken)
    {
        if (!this.resolverConfig.TryGetResolver(kind, out var resolverEntry))
        {
            throw new Exception(); // TODO: Better exception/diagnostic
        }

        rpcPluginCache.TryAdd(kind, await ExternalInputRpcPlugin.Start(
            IOUri.FromFilePath(resolverEntry.Target),
            fileSystem,
            cancellationToken));

        await InvokePlugin(
            kind,
            JToken.FromObject(config),
            cancellationToken);
    }

    private async Task<ExternalInputRpcResponse> InvokePlugin(
        string kind,
        JToken config,
        CancellationToken cancellationToken)
    {
        var plugin = rpcPluginCache[kind];
        var response = await plugin.
    }
}
