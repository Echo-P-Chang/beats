namespace Beats.Production.Contracts.Media;

public sealed record ArtifactReference(
    string Uri,
    string MediaType,
    string? Description = null);
