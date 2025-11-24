// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Abstractions;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Bicep.IO.Abstraction;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Bicep.Core.ExternalInputs
{
    internal class ExternalInputRpcPlugin(
        JsonRpc client,
        Process process,
        IOUri pluginUri) : IAsyncDisposable
    {
        public static async Task<ExternalInputRpcPlugin> Start(
            IOUri pluginUri,
            IFileSystem fileSystem,
            CancellationToken cancellationToken)
        {
            string processArgs;
            Func<CancellationToken, Task<Stream>> streamBuilder;
            Stream? stream = null;
            JsonRpc? client = null;
            if (Socket.OSSupportsUnixDomainSockets)
            {
                var socketName = $"{Guid.NewGuid()}.tmp";
                var socketPath = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), socketName);

                if (fileSystem.File.Exists(socketPath))
                {
                    fileSystem.File.Delete(socketPath);
                }

                processArgs = $"--socket {socketPath}";
                streamBuilder = async (cancToken) => await ExternalInputRpcHelper.CreateDomainSocketStream(socketPath, cancToken).ConfigureAwait(false);
            }
            else
            {
                var pipeName = $"{Guid.NewGuid()}.tmp";
                processArgs = $"--pipe {pipeName}";

                streamBuilder = async (cancToken) => await ExternalInputRpcHelper.CreateNamedPipeStream(pipeName, cancToken).ConfigureAwait(false);
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pluginUri.GetFilePath(),
                    Arguments = processArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            try
            {
                // 30s timeout for starting up the RPC connection
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                process.EnableRaisingEvents = true;
                process.Exited += (sender, e) => cts.Cancel();
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        WriteTrace(pluginUri, () => $"stdout: {e.Data}");
                    }
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        WriteTrace(pluginUri, () => $"stderr: {e.Data}");
                    }
                };

                process.Start();

                process.BeginErrorReadLine();
                process.BeginOutputReadLine();

                stream = await streamBuilder(cancellationToken);
                client = JsonRpc.Attach(stream);

                return new ExternalInputRpcPlugin(client, process, pluginUri);
            }
            catch (Exception ex)
            {
                await TerminateProcess(pluginUri, process, client);
                if (stream is not null)
                {
                    await stream.DisposeAsync();
                };
                throw new InvalidOperationException($"Failed to connect to RPC plugin {pluginUri}", ex);
            }
        }

        public async Task<ExternalInputRpcResponse> ResolveExternalInput(ExternalInputRpcRequest request)
        {
            WriteTrace(pluginUri, () => $"{nameof(ResolveExternalInput)} JSON RPC request: {JsonSerializer.Serialize(request, ExternalInputRpcRequestContext.Default.ExternalInputRpcRequest)}");

            var response = await client.InvokeAsync<ExternalInputRpcResponse>(
                "resolveExternalInput",
                request);

            WriteTrace(pluginUri, () => $"{nameof(ResolveExternalInput)} JSON RPC response: {JsonSerializer.Serialize(response, ExternalInputRpcResponseContext.Default.ExternalInputRpcResponse)}");

            return response;
        }

        public async ValueTask DisposeAsync()
        {
            await TerminateProcess(pluginUri, process, client);
        }

        private static void WriteTrace(IOUri pluginUri, Func<string> getMessage)
            => Trace.WriteLine($"[{pluginUri}] {getMessage()}");

        private static async Task TerminateProcess(IOUri binaryUri, Process process, JsonRpc? client)
        {
            try
            {
                if (!process.HasExited)
                {
                    // let's try and force-kill the process until we have a better option (e.g. sending a SIGTERM, or adding a Close event to the gRPC contract)
                    process.Kill();

                    // wait for a maximum of 15s for shutdown to occur - otherwise, give up and detatch, in case the process has hung
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await process.WaitForExitAsync(cts.Token);
                }
            }
            catch (Exception ex)
            {
                WriteTrace(binaryUri, () => $"Failed to terminate process: {ex}");
                // ignore exceptions - this is best-effort, and we want to avoid an exception from
                // process.Kill() bubbling up and masking the original exception that was thrown
            }
            finally
            {
                client?.Dispose();
                process.Dispose();
            }
        }
    }

    
    internal record ExternalInputRpcRequest(
        string Kind,
        string Config,
        JsonObject Settings);

    internal record ExternalInputRpcResponse(
        IEnumerable<ResolvedValue> ResolvedValues,
        IEnumerable<Diagnostic> Diagnostics);

    internal record ResolvedValue(JsonElement Value, AdditionalInfo AdditionalInfo);
    internal record AdditionalInfo(string RolloutInfra, string RolloutSpec, string Region);
    internal record Diagnostic(string Message, string Severity, AdditionalInfo AdditionalInfo);

    [JsonSerializable(typeof(ExternalInputRpcRequest))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
    internal partial class ExternalInputRpcRequestContext : JsonSerializerContext;

    [JsonSerializable(typeof(ExternalInputRpcResponse))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
    internal partial class ExternalInputRpcResponseContext : JsonSerializerContext;
}
