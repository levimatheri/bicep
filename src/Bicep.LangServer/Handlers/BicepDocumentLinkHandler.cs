// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Transactions;
using Bicep.Core.Diagnostics;
using Bicep.Core.Registry;
using Bicep.Core.Registry.Oci;
using Bicep.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Bicep.LanguageServer.Handlers
{
#nullable disable
    public partial record Asdfg : IHandlerIdentity
    {
        public string TargetArtifactId;

        public Asdfg(string targetArtifactId)
        {
            this.TargetArtifactId = targetArtifactId;
        }
    }
#nullable restore

    //asdfg
    //public partial record Data : IHandlerIdentity
    //{
    //    public string Name { get; init; }
    //    public Guid Id { get; init; }
    //    public string Child { get; init; }
    //}

    public class BicepDocumentLinkHandler : DocumentLinkHandlerBase<Asdfg>
    {
        private readonly IModuleDispatcher moduleDispatcher;

        public BicepDocumentLinkHandler(IModuleDispatcher moduleDispatcher)
        {
            this.moduleDispatcher = moduleDispatcher;
        }

        protected override Task<DocumentLinkContainer<Asdfg>> HandleParams(DocumentLinkParams request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Trace.WriteLine($"Handling document link: {request.TextDocument.Uri}"); //asdfg

            var links = GetDocumentLinksToNestedExternalSourceFiles(moduleDispatcher, request, cancellationToken);
            return Task.FromResult(new DocumentLinkContainer<Asdfg>(links));
            //request.WorkDoneToken asdfg
            //request.PartialResultToken asdfg
        }

        protected override DocumentLinkRegistrationOptions CreateRegistrationOptions(DocumentLinkCapability capability, ClientCapabilities clientCapabilities) => new()
        {
            DocumentSelector = TextDocumentSelector.ForScheme(LangServerConstants.ExternalSourceFileScheme),
            ResolveProvider = true,
        };

        /// <summary>
        /// This handles the case where the document is a source file from an external module, and we've been asked to return nested links within it (to files local to that module or to other external modules)
        /// </summary>
        public static IEnumerable<DocumentLink<Asdfg>> GetDocumentLinksToNestedExternalSourceFiles(IModuleDispatcher moduleDispatcher, DocumentLinkParams request, CancellationToken cancellationToken)
        {
            var currentDocument = request.TextDocument;

            if (currentDocument.Uri.Scheme == LangServerConstants.ExternalSourceFileScheme)
            {
                ExternalSourceReference? currentDocumentReference;
                try
                {
                    currentDocumentReference = new ExternalSourceReference(currentDocument.Uri);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"There was an error retrieving source code for this module: {ex.Message}");
                    yield break;
                }

                var currentDocumentRelativeFile = currentDocumentReference.RequestedFile;
                if (currentDocumentRelativeFile is { })
                {
                    if (!currentDocumentReference.ToArtifactReference().IsSuccess(out var currentDocumentArtifact, out var message))
                    {
                        Trace.WriteLine(message);
                        yield break;
                    }

                    if (!moduleDispatcher.TryGetModuleSources(currentDocumentArtifact).IsSuccess(out var currentDocumentSourceArchive, out var ex))
                    {
                        Trace.WriteLine(ex.Message);
                        yield break;
                    }

                    //asdfg var source currentDocumentSourceArchive.FindExpectedSourceFile(currentDocumentReference.RequestedFile);
                    if (currentDocumentSourceArchive.DocumentLinks.TryGetValue(currentDocumentRelativeFile, out var nestedLinks))
                    {
                        foreach (var nestedLink in nestedLinks)
                        {
                            // Does this nested link have a pointer to its artifact so we can try restoring it and get the source?
                            var targetFileInfo = currentDocumentSourceArchive.FindExpectedSourceFile(nestedLink.Target);
                            if (targetFileInfo.SourceArtifactId is { } && targetFileInfo.SourceArtifactId.StartsWith(OciArtifactReferenceFacts.SchemeWithColon)) //asdfg test ignore if not "br:"
                            {
                                // Yes, it's an external module with source.  Resolve it when clicked so we can attempt to retrieve source.
                                var sourceId = targetFileInfo.SourceArtifactId.Substring(OciArtifactReferenceFacts.SchemeWithColon.Length);
                                yield return new DocumentLink<Asdfg>()
                                {
                                    Range = nestedLink.Range.ToRange(),
                                    Data = new Asdfg(sourceId),
                                    //Target = new ExternalSourceReference(request.TextDocument.Uri) asdfg
                                    //    .WithRequestForSourceFile(targetFileInfo.Path).ToUri().ToString(),
                                };
                            }

                            yield return new DocumentLink()
                            {
                                // This is a link to a file that we don't have source for, so we'll just display the main.json file
                                Range = nestedLink.Range.ToRange(),
                                Target = new ExternalSourceReference(request.TextDocument.Uri)
                                    .WithRequestForSourceFile(targetFileInfo.Path).ToUri().ToString(),
                            };
                        }
                    }
                }
            }
        }

        protected override Task<DocumentLink<Asdfg>> HandleResolve(DocumentLink<Asdfg> request, CancellationToken cancellationToken)
        {
            //asdfg telemetry

            Trace.WriteLine($"Resolving document link: {request.Target}");

            var data = request.Data;

            if (!OciArtifactReference.TryParseModule(data.TargetArtifactId).IsSuccess(out var targetArtifactReference, out var error))
            {
                Trace.WriteLine(error(DiagnosticBuilder.ForDocumentStart()).Message); //asdfg
                return Task.FromResult(request);
            }

            if (!moduleDispatcher.TryGetModuleSources(targetArtifactReference).IsSuccess(out var sourceArchive, out var ex))
            {
                Trace.WriteLine(ex.Message); //asdfg?
                return Task.FromResult(request);
            }

            request = request with
            {
                Target = new ExternalSourceReference(targetArtifactReference, sourceArchive).ToUri().ToString()
            };
            return Task.FromResult(request);
        }
    }
}
