namespace Beats.Agents.Animator;

public interface IAnimatorAdapter
{
    Task<AnimationPlanResult> CreateAnimationPlanAsync(
        AnimationPlanRequest request,
        CancellationToken cancellationToken = default);
}
