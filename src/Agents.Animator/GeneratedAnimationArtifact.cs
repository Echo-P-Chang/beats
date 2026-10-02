namespace Beats.Agents.Animator;

public sealed record GeneratedAnimationArtifact(
    string FileName,
    string MediaType,
    string Description,
    byte[] Content);
