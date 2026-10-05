using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OlympiadGate.Core;

public static class GateJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class RpcRequest
{
    public string Op { get; set; } = "";
    public JsonElement Payload { get; set; }
}

public sealed class RpcResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public JsonElement? Result { get; set; }
}

public static class PipeFrames
{
    public const int MaxBytes = 8_000_000;

    public static async Task WriteAsync(Stream stream, string json, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(json);
        if (body.Length > MaxBytes)
            throw new GateException("Сообщение слишком большое.");

        var header = BitConverter.GetBytes(body.Length);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(header);

        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<string> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await ReadExactAsync(stream, header, cancellationToken);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(header);

        var length = BitConverter.ToInt32(header, 0);
        if (length < 2 || length > MaxBytes)
            throw new GateException("Служба прислала ответ неверного размера.");

        var body = new byte[length];
        await ReadExactAsync(stream, body, cancellationToken);
        return Encoding.UTF8.GetString(body);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
                throw new GateException("Служба закрыла соединение.");
            offset += read;
        }
    }
}
