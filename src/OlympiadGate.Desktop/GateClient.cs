using System.IO.Pipes;
using System.Text.Json;
using OlympiadGate.Core;

namespace OlympiadGate.Desktop;

public sealed class GateClient
{
    public async Task<T?> CallAsync<T>(string op, object? payload = null, CancellationToken cancellationToken = default)
    {
        using var pipe = new NamedPipeClientStream(".", AppPaths.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await pipe.ConnectAsync(timeout.Token);
            var request = new RpcRequest
            {
                Op = op,
                Payload = JsonSerializer.SerializeToElement(payload ?? new { }, GateJson.Options)
            };
            await PipeFrames.WriteAsync(pipe, JsonSerializer.Serialize(request, GateJson.Options), timeout.Token);
            var responseJson = await PipeFrames.ReadAsync(pipe, timeout.Token);
            var response = JsonSerializer.Deserialize<RpcResponse>(responseJson, GateJson.Options)
                ?? throw new GateException("Пустой ответ службы.");
            if (!response.Ok)
                throw new GateException(string.IsNullOrWhiteSpace(response.Error) ? "Ошибка службы." : response.Error);
            if (response.Result is not { } result || result.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                return default;
            return result.Deserialize<T>(GateJson.Options);
        }
        catch (OperationCanceledException)
        {
            throw new GateException("Служба OlympiadGate не ответила. Закройте лишние копии службы и запустите её снова.");
        }
        catch (GateException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new GateException("Служба OlympiadGate не запущена. Установите программу или запустите службу.");
        }
    }
}
