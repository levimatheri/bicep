// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bicep.Core.Extensions;

namespace Bicep.Core.Configuration;

// Represents the configuration object for a single external input resolver
public record ExternalInputResolverEntry
{
    // Path to the resolver target (executable / script). Required.
    public required string Target { get; init; }

    // Resolver-specific parameter bag passed to the resolver tooling. Required.
    // Properties are specific to the external input kind.
    public required JsonObject Settings { get; init; }
}

// The top-level map: key = external input kind (supports wildcards e.g. ev2.*) -> resolver entry
public partial class ExternalInputResolverConfiguration : ConfigurationSection<ImmutableDictionary<string, ExternalInputResolverEntry>>
{
    private ExternalInputResolverConfiguration(ImmutableDictionary<string, ExternalInputResolverEntry> data) : base(data) { }

    public static ExternalInputResolverConfiguration Empty => new(ImmutableDictionary.Create<string, ExternalInputResolverEntry>());

    public static ExternalInputResolverConfiguration Bind(JsonElement element)
    {
        // Treat an absent or null object as empty configuration
        if (element.ValueKind == JsonValueKind.Undefined || element.ValueKind == JsonValueKind.Null)
        {
            return Empty;
        }

        var dict = element.ToNonNullObject<ImmutableDictionary<string, ExternalInputResolverEntry>>();
        return new ExternalInputResolverConfiguration(dict);
    }

    public bool TryGetResolver(string kind, [NotNullWhen(true)] out ExternalInputResolverEntry? entry)
    {
        foreach (var kvp in this.Data)
        {
            if (Regex.IsMatch(kind, kvp.Key))
            {
                entry = kvp.Value;
                return true;
            }
        }
        entry = null;
        return false;
    }
}
