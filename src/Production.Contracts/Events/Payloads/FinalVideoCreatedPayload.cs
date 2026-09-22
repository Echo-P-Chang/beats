using Beats.Production.Contracts.Media;

namespace Beats.Production.Contracts.Events.Payloads;

public sealed record FinalVideoCreatedPayload(
    ArtifactReference Video,
    ArtifactReference? VoiceOver);
