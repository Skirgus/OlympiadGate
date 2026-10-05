using System.Text.Json;
using OlympiadGate.Core;

namespace OlympiadGate.Service;

public sealed record ClientContext(string Sid, bool IsElevatedAdmin);

public sealed class GateEndpoints
{
    private readonly GateCatalog _catalog;
    private readonly FileLog _log;

    public GateEndpoints(GateCatalog catalog, FileLog log)
    {
        _catalog = catalog;
        _log = log;
    }

    public JsonElement Execute(string op, JsonElement payload, ClientContext client)
    {
        try
        {
            return op switch
            {
                "listUsers" => Json(ListUsers(client)),
                "saveAccount" => Json(SaveAccount(payload, client)),
                "listProblems" => Json(Admin(client, () => _catalog.ListProblems())),
                "saveProblem" => Json(Admin(client, () => _catalog.SaveProblem(Read<SaveProblemRequest>(payload)))),
                "deleteProblem" => Json(Admin(client, () =>
                {
                    _catalog.DeleteProblem(Read<SaveProblemRequest>(payload).Id);
                    return true;
                })),
                "listDirections" => Json(Admin(client, () => _catalog.ListDirections())),
                "previewImport" => Json(Admin(client, () =>
                {
                    var request = Read<ImportRequest>(payload);
                    return _catalog.PreviewImport(request.Json, request.Defaults ?? new ImportDefaults());
                })),
                "commitImport" => Json(Admin(client, () =>
                {
                    var request = Read<ImportRequest>(payload);
                    return _catalog.CommitImport(request.Json, request.Defaults ?? new ImportDefaults(), request.UpdateDuplicates);
                })),
                "warnings" => Json(Admin(client, () => _catalog.Warnings())),
                "adviseDirection" => Json(Admin(client, () =>
                {
                    var request = Read<AdviseRequest>(payload);
                    return _catalog.Advise(request.Sid, request.Direction, request.Grade, request.Olympiad);
                })),
                "unlockToday" => Json(Admin(client, () =>
                {
                    var sid = Read<SidRequest>(payload).Sid;
                    _catalog.UnlockForToday(sid);
                    LockPolicies.Clear(sid);
                    return true;
                })),
                "clearProgress" => Json(Admin(client, () =>
                {
                    var request = Read<ClearProgressRequest>(payload);
                    return _catalog.ClearProgress(request.Sid, request.TodayOnly);
                })),
                "lockSnapshot" => Json(Snapshot(client)),
                "nextProblem" => Json(Next(client)),
                "submitAnswer" => Json(Submit(payload, client)),
                _ => throw new GateException("Неизвестная команда.")
            };
        }
        catch (GateException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error("Команда " + op, ex);
            throw new GateException("Не удалось выполнить команду.");
        }
    }

    private List<AccountSettings> ListUsers(ClientContext client)
    {
        RequireAdmin(client);
        var saved = _catalog.ListAccounts().ToDictionary(account => account.Sid, StringComparer.OrdinalIgnoreCase);
        var result = new List<AccountSettings>();
        foreach (var user in LocalAccounts.List())
        {
            if (!saved.TryGetValue(user.Sid, out var account))
            {
                result.Add(new AccountSettings
                {
                    Sid = user.Sid,
                    Username = user.Name,
                    IsAdministrator = user.IsAdministrator,
                    Grade = 5,
                    DailyGoal = 3
                });
                continue;
            }

            account.Username = user.Name;
            account.IsAdministrator = user.IsAdministrator;
            if (user.IsAdministrator)
                account.LockEnabled = false;
            result.Add(_catalog.Decorate(account));
        }

        return result;
    }

    private AccountSettings SaveAccount(JsonElement payload, ClientContext client)
    {
        RequireAdmin(client);
        var account = Read<AccountSettings>(payload);
        if (LocalAccounts.IsAdministrator(account.Sid))
            account.LockEnabled = false;
        _catalog.SaveAccount(account);
        if (!account.LockEnabled)
            LockPolicies.Clear(account.Sid);
        return ListUsers(client).FirstOrDefault(item => string.Equals(item.Sid, account.Sid, StringComparison.OrdinalIgnoreCase))
            ?? account;
    }

    private LockSnapshot Snapshot(ClientContext client)
    {
        if (LocalAccounts.IsAdministrator(client.Sid))
            return new LockSnapshot();
        return _catalog.GetLockSnapshot(client.Sid);
    }

    private TaskView? Next(ClientContext client)
    {
        if (LocalAccounts.IsAdministrator(client.Sid))
            return null;
        return _catalog.NextProblem(client.Sid);
    }

    private SubmitResult Submit(JsonElement payload, ClientContext client)
    {
        if (LocalAccounts.IsAdministrator(client.Sid))
            return new SubmitResult { Unlocked = true };
        var request = Read<SubmitRequest>(payload);
        var result = _catalog.Submit(client.Sid, request.ProblemId, request.Answer);
        if (result.Unlocked)
            LockPolicies.Clear(client.Sid);
        return result;
    }

    private static T Admin<T>(ClientContext client, Func<T> action)
    {
        RequireAdmin(client);
        return action();
    }

    private static void RequireAdmin(ClientContext client)
    {
        if (!client.IsElevatedAdmin)
            throw new GateException("Нужны права администратора.");
    }

    private static T Read<T>(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new GateException("Пустой запрос.");
        return payload.Deserialize<T>(GateJson.Options) ?? throw new GateException("Пустой запрос.");
    }

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, GateJson.Options);
}
