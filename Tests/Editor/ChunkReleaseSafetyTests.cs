using System.Collections.Generic;
using Cuvara.DOTS.Provisioning;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Cuvara.DOTS.Tests.Editor
{
    /// <summary>
    /// A chunk release must not destroy assets a live view is standing on.
    /// </summary>
    /// <remarks>
    /// The regression these cover: reference counting tracks <i>chunks</i>, and a live view is held
    /// by an entity the provisioner has never heard of. Before 0.5.0, unloading a chunk while its
    /// entities were alive destroyed their pooled instances while the registry kept the handles and
    /// the entities kept an <c>EntityViewLink</c> that could never resolve — views silently gone,
    /// no error, no recovery. That is the ordinary streaming path, not an exotic case.
    /// </remarks>
    public sealed class ChunkReleaseSafetyTests
    {
        /// <summary>Stands in for <c>EntityViewRegistry</c> without needing a World or GameObjects.</summary>
        private sealed class FakeLiveViewCounter : ILiveViewCounter
        {
            public readonly Dictionary<string, int> Live = new Dictionary<string, int>();

            public int CountLiveViews(string key) => Live.TryGetValue(key, out var count) ? count : 0;
        }

        private RecordingViewAssetProvider _provider;
        private FakeLiveViewCounter _live;
        private ChunkViewProvisioner _provisioner;

        [SetUp]
        public void SetUp()
        {
            _provider = new RecordingViewAssetProvider();
            _live = new FakeLiveViewCounter();
            _provisioner = new ChunkViewProvisioner(_provider, _live);

            // A refused release logs a warning on purpose; the tests assert the return value, and an
            // expected warning must not be read as an unexpected one.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown() => LogAssert.ignoreFailingMessages = false;

        [Test]
        public void ReleasingAChunkWithLiveViews_IsRefused_AndChangesNothing()
        {
            _provisioner.PrewarmChunkAsync("chunk-a", new[] { "goblin", "torch" });
            _live.Live["goblin"] = 2;

            var result = _provisioner.ReleaseChunk("chunk-a");

            Assert.IsFalse(result.Released);
            Assert.IsTrue(result.WasTracked);
            Assert.IsTrue(result.WasRefused, "distinguishable from an unknown chunk");
            Assert.AreEqual(2, result.LiveViewCount);
            Assert.AreEqual("goblin", result.BlockingKey);

            // Nothing released, nothing decremented — the caller can retry after despawning.
            CollectionAssert.IsEmpty(_provider.Released);
            Assert.AreEqual(1, _provisioner.GetReferenceCount("goblin"));
            Assert.AreEqual(1, _provisioner.GetReferenceCount("torch"));
            Assert.IsTrue(_provisioner.IsChunkTracked("chunk-a"));
        }

        [Test]
        public void ReleasingAfterTheViewsAreDespawned_Succeeds()
        {
            _provisioner.PrewarmChunkAsync("chunk-a", new[] { "goblin" });
            _live.Live["goblin"] = 1;

            Assert.IsFalse(_provisioner.ReleaseChunk("chunk-a").Released);

            _live.Live.Remove("goblin"); // the entity was despawned

            Assert.IsTrue(_provisioner.ReleaseChunk("chunk-a").Released);
            CollectionAssert.Contains(_provider.Released, "goblin");
            Assert.AreEqual(0, _provisioner.TrackedKeyCount);
        }

        [Test]
        public void LiveViewsOnAKeyAnotherChunkAlsoHolds_DoNotBlock()
        {
            // Releasing chunk-a would not tear "goblin" down — chunk-b still references it — so the
            // live views are in no danger and the release must not be refused.
            _provisioner.PrewarmChunkAsync("chunk-a", new[] { "goblin", "torch" });
            _provisioner.PrewarmChunkAsync("chunk-b", new[] { "goblin" });
            _live.Live["goblin"] = 5;

            var result = _provisioner.ReleaseChunk("chunk-a");

            Assert.IsTrue(result.Released);
            CollectionAssert.Contains(_provider.Released, "torch");
            CollectionAssert.DoesNotContain(_provider.Released, "goblin");
            Assert.AreEqual(1, _provisioner.GetReferenceCount("goblin"));
        }

        [Test]
        public void ReleaseAll_ReportsHowManyChunksItCouldNotRelease()
        {
            _provisioner.PrewarmChunkAsync("chunk-a", new[] { "goblin" });
            _provisioner.PrewarmChunkAsync("chunk-b", new[] { "torch" });
            _live.Live["goblin"] = 1;

            var refused = _provisioner.ReleaseAll();

            Assert.AreEqual(1, refused);
            Assert.IsTrue(_provisioner.IsChunkTracked("chunk-a"), "still held open by a live view");
            Assert.IsFalse(_provisioner.IsChunkTracked("chunk-b"));
        }

        [Test]
        public void WithoutALiveViewCounter_ReleaseIsUnconditional()
        {
            // Documented as unsafe for streaming: with no counter the provisioner cannot see live
            // views. Pinned so the hazard is a decision someone made, not an accident.
            var provisioner = new ChunkViewProvisioner(_provider);
            provisioner.PrewarmChunkAsync("chunk-a", new[] { "goblin" });

            Assert.IsTrue(provisioner.ReleaseChunk("chunk-a").Released);
            CollectionAssert.Contains(_provider.Released, "goblin");
        }

        [Test]
        public void IsChunkTracked_IsTrueBeforeLoadsFinish_IsChunkLoadedIsNot()
        {
            // The rename exists because the old name (IsChunkWarm) reads as "loads finished", which
            // it never meant. The fake provider completes synchronously, so the only way to observe
            // the distinction here is that both are true after the awaited prewarm.
            Assert.IsFalse(_provisioner.IsChunkTracked("chunk-a"));
            Assert.IsFalse(_provisioner.IsChunkLoaded("chunk-a"));

            _provisioner.PrewarmChunkAsync("chunk-a", new[] { "goblin" });

            Assert.IsTrue(_provisioner.IsChunkTracked("chunk-a"));
            Assert.IsTrue(_provisioner.IsChunkLoaded("chunk-a"));
        }
    }
}
