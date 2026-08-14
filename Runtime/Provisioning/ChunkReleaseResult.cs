namespace Cuvara.DOTS.Provisioning
{
    /// <summary>
    /// Outcome of <see cref="ChunkViewProvisioner.ReleaseChunk"/>.
    /// </summary>
    /// <remarks>
    /// A struct rather than a <c>bool</c> because there are three distinct outcomes a caller must be
    /// able to tell apart — released, never tracked, and refused because views are still live — and
    /// a bool collapses the last two into "false", which is how a streaming bug hides.
    /// </remarks>
    public readonly struct ChunkReleaseResult
    {
        /// <summary>The chunk's references were dropped.</summary>
        public readonly bool Released;

        /// <summary>The chunk was known to the provisioner when the call was made.</summary>
        public readonly bool WasTracked;

        /// <summary>
        /// Live views standing on the chunk's keys. Non-zero means the release was refused and
        /// nothing changed — despawn those entities and call again.
        /// </summary>
        public readonly int LiveViewCount;

        /// <summary>One key that was still in use, for the log line. Null when nothing blocked.</summary>
        public readonly string BlockingKey;

        public ChunkReleaseResult(bool released, bool wasTracked, int liveViewCount, string blockingKey)
        {
            Released = released;
            WasTracked = wasTracked;
            LiveViewCount = liveViewCount;
            BlockingKey = blockingKey;
        }

        /// <summary>True when the call was refused rather than being a no-op on an unknown chunk.</summary>
        public bool WasRefused => WasTracked && !Released;
    }
}
