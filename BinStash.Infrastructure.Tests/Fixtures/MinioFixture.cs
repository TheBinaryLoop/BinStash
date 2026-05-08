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

using Testcontainers.Minio;

namespace BinStash.Infrastructure.Tests.Fixtures;

/// <summary>
/// xUnit fixture that starts a MinIO container for integration tests.
/// Automatically starts the container before first test and disposes it after all tests in the collection.
/// </summary>
public sealed class MinioFixture : IAsyncLifetime
{
    private readonly MinioContainer _container = new MinioBuilder()
        .WithImage("quay.io/minio/minio:latest")
        .Build();

    /// <summary>Access key for connecting to the MinIO container.</summary>
    public string AccessKey => _container.GetAccessKey();

    /// <summary>Secret key for connecting to the MinIO container.</summary>
    public string SecretKey => _container.GetSecretKey();

    /// <summary>The HTTP endpoint URL for connecting to the MinIO container.</summary>
    public string Endpoint => _container.GetConnectionString();

    /// <summary>Default bucket name created in <see cref="InitializeAsync"/>.</summary>
    public const string DefaultBucket = "binstash-test";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
