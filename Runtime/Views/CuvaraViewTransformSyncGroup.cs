using Unity.Entities;

namespace Cuvara.DOTS.Views
{
    /// <summary>
    /// Read-only half of the view layer: ECS transforms are copied onto GameObjects here.
    /// </summary>
    /// <remarks>
    /// Runs after <see cref="CuvaraViewLifecycleGroup"/> so a view spawned this frame is positioned
    /// in the same frame rather than sitting at the origin until the next one. Nothing in this
    /// group makes structural changes, which is what lets it stay a pure read plus a managed write.
    /// </remarks>
    [UpdateInGroup(typeof(CuvaraViewPresentationGroup))]
    [UpdateAfter(typeof(CuvaraViewLifecycleGroup))]
    public partial class CuvaraViewTransformSyncGroup : ComponentSystemGroup
    {
    }
}
