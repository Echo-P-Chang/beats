namespace Beats.Production.Contracts.Media;

public sealed record SceneArtifact(
    string SceneId,
    ArtifactReference Artifact,
    int Order);
