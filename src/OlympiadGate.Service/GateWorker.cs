using OlympiadGate.Core;

namespace OlympiadGate.Service;

public sealed class GateWorker : BackgroundService
{
    private readonly GateEndpoints _endpoints;
    private readonly SessionGuard _guard;
    private readonly FileLog _log;

    public GateWorker(GateEndpoints endpoints, SessionGuard guard, FileLog log)
    {
        _endpoints = endpoints;
        _guard = guard;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            DataDirectory.EnsurePrivate();
        }
        catch (Exception ex)
        {
            _log.Error("Не удалось ограничить доступ к каталогу данных", ex);
        }

        _log.Info("Служба OlympiadGate запущена.");
        var pipeTask = new PipeServer(_endpoints, _log).RunAsync(stoppingToken);
        try
        {
            _guard.Tick();
        }
        catch (Exception ex)
        {
            _log.Error("Проверка сессий", ex);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    _guard.Tick();
                }
                catch (Exception ex)
                {
                    _log.Error("Проверка сессий", ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Остановка службы.
        }

        try
        {
            await pipeTask;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
