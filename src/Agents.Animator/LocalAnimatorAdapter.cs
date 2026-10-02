using System.Text;

namespace Beats.Agents.Animator;

public sealed class LocalAnimatorAdapter(ILogger<LocalAnimatorAdapter> logger) : IAnimatorAdapter
{
    public Task<AnimationPlanResult> CreateAnimationPlanAsync(
        AnimationPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Creating local animation plan placeholder. ProductionId={ProductionId}, ArtifactCount={ArtifactCount}, ImageCount={ImageCount}",
            request.ProductionId,
            request.Artifacts.Count,
            request.ImageArtifacts.Count);

        var manuscript = new StringBuilder();

        manuscript.AppendLine($"# Production {request.ProductionId}");
        manuscript.AppendLine();
        manuscript.AppendLine("## Animator");
        manuscript.AppendLine("Local animation execution report:");
        manuscript.AppendLine("The animator worker is now an adapter boundary. This local placeholder reads the storyteller scene breakdown and animation plan, then reports the intended video work.");
        manuscript.AppendLine();
        manuscript.AppendLine($"Scene breakdown loaded: {request.SceneBreakdown is not null}");
        manuscript.AppendLine($"Animation plan loaded: {request.AnimationPlan is not null}");
        manuscript.AppendLine();

        if (request.AnimationPlan is not null)
        {
            manuscript.AppendLine("Planned shots:");

            foreach (var shot in request.AnimationPlan.Shots.OrderBy(shot => shot.Order))
            {
                manuscript.AppendLine($"- {shot.SceneId}: {shot.Title}, {shot.DurationSeconds}s");
                manuscript.AppendLine($"  Motion: {shot.Motion}");
                manuscript.AppendLine($"  Camera: {shot.Camera}");
                manuscript.AppendLine($"  Transition: {shot.Transition}");
            }

            manuscript.AppendLine();
        }

        manuscript.AppendLine("Input artifact metadata:");

        foreach (var artifact in request.Artifacts)
        {
            manuscript.AppendLine($"- {artifact.MediaType}: {artifact.Uri}");
            if (!string.IsNullOrWhiteSpace(artifact.Description))
            {
                manuscript.AppendLine($"  Description: {artifact.Description}");
            }
        }

        manuscript.AppendLine();
        manuscript.AppendLine("Scene animation placeholders:");

        for (var index = 0; index < request.ImageArtifacts.Count; index++)
        {
            var image = request.ImageArtifacts[index];
            manuscript.AppendLine($"- scene-{index + 1:000}: Slow parallax push-in, gentle camera drift, soft transition, 8 seconds.");
            manuscript.AppendLine($"  Source image: {image.Uri}");
        }

        if (request.ImageArtifacts.Count == 0)
        {
            manuscript.AppendLine("- scene-001: No image artifacts were provided. Create a text-only timing plan from the available metadata.");
        }

        var result = new AnimationPlanResult(
            manuscript.ToString(),
            "Created a local placeholder animation plan from artifact metadata.",
            Math.Max(1, request.ImageArtifacts.Count),
            []);

        return Task.FromResult(result);
    }
}
