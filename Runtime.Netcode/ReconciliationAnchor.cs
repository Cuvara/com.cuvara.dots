using Unity.Entities;
using Unity.Mathematics;

namespace Cuvara.DOTS.Netcode
{
    /// <summary>
    /// The last authoritative position the server reported for this entity, in world space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Named for what a predictor does with it, not for where it came from.</b> "Network
    /// position" or "server position" would describe the source and invite the obvious wrong move —
    /// someone deciding the local entity looks stale and writing this straight into
    /// <c>LocalTransform</c>. This is the value a prediction layer <i>rewinds to and replays from</i>.
    /// The name is meant to make that misuse read as wrong before anyone runs it.
    /// </para>
    /// <para>
    /// <b>It exists because two writers to one component is the failure shape this workspace keeps
    /// paying for.</b> Once prediction owns the local player's per-frame position, prediction and
    /// this adapter would both write <c>LocalTransform</c> in the same frame — the adapter first, in
    /// initialization, prediction second, in simulation. That *works*, on every frame prediction
    /// runs, and fails only on the frames it does not: the avatar snaps back to the server position
    /// for one frame. Intermittent, visible only as feel, local player only, and it presents as a
    /// prediction bug. Splitting the value out gives each component exactly one writer.
    /// </para>
    /// <para>
    /// <b>Written for every replicated entity, not only predicted ones.</b> Uniform costs one
    /// <see cref="float3"/> per mirror entity and removes a question — "why does the local one have
    /// this and remotes do not" — whose answer would have to be re-derived every time someone reads
    /// the code. Nothing consumes it for remotes today; nothing has to.
    /// </para>
    /// <para>
    /// <b>Position only. There is deliberately no tick here.</b> A reconciliation anchor is a
    /// position <i>at a tick</i>, and this adapter genuinely does not know the tick:
    /// <c>IEntityView.SetState</c> carries <c>(id, x, y, hp, maxHp)</c> and nothing else. The tick a
    /// predictor needs is <c>WorldState.AckTick</c> — "the newest input tick the server accepted for
    /// this player", which netcode already surfaces and documents as exactly this, and which a
    /// predictor reads from netcode directly. Inventing a tick here, or guessing one from arrival
    /// order, would produce a number that looks authoritative and is not.
    /// </para>
    /// </remarks>
    public struct ReconciliationAnchor : IComponentData
    {
        /// <summary>
        /// The server's position, already through <see cref="SnapshotSpaceMapping"/> — so it is in
        /// the same space as <c>LocalTransform.Position</c> and needs no further conversion.
        /// </summary>
        public float3 Position;
    }
}
