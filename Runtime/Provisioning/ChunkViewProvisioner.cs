using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cuvara.DOTS.Provisioning
{
    /// <summary>
    /// Warms and releases whole sets of view prefabs on behalf of a spatial chunk or region,
    /// reference-counting keys so chunks that share a prefab do not unload it from under each
    /// other.
    /// </summary>
    /// <remarks>
    /// <para><b>Reference-count semantics — the part that is easy to get wrong:</b></para>
    /// <list type="bullet">
    /// <item>A key is counted <b>once per chunk</b>, never once per occurrence. The key list is
    /// de-duplicated on intake, so a chunk that lists <c>goblin</c> three times still contributes
    /// exactly one reference and one release. Counting occurrences would leak: the release path
    /// walks the stored set, and the stored set holds each key once.</item>
    /// <item>The count reaching zero is the <i>only</i> trigger for
    /// <see cref="IViewAssetProvider.Release"/>. Releasing chunk A while chunk B still lists the
    /// same key decrements and nothing else.</item>
    /// <item>Releasing an unknown or already-released chunk is a no-op, not an error. The chunk is
    /// removed from the table before its keys are decremented, so a double release cannot
    /// double-decrement.</item>
    /// <item>Re-warming a chunk that is already warm is a diff, not an add: the new set is
    /// incremented <i>before</i> the old set is decremented, so a key present in both never
    /// transiently hits zero and never gets unloaded and reloaded.</item>
    /// <item>Counts are mutated synchronously, before any await. Two overlapping
    /// <see cref="PrewarmChunkAsync"/> calls therefore cannot interleave into a wrong count, even
    /// though the loads they kick off do overlap.</item>
    /// </list>
    /// <para>
    /// <b>Accepted limitation:</b> the warm count for a key only grows. If chunk A asks for 8
    /// instances and chunk B for 2, releasing A leaves 8 warm rather than shrinking to 2. Shrinking
    /// would mean destroying pooled instances that a live chunk may be about to spawn, which is the
    /// hitch prewarming exists to avoid. Memory is reclaimed when the count reaches zero.
    /// </para>
    /// <para>
    /// Not thread-safe. Call from the main thread, like everything else that touches the pool.
    /// </para>
    /// </remarks>
    public sealed class ChunkViewProvisioner
    {
        private readonly IViewAssetProvider _provider;

        /// <summary>chunk id -> the de-duplicated key set that chunk currently holds a reference on.</summary>
        private readonly Dictionary<string, HashSet<string>> _chunkKeys = new Dictionary<string, HashSet<string>>();

        /// <summary>asset key -> number of chunks referencing it.</summary>
        private readonly Dictionary<string, int> _refCounts = new Dictionary<string, int>();

        /// <summary>asset key -> highest instance count anyone has asked to warm.</summary>
        private readonly Dictionary<string, int> _warmCounts = new Dictionary<string, int>();

        public ChunkViewProvisioner(IViewAssetProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>Number of chunks currently holding references.</summary>
        public int ChunkCount => _chunkKeys.Count;

        /// <summary>Number of distinct keys with a non-zero reference count.</summary>
        public int TrackedKeyCount => _refCounts.Count;

        /// <summary>How many chunks reference <paramref name="key"/>; zero if none.</summary>
        public int GetReferenceCount(string key)
        {
            return key != null && _refCounts.TryGetValue(key, out var count) ? count : 0;
        }

        /// <summary>Whether the chunk currently holds any references.</summary>
        public bool IsChunkWarm(string chunkId) => chunkId != null && _chunkKeys.ContainsKey(chunkId);

        /// <summary>
        /// Warms every key the chunk needs. Returns once all newly-required loads finished; keys
        /// already warm from another chunk cost nothing.
        /// </summary>
        /// <param name="chunkId">Opaque chunk or region identifier. Re-warming an existing id diffs.</param>
        /// <param name="keys">The prefab keys the chunk needs. Duplicates and nulls are ignored.</param>
        /// <param name="countPerKey">Instances to have ready per key. Clamped to at least 1.</param>
        public Task PrewarmChunkAsync(string chunkId, IEnumerable<string> keys, int countPerKey = 1, CancellationToken cancellationToken = default)
        {
            if (chunkId == null) throw new ArgumentNullException(nameof(chunkId));
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            if (countPerKey < 1) countPerKey = 1;

            var newSet = new HashSet<string>();
            foreach (var key in keys)
            {
                if (!string.IsNullOrEmpty(key)) newSet.Add(key);
            }

            // Increment first, decrement after: a key shared by the old and new set must never
            // transiently reach zero, or it would be unloaded and immediately reloaded.
            var toWarm = new List<string>();
            foreach (var key in newSet)
            {
                _refCounts.TryGetValue(key, out var count);
                _refCounts[key] = count + 1;

                _warmCounts.TryGetValue(key, out var warm);
                if (count == 0 || countPerKey > warm)
                {
                    _warmCounts[key] = Math.Max(warm, countPerKey);
                    toWarm.Add(key);
                }
            }

            if (_chunkKeys.TryGetValue(chunkId, out var oldSet))
            {
                foreach (var key in oldSet) ReleaseKey(key);
            }

            _chunkKeys[chunkId] = newSet;

            if (toWarm.Count == 0) return Task.CompletedTask;
            if (toWarm.Count == 1) return _provider.PrewarmAsync(toWarm[0], _warmCounts[toWarm[0]], cancellationToken);

            var tasks = new Task[toWarm.Count];
            for (var i = 0; i < toWarm.Count; i++)
            {
                tasks[i] = _provider.PrewarmAsync(toWarm[i], _warmCounts[toWarm[i]], cancellationToken);
            }

            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Drops the chunk's references. Keys whose count reaches zero are released through the
        /// provider; keys another chunk still lists are left alone. Unknown chunk id is a no-op.
        /// </summary>
        /// <returns>True if the chunk was known and released.</returns>
        public bool ReleaseChunk(string chunkId)
        {
            if (chunkId == null || !_chunkKeys.TryGetValue(chunkId, out var set)) return false;

            // Remove before decrementing so a re-entrant or repeated call cannot decrement twice.
            _chunkKeys.Remove(chunkId);
            foreach (var key in set) ReleaseKey(key);
            return true;
        }

        /// <summary>Releases every chunk. For scene teardown.</summary>
        public void ReleaseAll()
        {
            var chunkIds = new List<string>(_chunkKeys.Keys);
            foreach (var chunkId in chunkIds) ReleaseChunk(chunkId);
        }

        private void ReleaseKey(string key)
        {
            if (!_refCounts.TryGetValue(key, out var count)) return;

            if (count > 1)
            {
                _refCounts[key] = count - 1;
                return;
            }

            _refCounts.Remove(key);
            _warmCounts.Remove(key);
            _provider.Release(key);
        }
    }
}
