using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cuvara.DOTS.Messaging;
using UnityEngine;

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
    /// <b>Releasing a chunk whose views are still alive is refused, not performed.</b> Reference
    /// counting alone cannot make that case safe: the counts track chunks, and a live view is held
    /// by an entity the provisioner has never heard of. Releasing anyway destroys pooled instances
    /// that are on screen, while <c>EntityViewRegistry</c> keeps their handles and the entities keep
    /// an <c>EntityViewLink</c> that will never resolve and never respawn — views silently gone, no
    /// error, no way to recover. Of the available fixes, refusing is the one whose failure mode a
    /// consumer can actually see: the call returns <see cref="ChunkReleaseResult.LiveViewCount"/>,
    /// logs a warning naming a blocking key, publishes <see cref="ChunkReleased"/> with
    /// <c>Released == false</c>, and changes nothing. Deferring the release instead would report
    /// success for something that did not happen, and cascading into a despawn would put entity
    /// lifetime decisions in the asset layer, which cannot see entities at all.
    /// </para>
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
        private readonly ILiveViewCounter _liveViewCounter;
        private readonly IDotsPublisher<ChunkWarmed> _warmedPublisher;
        private readonly IDotsPublisher<ChunkReleased> _releasedPublisher;

        /// <summary>chunk id -> the de-duplicated key set that chunk currently holds a reference on.</summary>
        private readonly Dictionary<string, HashSet<string>> _chunkKeys = new Dictionary<string, HashSet<string>>();

        /// <summary>asset key -> number of chunks referencing it.</summary>
        private readonly Dictionary<string, int> _refCounts = new Dictionary<string, int>();

        /// <summary>asset key -> highest instance count anyone has asked to warm.</summary>
        private readonly Dictionary<string, int> _warmCounts = new Dictionary<string, int>();

        /// <summary>chunk ids whose prewarm has completed, as opposed to merely started.</summary>
        private readonly HashSet<string> _loaded = new HashSet<string>();

        /// <param name="liveViewCounter">
        /// Supplies live-view counts so a release that would destroy on-screen views can be refused.
        /// Optional, and <b>omitting it is unsafe for streaming</b>: without it the provisioner
        /// cannot see live views and releases unconditionally, which is the dangling-link bug this
        /// parameter exists to prevent. Pass the <c>EntityViewRegistry</c>.
        /// </param>
        public ChunkViewProvisioner(
            IViewAssetProvider provider,
            ILiveViewCounter liveViewCounter = null,
            IDotsPublisher<ChunkWarmed> warmedPublisher = null,
            IDotsPublisher<ChunkReleased> releasedPublisher = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _liveViewCounter = liveViewCounter;
            _warmedPublisher = warmedPublisher ?? NullDotsPublisher<ChunkWarmed>.Instance;
            _releasedPublisher = releasedPublisher ?? NullDotsPublisher<ChunkReleased>.Instance;
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

        /// <summary>
        /// Whether the chunk holds references — i.e. it has been prewarmed and not released.
        /// </summary>
        /// <remarks>
        /// This says nothing about loading progress. It is true the instant
        /// <see cref="PrewarmChunkAsync"/> is called, before a single byte has loaded. It was called
        /// <c>IsChunkWarm</c> before 0.5.0, which every caller would reasonably read as "loads
        /// finished" — the question they actually want is <see cref="IsChunkLoaded"/>.
        /// </remarks>
        public bool IsChunkTracked(string chunkId) => chunkId != null && _chunkKeys.ContainsKey(chunkId);

        /// <summary>
        /// Whether every key the chunk asked for has finished loading, so spawning from it will not
        /// be deferred.
        /// </summary>
        public bool IsChunkLoaded(string chunkId) => chunkId != null && _loaded.Contains(chunkId);

        /// <summary>
        /// Warms every key the chunk needs. Returns once all newly-required loads finished; keys
        /// already warm from another chunk cost nothing.
        /// </summary>
        /// <param name="chunkId">Opaque chunk or region identifier. Re-warming an existing id diffs.</param>
        /// <param name="keys">The prefab keys the chunk needs. Duplicates and nulls are ignored.</param>
        /// <param name="countPerKey">Instances to have ready per key. Clamped to at least 1.</param>
        public async Task PrewarmChunkAsync(string chunkId, IEnumerable<string> keys, int countPerKey = 1, CancellationToken cancellationToken = default)
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
            _loaded.Remove(chunkId); // re-warming reopens the loading window

            if (toWarm.Count == 1)
            {
                await _provider.PrewarmAsync(toWarm[0], _warmCounts[toWarm[0]], cancellationToken);
            }
            else if (toWarm.Count > 1)
            {
                var tasks = new Task[toWarm.Count];
                for (var i = 0; i < toWarm.Count; i++)
                {
                    tasks[i] = _provider.PrewarmAsync(toWarm[i], _warmCounts[toWarm[i]], cancellationToken);
                }

                await Task.WhenAll(tasks);
            }

            // Only mark loaded if the chunk still exists as warmed here — a release or a re-warm
            // that landed while this awaited must win over a stale completion.
            if (_chunkKeys.TryGetValue(chunkId, out var current) && ReferenceEquals(current, newSet))
            {
                _loaded.Add(chunkId);
                _warmedPublisher.Publish(new ChunkWarmed(chunkId, newSet.Count));
            }
        }

        /// <summary>
        /// Drops the chunk's references. Keys whose count reaches zero are released through the
        /// provider; keys another chunk still lists are left alone.
        /// </summary>
        /// <remarks>
        /// <b>Refused, with nothing changed, when a live view still stands on one of the keys this
        /// call would release.</b> Despawn the entities first, then call again. An unknown chunk id
        /// is a no-op, distinguishable from a refusal via
        /// <see cref="ChunkReleaseResult.WasTracked"/>.
        /// </remarks>
        public ChunkReleaseResult ReleaseChunk(string chunkId)
        {
            if (chunkId == null || !_chunkKeys.TryGetValue(chunkId, out var set))
            {
                return new ChunkReleaseResult(false, false, 0, null);
            }

            var live = CountBlockingViews(set, out var blockingKey);
            if (live > 0)
            {
                Debug.LogWarning(
                    $"[Cuvara.DOTS] Refused to release chunk '{chunkId}': {live} view(s) are still live on its " +
                    $"keys (e.g. '{blockingKey}'). Releasing would destroy on-screen instances and leave their " +
                    "EntityViewLinks dangling forever. Despawn those entities first, then release the chunk.");

                var refused = new ChunkReleaseResult(false, true, live, blockingKey);
                _releasedPublisher.Publish(new ChunkReleased(chunkId, false, live));
                return refused;
            }

            // Remove before decrementing so a re-entrant or repeated call cannot decrement twice.
            _chunkKeys.Remove(chunkId);
            _loaded.Remove(chunkId);
            foreach (var key in set) ReleaseKey(key);

            _releasedPublisher.Publish(new ChunkReleased(chunkId, true, 0));
            return new ChunkReleaseResult(true, true, 0, null);
        }

        /// <summary>
        /// Releases every chunk. For scene teardown. Chunks with live views are refused like any
        /// other, and the count of those refusals is returned.
        /// </summary>
        public int ReleaseAll()
        {
            var chunkIds = new List<string>(_chunkKeys.Keys);
            var refused = 0;
            foreach (var chunkId in chunkIds)
            {
                if (!ReleaseChunk(chunkId).Released) refused++;
            }

            return refused;
        }

        /// <summary>
        /// Live views standing on keys this chunk is the last referencer of. Keys another chunk also
        /// holds are not counted: releasing this chunk would not tear those down.
        /// </summary>
        private int CountBlockingViews(HashSet<string> set, out string blockingKey)
        {
            blockingKey = null;
            if (_liveViewCounter == null) return 0;

            var total = 0;
            foreach (var key in set)
            {
                if (GetReferenceCount(key) > 1) continue;

                var live = _liveViewCounter.CountLiveViews(key);
                if (live <= 0) continue;

                total += live;
                blockingKey ??= key;
            }

            return total;
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
