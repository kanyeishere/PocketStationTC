using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace PocketStation.Web;

public sealed class WebSocketConnection : IDisposable
{
    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private bool disposed;

    public Guid Id { get; } = Guid.NewGuid();

    public WebSocketConnection(TcpClient client)
    {
        this.client = client;
        stream = client.GetStream();
    }

    public async Task RunAsync(Func<string, Task> onText, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && client.Connected)
        {
            var frame = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            if (frame == null)
                break;

            switch (frame.Opcode)
            {
                case 0x1:
                    await onText(Encoding.UTF8.GetString(frame.Payload)).ConfigureAwait(false);
                    break;
                case 0x8:
                    await SendFrameAsync(0x8, frame.Payload, cancellationToken).ConfigureAwait(false);
                    return;
                case 0x9:
                    await SendFrameAsync(0xA, frame.Payload, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    public Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        return SendFrameAsync(0x1, Encoding.UTF8.GetBytes(text), cancellationToken);
    }

    public Task SendBinaryAsync(byte[] data, CancellationToken cancellationToken)
    {
        return SendFrameAsync(0x2, data, cancellationToken);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        sendLock.Dispose();
        stream.Dispose();
        client.Dispose();
    }

    private async Task<WebSocketFrame?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        var header = await ReadExactAsync(2, cancellationToken).ConfigureAwait(false);
        if (header == null)
            return null;

        var opcode = header[0] & 0x0F;
        var masked = (header[1] & 0x80) != 0;
        ulong length = (ulong)(header[1] & 0x7F);

        if (length == 126)
        {
            var ext = await ReadExactAsync(2, cancellationToken).ConfigureAwait(false);
            if (ext == null)
                return null;
            length = BinaryPrimitives.ReadUInt16BigEndian(ext);
        }
        else if (length == 127)
        {
            var ext = await ReadExactAsync(8, cancellationToken).ConfigureAwait(false);
            if (ext == null)
                return null;
            length = BinaryPrimitives.ReadUInt64BigEndian(ext);
        }

        if (length > 1_048_576)
            throw new InvalidOperationException("WebSocket frame is too large.");

        byte[]? mask = null;
        if (masked)
        {
            mask = await ReadExactAsync(4, cancellationToken).ConfigureAwait(false);
            if (mask == null)
                return null;
        }

        var payload = length == 0
            ? []
            : await ReadExactAsync((int)length, cancellationToken).ConfigureAwait(false);

        if (payload == null)
            return null;

        if (mask != null)
        {
            for (var i = 0; i < payload.Length; i++)
                payload[i] = (byte)(payload[i] ^ mask[i % 4]);
        }

        return new WebSocketFrame(opcode, payload);
    }

    private async Task SendFrameAsync(byte opcode, byte[] payload, CancellationToken cancellationToken)
    {
        await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var headerLength = payload.Length switch
            {
                < 126 => 2,
                <= ushort.MaxValue => 4,
                _ => 10
            };

            var header = new byte[headerLength];
            header[0] = (byte)(0x80 | opcode);

            if (payload.Length < 126)
            {
                header[1] = (byte)payload.Length;
            }
            else if (payload.Length <= ushort.MaxValue)
            {
                header[1] = 126;
                BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), (ushort)payload.Length);
            }
            else
            {
                header[1] = 127;
                BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(2), (ulong)payload.Length);
            }

            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            if (payload.Length > 0)
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private async Task<byte[]?> ReadExactAsync(int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return null;

            offset += read;
        }

        return buffer;
    }

    private sealed record WebSocketFrame(int Opcode, byte[] Payload);
}
