// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Resources;
using Bicep.Core.TypeSystem.Types;

namespace Bicep.Core.TypeSystem.Providers
{
    public interface IResourceTypeLoader
    {
        ResourceTypeComponents LoadType(ResourceTypeReference reference);

        IEnumerable<ResourceTypeReferenceInfo> GetAvailableTypes();//asdfg - yes, Info

        //public string[] GetSearchKeywords(ResourceTypeReference reference)//asdfg?
        //{
        //    return Array.Empty<string>();
        //}
    }
}
