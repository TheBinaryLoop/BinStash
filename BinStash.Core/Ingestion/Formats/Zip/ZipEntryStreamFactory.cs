// Copyright (C) 2025-2026  Lukas Eßmann
// 
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
// 
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
// 
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.IO.Compression;

namespace BinStash.Core.Ingestion.Formats.Zip;

public sealed class ZipEntryStreamFactory
{
    public Func<Stream> Create(string zipFilePath, string entryFullName)
    {
        return () => new ZipEntryReadStream(zipFilePath, entryFullName);
    }

    private sealed class ZipEntryReadStream : Stream
    {
        private readonly FileStream _fileStream;
        private readonly ZipArchive _archive;
        private readonly Stream _entryStream;

        public ZipEntryReadStream(string zipFilePath, string entryFullName)
        {
            _fileStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);

            _archive = new ZipArchive(_fileStream, ZipArchiveMode.Read, leaveOpen: false);

            var entry = _archive.GetEntry(entryFullName);
            if (entry == null)
                throw new FileNotFoundException($"ZIP entry '{entryFullName}' was not found in '{zipFilePath}'.");

            _entryStream = entry.Open();
        }

        public override bool CanRead => _entryStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _entryStream.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _entryStream.Read(buffer);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _entryStream.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _entryStream.ReadAsync(buffer, offset, count, cancellationToken);

        public override void Flush() => _entryStream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _entryStream.Dispose();
                _archive.Dispose();
                _fileStream.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _entryStream.DisposeAsync();
            _archive.Dispose();
            await _fileStream.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}