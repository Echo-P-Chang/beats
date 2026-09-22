namespace Beats.Production.Contracts.Productions;

public sealed record StartProductionResponse(
    Guid ProductionId,
    Guid EventId,
    string Status);
