using Beats.Production.Contracts.Media;

namespace Beats.Production.Contracts.Events.Payloads;

public sealed record SceneAnimationsCreatedPayload(
    IReadOnlyList<SceneArtifact> Animations);
