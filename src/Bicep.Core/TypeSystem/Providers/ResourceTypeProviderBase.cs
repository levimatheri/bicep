// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Immutable;
using System.Linq;
using Bicep.Core.Resources;

namespace Bicep.Core.TypeSystem.Providers;

public abstract class ResourceTypeProviderBase
{
    protected readonly ImmutableDictionary<ResourceTypeReference, ResourceTypeReferenceInfo> availableResourceTypes;
    protected readonly Lazy<ImmutableDictionary<string, ImmutableArray<ResourceTypeReferenceInfo>>> typeReferencesByTypeLazy;

    public ImmutableDictionary<string, ImmutableArray<ResourceTypeReferenceInfo/*asdfg Info?*/>> TypeReferencesByType => typeReferencesByTypeLazy.Value;

    protected ResourceTypeProviderBase(IEnumerable< ResourceTypeReferenceInfo> availableResourceTypes/*asdfg ??*/)
    {
        // Only enumerate availableResourceTypes once to avoid redundant work
        this.availableResourceTypes = availableResourceTypes.ToImmutableDictionary(x => x.TypeReference, x => x);
        typeReferencesByTypeLazy = new(() => this.availableResourceTypes.Values //asdfg
            .GroupBy(x => x.TypeReference.Type, StringComparer.OrdinalIgnoreCase)
            .ToImmutableDictionary(x => x.Key, x => x.ToImmutableArray()));
    }

    public bool HasDefinedType(ResourceTypeReference typeReference)
        => availableResourceTypes.ContainsKey(typeReference);

    public IEnumerable<ResourceTypeReferenceInfo> GetAvailableTypes()
        => availableResourceTypes.Values;
}
