using System.Text;

namespace Callrift.Core;

internal sealed class Utf8TextWriterStream(TextWriter output) : Stream
{
    private readonly Decoder decoder = Encoding.UTF8.GetDecoder();
    private readonly char[] characters = new char[4096];

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() => output.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => output.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            decoder.Convert(buffer, characters, false, out var bytesUsed, out var charsUsed, out _);
            output.Write(characters, 0, charsUsed);
            buffer = buffer[bytesUsed..];
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (!buffer.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            decoder.Convert(buffer.Span, characters, false, out var bytesUsed, out var charsUsed, out _);
            await output.WriteAsync(characters.AsMemory(0, charsUsed), cancellationToken);
            buffer = buffer[bytesUsed..];
        }
    }
}
