using System.Security.Cryptography;
using AppleMusicHistory.Core.Abstractions;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Infrastructure.Data;

namespace AppleMusicHistory.Infrastructure.Scraping;

public sealed class ArtworkCacheService : IArtworkCache
{
    private static readonly HttpClient HttpClient = new();

    public async Task<CachedArtworkResult?> CacheAsync(string artworkUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artworkUrl))
        {
            return null;
        }

        Directory.CreateDirectory(AppPaths.ArtworkDirectory);
        var extension = InferExtension(artworkUrl);
        var fileName = $"{Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(artworkUrl)))}{extension}";
        var fullPath = Path.Combine(AppPaths.ArtworkDirectory, fileName);

        if (!File.Exists(fullPath))
        {
            using var response = await HttpClient.GetAsync(artworkUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = File.Create(fullPath);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        return new CachedArtworkResult(Path.Combine("artwork", fileName), DateTimeOffset.UtcNow);
    }

    private static string InferExtension(string artworkUrl)
    {
        var extension = Path.GetExtension(new Uri(artworkUrl).AbsolutePath);
        return string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension.ToLowerInvariant();
    }
}
