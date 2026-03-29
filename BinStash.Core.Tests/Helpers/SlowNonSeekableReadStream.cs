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

namespace BinStash.Core.Tests.Helpers;

public sealed class SlowNonSeekableReadStream : Stream
{
    private readonly Stream _inner;
    private readonly int _maxBytesPerRead;
    private readonly int _delayPerReadMs;

    public SlowNonSeekableReadStream(byte[] data, int maxBytesPerRead = 1024, int delayPerReadMs = 0)
        : this(new MemoryStream(data, writable: false), maxBytesPerRead, delayPerReadMs)
    {
    }

    public SlowNonSeekableReadStream(Stream inner, int maxBytesPerRead = 1024, int delayPerReadMs = 0)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (!inner.CanRead)
            throw new ArgumentException("Inner stream must be readable.", nameof(inner));
        if (maxBytesPerRead <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytesPerRead));
        if (delayPerReadMs < 0)
            throw new ArgumentOutOfRangeException(nameof(delayPerReadMs));

        _maxBytesPerRead = maxBytesPerRead;
        _delayPerReadMs = delayPerReadMs;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_delayPerReadMs > 0)
            Thread.Sleep(_delayPerReadMs);

        return _inner.Read(buffer, offset, Math.Min(count, _maxBytesPerRead));
    }

    public override int Read(Span<byte> buffer)
    {
        if (_delayPerReadMs > 0)
            Thread.Sleep(_delayPerReadMs);

        var temp = buffer[..Math.Min(buffer.Length, _maxBytesPerRead)];
        return _inner.Read(temp);
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_delayPerReadMs > 0)
            await Task.Delay(_delayPerReadMs, cancellationToken);

        return await _inner.ReadAsync(buffer.AsMemory(offset, Math.Min(count, _maxBytesPerRead)), cancellationToken);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_delayPerReadMs > 0)
            await Task.Delay(_delayPerReadMs, cancellationToken);

        return await _inner.ReadAsync(buffer[..Math.Min(buffer.Length, _maxBytesPerRead)], cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    public override void Write(ReadOnlySpan<byte> buffer)
        => throw new NotSupportedException();

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        await base.DisposeAsync();
    }
}