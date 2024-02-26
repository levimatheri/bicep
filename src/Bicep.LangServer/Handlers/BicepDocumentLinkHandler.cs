// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Transactions;
using Azure.Deployments.Core;
using Bicep.Core.Diagnostics;
using Bicep.Core.Registry;
using Bicep.Core.Registry.Oci;
using Bicep.LanguageServer.Extensions;
using Bicep.LanguageServer.Providers;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server.Messages;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;

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

    public class BicepDocumentLinkHandler(IModuleDispatcher ModuleDispatcher, ILanguageServerFacade Server)
        : DocumentLinkHandlerBase<Asdfg>
    {
        protected override Task<DocumentLinkContainer<Asdfg>> HandleParams(DocumentLinkParams request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Trace.WriteLine($"Handling document link: {request.TextDocument.Uri}"); //asdfg

            //using var reporter = Server.ProgressManager.For(
            //   request, new WorkDoneProgressBegin
            //   {
            //       Cancellable = true,
            //       Message = "This might take a while...",
            //       Title = "Some long task....",
            //       Percentage = 0
            //   },
            //   cancellationToken
            //);
            //await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
            //reporter.OnNext(
            //     new WorkDoneProgressReport
            //     {
            //         Cancellable = true,
            //         Percentage = 20
            //     }
            // );
            //await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
            //reporter.OnNext(
            //     new WorkDoneProgressReport
            //     {
            //         Cancellable = true,
            //         Percentage = 40
            //     }
            // );

            //reporter.OnNext(
            //      new WorkDoneProgressReport
            //      {
            //          Cancellable = true,
            //          Percentage = 100
            //      }
            //  );

            var links = GetDocumentLinksToNestedExternalSourceFiles(ModuleDispatcher, request, cancellationToken);
            return Task.FromResult(new DocumentLinkContainer<Asdfg>(links));
            //request.WorkDoneToken asdfg
            //request.PartialResultToken asdfg
            
        }

        protected override DocumentLinkRegistrationOptions CreateRegistrationOptions(DocumentLinkCapability capability, ClientCapabilities clientCapabilities) => new()
        {
            DocumentSelector = TextDocumentSelector.ForScheme(LangServerConstants.ExternalSourceFileScheme),
            ResolveProvider = true,
            WorkDoneProgress = true,
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

        private int delay = 10;
        protected override async Task<DocumentLink<Asdfg>> HandleResolve(DocumentLink<Asdfg> request, CancellationToken cancellationToken)
        {
            //asdfg telemetry

            Trace.WriteLine($"Resolving document link: {request.Target}");

            var data = request.Data;

            if (!OciArtifactReference.TryParseModule(data.TargetArtifactId).IsSuccess(out var targetArtifactReference, out var error))
            {
                Trace.WriteLine(error(DiagnosticBuilder.ForDocumentStart()).Message); //asdfg
                return request;
            }

            var restoreStatus = ModuleDispatcher.GetArtifactRestoreStatus(targetArtifactReference, out var errorBuilder);
            Trace.WriteLine($"Restore status: {restoreStatus}"); //asdfg: what about failure?
            if (restoreStatus != ArtifactRestoreStatus.Succeeded)
            {
                var errorMessage = errorBuilder is { } ? errorBuilder(DiagnosticBuilder.ForDocumentStart()).Message : "The module has not yet been successfully restored.";
                Trace.WriteLine(errorMessage); //asdfg
                if (!await ModuleDispatcher.RestoreModules(new[] { targetArtifactReference }, forceRestore: true))
                {
                    throw new InvalidOperationException("The module has not yet been successfully restored. asdfg");
                }
            }

            if (!ModuleDispatcher.TryGetModuleSources(targetArtifactReference).IsSuccess(out var sourceArchive, out var ex))
            {
                Trace.WriteLine(ex.Message); //asdfg
                throw ex; //asdfg
            }



            //WorkDoneProgressCreateExtensions.SendWorkDoneProgressCreate(Server,new WorkDoneProgressCreateParams() {  new WorkDoneProgressBegin() { Title = "asdfg", });

            ////string token = WorkDoneToken = new Guid().ToString();
            Server.SendNotification("progress", new WorkDoneProgressBegin() { Title = "asdfg", });




            //Server.SendNotification(new ProgressParams() { Token = request.WorkDoneToken, Value = new WorkDoneProgressBegin() { Title = "asdfg", Percentage = 0 } });

            //// Create a WorkDoneProgress object with a unique token
            //var progress = new WorkDoneProgressBegin() {  Message = "Starting my task...asdfg", Title = "asdfg" };
            

            //// Send a begin notification with the title and message of the task
            //await progress.Begin(new WorkDoneProgressBegin
            //{
            //    Title = "My Task",
            //    Message = "Starting my task..."
            //}, cancellationToken);

            //// Do some work and report the progress
            //for (int i = 0; i < 100; i++)
            //{
            //    await Task.Delay(100, cancellationToken); // Simulate some work
            //    await progress.Report(new WorkDoneProgressReport
            //    {
            //        Message = $"Processing {i + 1}%",
            //        Percentage = i + 1
            //    }, cancellationToken);
            //}

            //// Send an end notification with the message of the task
            //await progress.End(new WorkDoneProgressEnd
            //{
            //    Message = "Finished my task."
            //}, cancellationToken);





            //ProgressExtensions.SendProgress(Server,new ProgressParams() {  Token = request.}) => mediator.SendNotification(request);

            //ProgressExtensions.SendProgress(Server, new WorkDoneProgressBegin
            //{
            //    Title = "My Task",
            ////    Message = "Starting my task..."
            //});
            //// Create a WorkDoneProgress object with a unique token
            //var progress = new WorkDoneProgress(_responseRouter, Guid.NewGuid().ToString());

            //// Send a begin notification with the title and message of the task
            //await progress.Begin(new WorkDoneProgressBegin
            //{
            //    Title = "My Task",
            //    Message = "Starting my task..."
            //}, cancellationToken);

            //// Do some work and report the progress
            //for (int i = 0; i < 100; i++)
            //{
            //    await Task.Delay(100, cancellationToken); // Simulate some work
            //    await progress.Report(new WorkDoneProgressReport
            //    {
            //        Message = $"Processing {i + 1}%",
            //        Percentage = i + 1
            //    }, cancellationToken);
            //}

            //// Send an end notification with the message of the task
            //await progress.End(new WorkDoneProgressEnd
            //{
            //    Message = "Finished my task."
            //}, cancellationToken);



            // Delay to simulate a long-running operation
            await Task.Delay(delay, cancellationToken);

            return request with
            {
                Target = new ExternalSourceReference(targetArtifactReference, sourceArchive).ToUri().ToString()
            };
        }
    }
}
