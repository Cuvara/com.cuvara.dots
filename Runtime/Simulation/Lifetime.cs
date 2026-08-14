using Unity.Entities;

namespace Cuvara.DOTS.Simulation
{
    /// <summary>
    /// Countdown to destruction. The entity is destroyed once <see cref="Remaining"/> reaches zero.
    /// </summary>
    /// <remarks>
    /// Carrying this component is the opt-in: nothing gives an entity a lifetime implicitly, and
    /// removing it stops the countdown without the entity ever being destroyed. The destruction is
    /// recorded into the package's own command buffer rather than applied inline — see
    /// <see cref="LifetimeSystem"/>.
    /// </remarks>
    public struct Lifetime : IComponentData
    {
        /// <summary>Seconds left. Counted down by <see cref="LifetimeSystem"/> in simulation time.</summary>
        public float Remaining;
    }
}
