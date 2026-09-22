using Beats.Production.Contracts;
using Beats.Production.Flows;

namespace Beats.Agents.Animator;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IProductionFlow _productionFlow;

    public Worker(ILogger<Worker> logger, IProductionFlow productionFlow)
    {
        _logger = logger;
        _productionFlow = productionFlow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriptions = string.Join(
            ", ",
            _productionFlow.GetSubscriptions(AgentRoles.Animator).Select(subscription => subscription.EventType));

        _logger.LogInformation(
            "{AgentRole} started. Waiting for {EventTypes} events.",
            AgentRoles.Animator,
            subscriptions);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("{AgentRole} stopped.", AgentRoles.Animator);
        }
    }
}
