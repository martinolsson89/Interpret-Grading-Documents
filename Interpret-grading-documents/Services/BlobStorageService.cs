namespace Interpret_grading_documents.Services;

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Text;
using System.Text.Json;

public class BlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;

    public BlobStorageService(BlobServiceClient blobServiceClient)
    {
        _blobServiceClient = blobServiceClient ?? throw new ArgumentNullException(nameof(blobServiceClient));
    }

    public Uri GetBlobUri(string containerName, string blobName)
    {
        var container = _blobServiceClient.GetBlobContainerClient(containerName.Trim().ToLowerInvariant());
        return container.GetBlobClient(blobName).Uri;
    }

    private async Task<BlobContainerClient> GetContainerAsync(string containerName, CancellationToken ct = default)
    {
        // Azure requires lowercase, letters/numbers/hyphens for container names
        var safe = containerName.Trim().ToLowerInvariant();
        var container = _blobServiceClient.GetBlobContainerClient(safe);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        return container;
    }

    public async Task UploadAsync(
        string containerName,
        string blobName,
        Stream fileStream,
        string? contentType = null,
        CancellationToken ct = default)
    {
        var container = await GetContainerAsync(containerName, ct);
        var blob = container.GetBlobClient(blobName);

        var headers = new BlobHttpHeaders();
        if (!string.IsNullOrWhiteSpace(contentType))
            headers.ContentType = contentType;

        await blob.UploadAsync(fileStream, new BlobUploadOptions { HttpHeaders = headers }, ct);
    }

    public async Task<Stream> DownloadAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        var container = await GetContainerAsync(containerName, ct);
        var blob = container.GetBlobClient(blobName);

        Response<BlobDownloadStreamingResult> result =
            await blob.DownloadStreamingAsync(cancellationToken: ct);

        var ms = new MemoryStream();
        await result.Value.Content.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }


    public async Task<bool> DeleteAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        var container = await GetContainerAsync(containerName, ct);
        var blob = container.GetBlobClient(blobName);
        var response = await blob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);
        return response.Value;
    }

    // ---------- JSON convenience ----------

    public async Task<T?> DownloadJsonAsync<T>(string containerName, string blobName, CancellationToken ct = default)
    {
        var container = await GetContainerAsync(containerName, ct);
        var blob = container.GetBlobClient(blobName);

        if (!await blob.ExistsAsync(ct)) return default;

        var download = await blob.DownloadStreamingAsync(cancellationToken: ct);
        using var sr = new StreamReader(download.Value.Content, Encoding.UTF8);
        var json = await sr.ReadToEndAsync();
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public async Task UploadJsonAsync<T>(string containerName, string blobName, T data, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        var bytes = Encoding.UTF8.GetBytes(json);
        using var ms = new MemoryStream(bytes);
        await UploadAsync(containerName, blobName, ms, "application/json", ct);
    }

    public async Task<bool> ExistsAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        var container = await GetContainerAsync(containerName, ct);
        var blob = container.GetBlobClient(blobName);
        var exists = await blob.ExistsAsync(ct);
        return exists.Value;
    }

}