using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using OlympiadGate.Core;

namespace OlympiadGate.Service;

public sealed class PipeServer
{
    private readonly GateEndpoints _endpoints;
    private readonly FileLog _log;

    public PipeServer(GateEndpoints endpoints, FileLog log)
    {
        _endpoints = endpoints;
        _log = log;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pipe = CreatePipe();
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                pipe.Dispose();
                break;
            }
            catch (Exception ex)
            {
                pipe.Dispose();
                _log.Error("Канал не открылся", ex);
                await Task.Delay(500, cancellationToken);
                continue;
            }

            _ = Task.Run(() => Handle(pipe));
        }
    }

    private void Handle(NamedPipeServerStream pipe)
    {
        try
        {
            var json = PipeFrames.ReadAsync(pipe, CancellationToken.None).GetAwaiter().GetResult();
            var request = JsonSerializer.Deserialize<RpcRequest>(json, GateJson.Options) ?? new RpcRequest();
            var client = Identify(pipe);
            var result = _endpoints.Execute(request.Op, request.Payload, client);
            Write(pipe, new RpcResponse { Ok = true, Result = result });
        }
        catch (Exception ex)
        {
            var message = ex is GateException gate ? gate.Message : "Не удалось выполнить команду.";
            if (ex is not GateException)
                _log.Error("Ошибка канала", ex);
            try
            {
                Write(pipe, new RpcResponse { Ok = false, Error = message });
            }
            catch (Exception writeError)
            {
                _log.Error("Не удалось отправить ошибку", writeError);
            }
        }
        finally
        {
            try
            {
                pipe.Dispose();
            }
            catch
            {
                // Соединение уже закрыто.
            }
        }
    }

    private static void Write(NamedPipeServerStream pipe, RpcResponse response)
    {
        var json = JsonSerializer.Serialize(response, GateJson.Options);
        PipeFrames.WriteAsync(pipe, json, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static ClientContext Identify(NamedPipeServerStream pipe)
    {
        string sid = "";
        var elevated = false;
        pipe.RunAsClient(() =>
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            sid = identity.User?.Value ?? "";
            elevated = new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        });
        return new ClientContext(sid, elevated);
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            AppPaths.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            65536,
            65536,
            security);
    }
}
