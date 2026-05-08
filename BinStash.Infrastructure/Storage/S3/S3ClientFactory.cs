// Copyright (C) 2025-2026  Lukas Eßmann
// 
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published
//     by the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
// 
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
// 
//     You should have received a copy of the GNU Affero General Public License
//     along with this program.  If not, see <https://www.gnu.org/licenses/>.

using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using BinStash.Core.Entities;

namespace BinStash.Infrastructure.Storage.S3;

/// <summary>
/// Creates <see cref="IAmazonS3"/> instances from <see cref="S3BackendSettings"/>.
/// Supports AWS S3, MinIO, Cloudflare R2, Backblaze B2, and any other S3-compatible provider.
/// </summary>
internal static class S3ClientFactory
{
    /// <summary>
    /// Creates a configured <see cref="IAmazonS3"/> client from the provided settings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see cref="S3BackendSettings.ServiceUrl"/> is set, the client targets that endpoint
    /// (suitable for MinIO, Cloudflare R2, etc.). Otherwise the AWS S3 service for
    /// <see cref="S3BackendSettings.Region"/> is used.
    /// </para>
    /// <para>
    /// When <see cref="S3BackendSettings.AccessKeyId"/> and <see cref="S3BackendSettings.SecretAccessKey"/>
    /// are both non-null, explicit credentials are used. Otherwise the AWS default credential chain
    /// (IAM role, environment variables, shared credentials file) is used.
    /// </para>
    /// </remarks>
    public static IAmazonS3 Create(S3BackendSettings settings)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = settings.ForcePathStyle,
        };

        if (settings.ServiceUrl is not null)
        {
            config.ServiceURL = settings.ServiceUrl;

            // When a custom endpoint is used, the region may still be needed for request signing.
            // For example, Cloudflare R2 requires "auto" or a specific region for auth.
            if (settings.Region is not null)
                config.AuthenticationRegion = settings.Region;
        }
        else if (settings.Region is not null)
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region);
        }

        if (settings.AccessKeyId is not null && settings.SecretAccessKey is not null)
        {
            var credentials = new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey);
            return new AmazonS3Client(credentials, config);
        }

        // Fall back to AWS default credential chain (IAM role, AWS_ACCESS_KEY_ID env var, shared credentials file).
        return new AmazonS3Client(config);
    }

    /// <summary>
    /// Creates an <see cref="IAmazonS3"/> client with a custom <see cref="System.Net.Http.DelegatingHandler"/>
    /// injected into the HTTP pipeline. Useful for integration tests that need to intercept requests.
    /// </summary>
    internal static IAmazonS3 CreateWithHandler(S3BackendSettings settings, System.Net.Http.DelegatingHandler handler)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = settings.ForcePathStyle,
        };

        if (settings.ServiceUrl is not null)
        {
            config.ServiceURL = settings.ServiceUrl;
            if (settings.Region is not null)
                config.AuthenticationRegion = settings.Region;
        }
        else if (settings.Region is not null)
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region);
        }

        // DelegatingHandler requires an inner handler; set one if it hasn't been assigned yet.
        if (handler.InnerHandler is null)
            handler.InnerHandler = new System.Net.Http.HttpClientHandler();

        var httpClient = new System.Net.Http.HttpClient(handler);
        var httpClientFactory = new AmazonS3HttpClientFactory(httpClient);
        config.HttpClientFactory = httpClientFactory;

        if (settings.AccessKeyId is not null && settings.SecretAccessKey is not null)
        {
            var credentials = new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey);
            return new AmazonS3Client(credentials, config);
        }

        return new AmazonS3Client(config);
    }
}

/// <summary>
/// An <see cref="Amazon.Runtime.HttpClientFactory"/> that returns a pre-configured <see cref="System.Net.Http.HttpClient"/>.
/// Used by integration tests to inject a delegating handler into the AWS SDK pipeline.
/// </summary>
internal sealed class AmazonS3HttpClientFactory : Amazon.Runtime.HttpClientFactory
{
    private readonly System.Net.Http.HttpClient _client;

    public AmazonS3HttpClientFactory(System.Net.Http.HttpClient client)
    {
        _client = client;
    }

    public override System.Net.Http.HttpClient CreateHttpClient(IClientConfig clientConfig) => _client;
}
