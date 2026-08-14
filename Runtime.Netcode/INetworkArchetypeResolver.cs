namespace Cuvara.DOTS.Netcode
{
    /// <summary>
    /// Decides which <see cref="Cuvara.DOTS.Configuration.ViewArchetypeLibrary"/> archetype a
    /// replicated id should be presented as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This interface exists because of a gap in the wire seam, not because indirection is
    /// nice.</b> <c>Cuvara.Netcode.View.IEntityView.Spawn</c> takes <c>(string id, bool isLocal)</c>
    /// and nothing else. The snapshot does carry a type — <c>ResolvedEntity.Type</c> — but
    /// <c>WorldViewBinder</c> does not forward it, and the interface is three methods on purpose and
    /// is not ours to widen. So the id is the only signal an <see cref="IEntityView"/> has, and the
    /// reference implementation reacted by hardcoding <c>id.StartsWith("enemy-")</c>.
    /// </para>
    /// <para>
    /// Naming the decision instead of hardcoding it moves one game's convention out of the package
    /// and leaves the package with a seam that a later netcode release can fill properly: when the
    /// binder does forward <c>Type</c>, this interface is where that plugs in and the id-prefix
    /// implementation is what gets deleted.
    /// </para>
    /// </remarks>
    public interface INetworkArchetypeResolver
    {
        /// <summary>
        /// Names the archetype for a replicated id, as authored in the
        /// <see cref="Cuvara.DOTS.Configuration.ViewArchetypeLibrary"/>.
        /// </summary>
        /// <returns>
        /// False when this id should not be presented at all. The adapter then skips the spawn
        /// entirely rather than inventing a fallback archetype — an entity nobody configured a look
        /// for is better invisible than rendered as something it is not.
        /// </returns>
        bool TryResolve(string id, bool isLocal, out string archetypeName);
    }
}
