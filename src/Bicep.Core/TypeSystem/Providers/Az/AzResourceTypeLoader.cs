// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Immutable;
using Azure.Bicep.Types;
using Bicep.Core.Extensions;
using Bicep.Core.Resources;
using Bicep.Core.TypeSystem.Types;

namespace Bicep.Core.TypeSystem.Providers.Az
{
    public class AzResourceTypeLoader : IResourceTypeLoader
    {
        private readonly ITypeLoader typeLoader;
        private readonly AzResourceTypeFactory resourceTypeFactory;
        private readonly ImmutableDictionary<ResourceTypeReference, CrossFileTypeReference> availableTypes;
        private readonly ImmutableDictionary<string, string[]?> typeNameKeywords;
        private readonly ImmutableDictionary<string, ImmutableDictionary<string, ImmutableArray<CrossFileTypeReference>>> availableFunctions;

        public AzResourceTypeLoader(ITypeLoader typeLoader)
        {
            this.typeLoader = typeLoader;
            resourceTypeFactory = new AzResourceTypeFactory();
            var indexedTypes = typeLoader.LoadTypeIndex();
            availableTypes = indexedTypes.Resources.ToImmutableDictionary(
                kvp => ResourceTypeReference.Parse(kvp.Key),
                kvp => kvp.Value);


            var typeNameKeywordsDict = new Dictionary<string, string[]?>(StringComparer.OrdinalIgnoreCase);
            typeNameKeywordsDict["microsoft.web/serverfarms"] = ["appserviceplan", "asp"];// ["appservice", "webapp", "function"];
            this.typeNameKeywords = typeNameKeywordsDict.ToImmutableDictionary();

            // var a = new ResourceTypeReference("microsoft.web/sites", "2020-06-01");
            //a = a with { Aliases = ["appservice", "webapp", "function"] };

            //var availableTypesWithoutAliases = indexedTypes.Resources.ToDictionary(StringComparer.OrdinalIgnoreCase); //asdfgasdfg
            //    kvp => ResourceTypeReference.Parse(kvp.Key),
            //    kvp => kvp.Value);
            //var keys = availableTypesWithoutAliases.Keys.Where(k => k.Name.Contains("microsoft.web", StringComparison.OrdinalIgnoreCase)).ToArray();
            //foreach (var key in keys)
            //{
            //    key.Aliases ??= ["appservice", "webapp", "function"];
            //}

            //availableTypesWithoutAliases.Keys. [new ResourceTypeReference("microsoft.web/sites", "2020-06-01")]
            //foreach (var t in availableTypesWithoutAliases)
            //{
            //    if (t.Key.Name.Contains("microsoft.web", StringComparison.OrdinalIgnoreCase))
            //    {
            //        t.Key.Aliases ??= ["appservice", "webapp", "function"];
            //    }
            //}

            // availableTypes = availableTypesWithoutAliases.ToImmutableDictionary();
            // var asdfg = availableTypes.Select(t => t.Value.RelativePath).Distinct().Order().ToArray();




            availableFunctions = indexedTypes.ResourceFunctions.ToImmutableDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToImmutableDictionary(
                    x => x.Key,
                    x => x.Value.ToImmutableArray(),
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        }

        public string[]? GetSearchKeywords(ResourceTypeReference reference) =>
            typeNameKeywords.TryGetValue(reference.Type, out var keywords) && keywords is string [] ? keywords : null;

        public IEnumerable<ResourceTypeReferenceInfo> GetAvailableTypes() => availableTypes.Keys.Select(x => new ResourceTypeReferenceInfo(x, GetSearchKeywords(x)));

        public bool HasType(ResourceTypeReference reference) => availableTypes.ContainsKey(reference);

        public ResourceTypeComponents LoadType(ResourceTypeReference reference)//asdfgasdfg
        {
            var typeLocation = availableTypes[reference];

            if (!availableFunctions.TryGetValue(reference.FormatType(), out var apiFunctions) ||
                reference.ApiVersion is null ||
                !apiFunctions.TryGetValue(reference.ApiVersion, out var functions))
            {
                functions = ImmutableArray<CrossFileTypeReference>.Empty;
            }

            var functionOverloads = functions.SelectMany(typeLocation => resourceTypeFactory.GetResourceFunctionOverloads(typeLoader.LoadResourceFunctionType(typeLocation)));

            var serializedResourceType = typeLoader.LoadResourceType(typeLocation);
            return resourceTypeFactory.GetResourceType(serializedResourceType, functionOverloads);
        }
    }
}
