using Unity.Entities;

namespace Cuvara.DOTS.Views
{
    /// <summary>
    /// Everything the package does to turn entities into visible GameObjects, as one orderable unit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Presentation, not simulation.</b> The whole group reads <see cref="Unity.Transforms.LocalTransform"/>
    /// after the frame's simulation has written it. <c>TransformSystemGroup</c> lives at the end of
    /// <see cref="SimulationSystemGroup"/>, and <see cref="PresentationSystemGroup"/> runs after
    /// that group in the root ordering, so being here is already sufficient to read post-transform
    /// values. There is deliberately <b>no</b> <c>[UpdateAfter(typeof(TransformSystemGroup))]</c>:
    /// cross-group ordering attributes name a system in a different parent, which Entities ignores
    /// with a warning. Putting any of this in <see cref="SimulationSystemGroup"/> instead would
    /// render the previous frame's positions — the classic one-frame-stale hybrid view.
    /// </para>
    /// <para>
    /// Consumers order their own presentation systems against this group as a whole rather than
    /// naming individual package systems, so the package can add or split systems internally
    /// without breaking them.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CuvaraViewPresentationGroup : ComponentSystemGroup
    {
    }
}
