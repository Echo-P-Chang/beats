using Beats.Production.Contracts.Media;

namespace Beats.Production.Contracts.Events.Payloads;

public sealed record StoryCreatedPayload(
    ArtifactReference Story,
    ArtifactReference Scenes);
