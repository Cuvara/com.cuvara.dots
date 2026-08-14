namespace Cuvara.DOTS.Messaging
{
    /// <summary>
    /// A chunk release was attempted. <see cref="Released"/> is false when it was refused because
    /// views were still live against the chunk's keys.
    /// </summary>
    /// <remarks>
    /// The refusal is published rather than only returned, because the caller that unloads a chunk
    /// is often not the one that owns the entities holding it open.
    /// </remarks>
    public readonly struct ChunkReleased
    {
        public readonly string ChunkId;
        public readonly bool Released;

        /// <summary>Live views still linked against the chunk's keys; 0 on a successful release.</summary>
        public readonly int LiveViewCount;

        public ChunkReleased(string chunkId, bool released, int liveViewCount)
        {
            ChunkId = chunkId;
            Released = released;
            LiveViewCount = liveViewCount;
        }
    }
}
