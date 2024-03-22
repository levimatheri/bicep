// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Immutable;
using Bicep.Core.Resources;
using Bicep.Core.TypeSystem.Types;

namespace Bicep.Core.TypeSystem.Providers
{
    public class EmptyResourceTypeProvider : IResourceTypeProvider
    {
        public IEnumerable<ResourceTypeReferenceInfo> GetAvailableTypes()
            => Enumerable.Empty<ResourceTypeReferenceInfo>();

        public ResourceType? TryGetDefinedType(NamespaceType declaringNamespace, ResourceTypeReference reference, ResourceTypeGenerationFlags flags)
            => null;

        public ResourceType? TryGenerateFallbackType(NamespaceType declaringNamespace, ResourceTypeReference reference, ResourceTypeGenerationFlags flags)
            => null;

        public bool HasDefinedType(ResourceTypeReference typeReference)
            => false;

        public ImmutableDictionary<string, ImmutableArray<ResourceTypeReferenceInfo>> TypeReferencesByType
            => ImmutableDictionary<string, ImmutableArray<ResourceTypeReferenceInfo>>.Empty;

        public string Version { get; } = "1.0.0";
    }
}
