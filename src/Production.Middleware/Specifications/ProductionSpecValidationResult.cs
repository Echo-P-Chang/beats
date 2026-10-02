using Beats.Production.Contracts.Specifications;

namespace Beats.Production.Middleware.Specifications;

public sealed record ProductionSpecValidationResult(
    bool IsValid,
    ProductionSpec Spec,
    StageSpec Stage,
    IReadOnlyList<string> Errors);
