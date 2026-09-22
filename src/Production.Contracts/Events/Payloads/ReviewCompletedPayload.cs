using Beats.Production.Contracts.Media;

namespace Beats.Production.Contracts.Events.Payloads;

public sealed record ReviewCompletedPayload(
    bool Passed,
    ArtifactReference Report,
    IReadOnlyList<string> Findings);
