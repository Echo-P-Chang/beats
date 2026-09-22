using Beats.Production.Contracts;
using Beats.Production.Contracts.Events;

namespace Beats.Agents.Reviewer;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "{AgentRole} started. Waiting for {EventType} events.",
            AgentRoles.Reviewer,
            EventTypes.FinalVideoCreated);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("{AgentRole} stopped.", AgentRoles.Reviewer);
        }
    }
}
