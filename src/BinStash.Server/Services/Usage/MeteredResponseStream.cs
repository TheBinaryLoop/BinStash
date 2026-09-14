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

namespace BinStash.Server.Services.Usage;

/// <summary>
/// Counts bytes on their way to the response and reports them to the meter in increments.
/// </summary>
/// <remarks>
/// The increment is the point. A release download can run for minutes, and reporting egress only
/// on completion means a client that disconnects — or a process that is recycled — is billed
/// nothing for bandwidth already spent. Reporting every <paramref name="checkpointBytes"/> bounds
/// that loss to one checkpoint.
///
/// <para>
/// The invariant callers rely on: every byte written is reported exactly once, across the
/// checkpoints plus the final <see cref="TakeUnmeteredBytes"/>. Never twice, never dropped.
/// </para>
///
/// <para>
/// <paramref name="onCheckpoint"/> is raised from inside the write, so it must be cheap and must
/// not throw — the caller is responsible for wrapping a meter that might.
/// </para>
/// </remarks>
internal sealed class MeteredResponseStream(Stream inner, long checkpointBytes, Action<long> onCheckpoint) : Stream
{
    private long _unmetered;

    /// <summary>Total bytes written to the underlying stream.</summary>
    public long BytesWritten { get; private set; }

    /// <summary>
    /// Returns the bytes written since the last checkpoint and clears them, so the caller reports
    /// the remainder exactly once when the stream is done. Safe to call more than once; the
    /// second call returns zero.
    /// </summary>
    public long TakeUnmeteredBytes()
    {
        var pending = _unmetered;
        _unmetered = 0;
        return pending;
    }

    private void Count(long bytes)
    {
        BytesWritten += bytes;
        _unmetered += bytes;

        // A non-positive checkpoint means "report once, at the end" — the remainder is then the
        // whole stream and TakeUnmeteredBytes delivers it.
        if (checkpointBytes <= 0 || _unmetered < checkpointBytes)
            return;

        var delta = _unmetered;
        _unmetered = 0;
        onCheckpoint(delta);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) { inner.Write(buffer, offset, count); Count(count); }
    public override void Write(ReadOnlySpan<byte> buffer) { inner.Write(buffer); Count(buffer.Length); }
    public override void WriteByte(byte value) { inner.WriteByte(value); Count(1); }
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) { await inner.WriteAsync(buffer, offset, count, ct); Count(count); }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) { await inner.WriteAsync(buffer, ct); Count(buffer.Length); }
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
