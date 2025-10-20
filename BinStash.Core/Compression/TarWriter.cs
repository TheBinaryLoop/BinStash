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

using System.Text;

namespace BinStash.Core.Compression;

public sealed class TarWriter : IDisposable, IAsyncDisposable
{
    private readonly Stream _BaseStream;
    private bool _Disposed;

    public TarWriter(Stream baseStream)
    {
        _BaseStream = baseStream;
    }

    public async Task WriteFileAsync(string name, byte[] data, DateTime? lastModifiedUtc = null)
    {
        var header = CreateTarHeader(name, data.Length, lastModifiedUtc ?? DateTime.UtcNow);
        await _BaseStream.WriteAsync(header);
        await _BaseStream.WriteAsync(data);

        await WritePaddingAsync(data.Length);
    }
    
    public async Task WriteFileAsync(string name, Func<Stream, Task> writeCallback, long length, DateTime? lastModifiedUtc = null)
    {
        var header = CreateTarHeader(name, length, lastModifiedUtc ?? DateTime.UtcNow);
        await _BaseStream.WriteAsync(header);
        await writeCallback(_BaseStream);
        await WritePaddingAsync(length);
    }

    private async Task WritePaddingAsync(long length)
    {
        var padding = 512 - (length % 512);
        if (padding != 512)
            await _BaseStream.WriteAsync(new byte[padding]);
    }

    private static byte[] CreateTarHeader(string fullName, long size, DateTime lastModifiedUtc)
    {
        var buffer = new byte[512];

        void WriteOctal(long value, int offset, int length)
        {
            var str = Convert.ToString(value, 8).PadLeft(length - 1, '0') + '\0';
            Encoding.ASCII.GetBytes(str).CopyTo(buffer, offset);
        }

        void WriteString(string value, int offset, int length)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            Array.Clear(buffer, offset, length);
            Array.Copy(bytes, 0, buffer, offset, Math.Min(bytes.Length, length));
        }

        // Split into name and prefix if needed
        string name = fullName;
        string? prefix = null;

        if (Encoding.ASCII.GetByteCount(fullName) > 100)
        {
            var parts = fullName.Replace('\\', '/').Split('/');
            for (int i = parts.Length - 1; i > 0; i--)
            {
                var candidateName = string.Join('/', parts.Skip(i));
                var candidatePrefix = string.Join('/', parts.Take(i));

                if (Encoding.ASCII.GetByteCount(candidateName) <= 100 &&
                    Encoding.ASCII.GetByteCount(candidatePrefix) <= 155)
                {
                    name = candidateName;
                    prefix = candidatePrefix;
                    break;
                }
            }

            if (Encoding.ASCII.GetByteCount(name) > 100)
                throw new ArgumentException("File name too long and cannot be split into name and prefix");
        }

        WriteString(name, 0, 100);
        WriteOctal(0, 100, 8); // mode
        WriteOctal(0, 108, 8); // uid
        WriteOctal(0, 116, 8); // gid
        WriteOctal(size, 124, 12);
        WriteOctal((long)(lastModifiedUtc - new DateTime(1970, 1, 1)).TotalSeconds, 136, 12);

        for (int i = 148; i < 156; i++) buffer[i] = (byte)' ';
        buffer[156] = (byte)'0'; // typeflag = normal file

        WriteString("ustar", 257, 6); // magic
        WriteString("00", 263, 2);    // version
        WriteString("binstash", 265, 32); // uname (optional)
        WriteString("binstash", 297, 32); // gname (optional)

        if (prefix != null)
            WriteString(prefix, 345, 155);

        var checksum = buffer.Sum(b => b);
        WriteOctal(checksum, 148, 8);

        return buffer;
    }

    public void Dispose()
    {
        if (_Disposed) return;
        _Disposed = true;
        _BaseStream.Write(new byte[1024]); // 2 empty 512-byte blocks
    }

    public async ValueTask DisposeAsync()
    {
        if (_Disposed) return;
        _Disposed = true;
        await _BaseStream.WriteAsync(new byte[1024]);
    }
}
