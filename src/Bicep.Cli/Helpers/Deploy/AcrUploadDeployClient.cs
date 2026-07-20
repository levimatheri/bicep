// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bicep.Core.Registry.Oci;
using Microsoft.Extensions.Logging;

namespace Bicep.Cli.Helpers.Deploy;

/// <summary>
/// Client for the AcrUpload web API. Implements the "request an upload link, then
/// AcrPush, then deploy" flow: it asks the server for a scoped push token, pushes
/// the compiled ARM template to the registry as an OCI artifact using that token,
/// then asks the server to deploy the pushed artifact.
/// </summary>
public sealed class AcrUploadDeployClient(HttpClient httpClient, ILogger logger)
{
    private const string ManifestMediaType = "application/vnd.oci.image.manifest.v1+json";

    public async Task<AcrUploadJson.DeployResult> DeployAsync(
        Uri serverUri,
        string repository,
        string tag,
        BinaryData compiledArmTemplate,
        string deploymentName,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Requesting upload token from {Server}", serverUri);
        var token = await RequestUploadTokenAsync(serverUri, repository, cancellationToken);
        logger.LogInformation("Received upload token for {LoginServer}/{Repository}", token.LoginServer, token.Repository);

        var reference = await PushArtifactAsync(token, tag, compiledArmTemplate, cancellationToken);
        logger.LogInformation("Pushed artifact to {Reference}", reference);

        var result = await RequestDeployAsync(serverUri, reference, deploymentName, cancellationToken);
        logger.LogInformation("Server recorded deployment {DeploymentId} with status {Status}", result.DeploymentId, result.Status);

        return result;
    }

    private async Task<AcrUploadJson.UploadTokenResponse> RequestUploadTokenAsync(Uri serverUri, string repository, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(serverUri, "registry/upload-token");
        using var response = await httpClient.PostAsJsonAsync(
            requestUri,
            new AcrUploadJson.UploadTokenRequest(repository),
            AcrUploadJsonContext.Default.UploadTokenRequest,
            cancellationToken);
        await EnsureSuccessAsync(response, "request an upload token", cancellationToken);

        return await response.Content.ReadFromJsonAsync(AcrUploadJsonContext.Default.UploadTokenResponse, cancellationToken)
            ?? throw new InvalidOperationException("The AcrUpload server returned an empty upload token response.");
    }

    private async Task<string> PushArtifactAsync(AcrUploadJson.UploadTokenResponse token, string tag, BinaryData compiledArmTemplate, CancellationToken cancellationToken)
    {
        // Config and layer blobs mirror the format produced by `bicep publish`.
        var configBlob = BinaryData.FromString("{}");
        var configDigest = await UploadBlobAsync(token, configBlob, cancellationToken);
        var layerDigest = await UploadBlobAsync(token, compiledArmTemplate, cancellationToken);

        var manifest = new AcrUploadJson.OciManifest(
            SchemaVersion: 2,
            MediaType: ManifestMediaType,
            ArtifactType: BicepMediaTypes.BicepModuleArtifactType,
            Config: new AcrUploadJson.OciDescriptor(BicepMediaTypes.BicepModuleConfigV1, configDigest, configBlob.ToArray().Length),
            Layers: [new AcrUploadJson.OciDescriptor(BicepMediaTypes.BicepModuleLayerV1Json, layerDigest, compiledArmTemplate.ToArray().Length)]);

        var manifestJson = JsonSerializer.Serialize(manifest, AcrUploadJsonContext.Default.OciManifest);
        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        var manifestUri = new Uri($"https://{token.LoginServer}/v2/{token.Repository}/manifests/{tag}");
        using var request = new HttpRequestMessage(HttpMethod.Put, manifestUri)
        {
            Content = new ByteArrayContent(manifestBytes),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(ManifestMediaType);
        AddAuthorization(request, token);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "upload the artifact manifest", cancellationToken);

        return $"{token.LoginServer}/{token.Repository}:{tag}";
    }

    private async Task<string> UploadBlobAsync(AcrUploadJson.UploadTokenResponse token, BinaryData blob, CancellationToken cancellationToken)
    {
        var digest = ComputeDigest(blob);

        // Start an upload session to obtain the upload location.
        var startUri = new Uri($"https://{token.LoginServer}/v2/{token.Repository}/blobs/uploads/");
        using var startRequest = new HttpRequestMessage(HttpMethod.Post, startUri);
        AddAuthorization(startRequest, token);

        using var startResponse = await httpClient.SendAsync(startRequest, cancellationToken);
        await EnsureSuccessAsync(startResponse, "begin a blob upload", cancellationToken);

        var location = startResponse.Headers.Location
            ?? throw new InvalidOperationException("The registry did not return an upload location.");
        var uploadUri = location.IsAbsoluteUri ? location : new Uri(startUri, location);

        // Monolithic upload: PUT the blob bytes with the digest query parameter.
        var separator = string.IsNullOrEmpty(uploadUri.Query) ? "?" : "&";
        var putUri = new Uri($"{uploadUri}{separator}digest={digest}");
        using var putRequest = new HttpRequestMessage(HttpMethod.Put, putUri)
        {
            Content = new ByteArrayContent(blob.ToArray()),
        };
        putRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        AddAuthorization(putRequest, token);

        using var putResponse = await httpClient.SendAsync(putRequest, cancellationToken);
        await EnsureSuccessAsync(putResponse, "upload a blob", cancellationToken);

        return digest;
    }

    private async Task<AcrUploadJson.DeployResult> RequestDeployAsync(Uri serverUri, string artifactReference, string name, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(serverUri, "deploy");
        using var response = await httpClient.PostAsJsonAsync(
            requestUri,
            new AcrUploadJson.DeployRequest(artifactReference, name),
            AcrUploadJsonContext.Default.DeployRequest,
            cancellationToken);
        await EnsureSuccessAsync(response, "deploy the artifact", cancellationToken);

        return await response.Content.ReadFromJsonAsync(AcrUploadJsonContext.Default.DeployResult, cancellationToken)
            ?? throw new InvalidOperationException("The AcrUpload server returned an empty deploy response.");
    }

    private static void AddAuthorization(HttpRequestMessage request, AcrUploadJson.UploadTokenResponse token)
        => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

    private static string ComputeDigest(BinaryData data)
        => $"sha256:{Convert.ToHexStringLower(SHA256.HashData(data.ToArray()))}";

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Failed to {action}: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
    }
}

/// <summary>
/// DTOs exchanged with the AcrUpload web API and the registry.
/// </summary>
public static class AcrUploadJson
{
    public sealed record UploadTokenRequest(string Repository);

    public sealed record UploadTokenResponse(string LoginServer, string Repository, string AccessToken, DateTimeOffset ExpiresOn);

    public sealed record DeployRequest(string ArtifactReference, string Name);

    public sealed record DeployResult(string DeploymentId, string Status, string ArtifactReference, DateTimeOffset CreatedAt);

    public sealed record OciManifest(int SchemaVersion, string MediaType, string ArtifactType, OciDescriptor Config, OciDescriptor[] Layers);

    public sealed record OciDescriptor(string MediaType, string Digest, long Size);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AcrUploadJson.UploadTokenRequest))]
[JsonSerializable(typeof(AcrUploadJson.UploadTokenResponse))]
[JsonSerializable(typeof(AcrUploadJson.DeployRequest))]
[JsonSerializable(typeof(AcrUploadJson.DeployResult))]
[JsonSerializable(typeof(AcrUploadJson.OciManifest))]
internal partial class AcrUploadJsonContext : JsonSerializerContext;
