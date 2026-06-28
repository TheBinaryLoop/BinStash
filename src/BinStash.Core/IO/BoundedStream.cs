namespace BinStash.Core.IO;

sealed class BoundedStream : Stream
{
    private readonly Stream _base;
    private readonly long _length;
    private long _position;
    private long _remaining;

    public BoundedStream(Stream @base, long length)
    {
        _base = @base;
        _length = length;
        _remaining = length;
    }
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_remaining <= 0) return 0;
        if (count > _remaining) count = (int)_remaining;
        var read = _base.Read(buffer, offset, count);
        _remaining -= read;
        _position += read;
        return read;
    }
    public override int Read(Span<byte> buffer)
    {
        if (_remaining <= 0) return 0;
        if (buffer.Length > _remaining) buffer = buffer[..(int)_remaining];
        var read = _base.Read(buffer);
        _remaining -= read;
        _position += read;
        return read;
    }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();
    
    public override void SetLength(long value)
        => throw new NotSupportedException();
    
    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();
}
