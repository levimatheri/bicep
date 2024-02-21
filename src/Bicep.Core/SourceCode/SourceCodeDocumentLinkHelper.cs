// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Bicep.Core.Extensions;
using Bicep.Core.Navigation;
using Bicep.Core.Registry;
using Bicep.Core.Text;
using Bicep.Core.Utils;
using Bicep.Core.Workspaces;
using Uri = System.Uri;

namespace Bicep.Core.SourceCode;

public static class SourceCodeDocumentLinkHelper
{
    /// <summary>
    /// Retrieves all document links for module declaration syntax lines
    /// </summary>
    /// <param name="sourceFileGrouping"></param>
    /// <returns></returns>
    /// <example>
    ///   ```bicep
    ///   module mod1 'subfolder/module1.bicep' = {
    ///   ```bicep
    ///
    ///   will cause a DocumentLink from the source location 'subfolder/module1.bicep' to the source file
    ///   URI resolved from "subfolder/module1.bicep"
    /// </example>
    public static IImmutableDictionary<Uri, SourceCodeDocumentUriLink[]> GetAllModuleDocumentLinks(SourceFileGrouping sourceFileGrouping)
    {
        var dictionary = new Dictionary<Uri, SourceCodeDocumentUriLink[]>();

        foreach (var sourceAndDictPair in sourceFileGrouping.ArtifactResolutionBySyntax)
        {
            ISourceFile referencingFile = sourceAndDictPair.Key;
            ImmutableDictionary<IArtifactReferenceSyntax, ArtifactResolution> referenceSyntaxToArtifactResolution = sourceAndDictPair.Value;

            var referencingFileLineStarts = TextCoordinateConverter.GetLineStarts(referencingFile.GetOriginalSource());

            var linksForReferencingFile = new List<SourceCodeDocumentUriLink>();

            foreach (var syntaxAndArtifactResolution in referenceSyntaxToArtifactResolution)
            {
                IArtifactReferenceSyntax syntax = syntaxAndArtifactResolution.Key;
                Result<Uri, UriResolutionError> uriResult = syntaxAndArtifactResolution.Value.UriResult;
                ArtifactReference? externalArtifactReference = syntaxAndArtifactResolution.Value.ArtifactReference;

                if (syntax.Path is { } && uriResult.IsSuccess(out var uri))
                {
                    var start = new SourceCodePosition(TextCoordinateConverter.GetPosition(referencingFileLineStarts, syntax.Path.Span.Position));
                    var end = new SourceCodePosition(TextCoordinateConverter.GetPosition(referencingFileLineStarts, syntax.Path.Span.Position + syntax.Path.Span.Length));

                    linksForReferencingFile.Add(new SourceCodeDocumentUriLink(new SourceCodeRange(start, end), uri));
                }
            }

            dictionary.Add(referencingFile.FileUri, linksForReferencingFile.ToArray());
        }

        return dictionary.ToImmutableDictionary();
    }
}
