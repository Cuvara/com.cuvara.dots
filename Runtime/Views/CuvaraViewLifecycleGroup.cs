using Unity.Entities;

namespace Cuvara.DOTS.Views
{
    /// <summary>
    /// Structural half of the view layer: views appear and disappear here, and nowhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why presentation and not <see cref="InitializationSystemGroup"/>.</b> Both spawn and
    /// despawn are structural changes, so they force a sync point wherever they run. Running them
    /// here — after the frame's simulation has already finished — means the sync point lands where
    /// there is no simulation work left to stall. It also keeps view lifetime in step with the
    /// frame that caused it: an entity created during simulation gets its view in the same frame's
    /// presentation, and an entity destroyed during simulation loses its view in the same frame.
    /// Deferring to the next frame's initialization would show a dead entity for one extra frame
    /// and delay every new view by one, which is exactly the artefact a hybrid view is blamed for.
    /// </para>
    /// <para>
    /// A separate group from <see cref="CuvaraViewTransformSyncGroup"/> so a consumer can inject
    /// work between "views exist" and "views are positioned" without naming a package system.
    /// </para>
    /// </remarks>
    // Ordered by an explicit UpdateAfter on CuvaraViewTransformSyncGroup rather than OrderFirst
    // here: Entities sorts OrderFirst systems into their own batch and then ignores any
    // Update*/After relation between that batch and a normal system, with only a warning.
    [UpdateInGroup(typeof(CuvaraViewPresentationGroup))]
    public partial class CuvaraViewLifecycleGroup : ComponentSystemGroup
    {
    }
}
