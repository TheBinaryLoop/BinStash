// Copyright (C) 2025  Lukas Eßmann
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

using System.IO.Compression;
using BinStash.Core.Extensions;
using MessagePack;
using ZstdNet;

namespace BinStash.Server.Helpers;

public static class AdvancedMessagePackSerializer
{
    public static async Task<T> DeserializeAsync<T>(string contentType, Stream inputStream, MessagePackSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        return contentType switch
        {
            "application/x-msgpack+zst" => MessagePackSerializer.Deserialize<T>(new Decompressor().Unwrap(await inputStream.ToByteArrayAsync()), options, cancellationToken),
            "application/x-msgpack+gzip" => MessagePackSerializer.Deserialize<T>(new GZipStream(inputStream, CompressionMode.Decompress), options, cancellationToken),
            "application/x-msgpack" => MessagePackSerializer.Deserialize<T>(inputStream, options, cancellationToken),
            _ => throw new NotSupportedException()
        };
    }
}