// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Bicep.Core.Registry;
using Bicep.LanguageServer.Extensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Bicep.LanguageServer.Handlers
{
#nullable disable
    public partial record Asdfg : IHandlerIdentity
    {
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

            var links = GetDocumentLinks(moduleDispatcher, request, cancellationToken);
            return Task.FromResult(new DocumentLinkContainer<Asdfg>(links));
            //request.WorkDoneToken asdfg
            //request.PartialResultToken asdfg
        }

        protected override DocumentLinkRegistrationOptions CreateRegistrationOptions(DocumentLinkCapability capability, ClientCapabilities clientCapabilities) => new()
        {
            DocumentSelector = TextDocumentSelector.ForScheme(LangServerConstants.ExternalSourceFileScheme),
            ResolveProvider = true,
        };

        public static IEnumerable<DocumentLink<Asdfg>> GetDocumentLinks(IModuleDispatcher moduleDispatcher, DocumentLinkParams request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Trace.WriteLine($"Handling document link: {request.TextDocument.Uri}");

            if (request.TextDocument.Uri.Scheme == LangServerConstants.ExternalSourceFileScheme)
            {
                ExternalSourceReference? externalReference;
                try
                {
                    externalReference = new ExternalSourceReference(request.TextDocument.Uri);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"(Experimental) There was an error retrieving source code for this module: {ex.Message}");
                    yield break;
                }

                if (externalReference.RequestedFile is not null)
                {
                    if (!externalReference.ToArtifactReference().IsSuccess(out var artifactReference, out var message))
                    {
                        Trace.WriteLine(message);
                        yield break;
                    }

                    if (!moduleDispatcher.TryGetModuleSources(artifactReference).IsSuccess(out var sourceArchive, out var ex))
                    {
                        Trace.WriteLine(ex.Message);
                        yield break;
                    }

                    //asdfg var source sourceArchive.FindExpectedSourceFile(externalReference.RequestedFile);
                    if (sourceArchive.DocumentLinks.TryGetValue(externalReference.RequestedFile, out var links))
                    {
                        foreach (var link in links)
                        {
                            var targetFile = "main.json";
                            var targetFileInfo = sourceArchive.FindExpectedSourceFile(link.Target);
                            if (targetFileInfo.Source is not null && targetFileInfo.Source.StartsWith("br:"))
                            {
                                targetFile = targetFileInfo.Source;
                            }

                            yield return new DocumentLink()
                            {
                                Range = link.Range.ToRange(),
                                Target = new ExternalSourceReference(request.TextDocument.Uri)
                                    .WithRequestForSourceFile(targetFile).ToUri().ToString()
                            };
                        }
                    }
                }
            }
        }

        protected override Task<DocumentLink<Asdfg>> HandleResolve(DocumentLink<Asdfg> request, CancellationToken cancellationToken)
        {
            Trace.WriteLine($"Resolving document link: {request.Target}");
            return Task.FromResult(request);
        }
    }
}
