using Cuvara.DOTS.Configuration;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cuvara.DOTS.Tests.Editor
{
    /// <summary>
    /// Authoring assets in, blob out: name resolution, pool-size aggregation, and the malformed-row
    /// behaviour.
    /// </summary>
    public sealed class ViewConfigCatalogTests
    {
        private ViewArchetypeLibrary _library;
        private ViewConfigCatalog _catalog;

        private static ViewConfig MakeConfig(string key, int poolSize = 1, float scale = 1f)
        {
            var config = ScriptableObject.CreateInstance<ViewConfig>();
            config.Configure(key, poolSize, scale);
            return config;
        }

        private void SetEntries(params ViewArchetypeLibrary.Entry[] entries) => _library.Configure(entries);

        [SetUp]
        public void SetUp()
        {
            _library = ScriptableObject.CreateInstance<ViewArchetypeLibrary>();
            _catalog = new ViewConfigCatalog();
        }

        [TearDown]
        public void TearDown()
        {
            _catalog.Dispose();
            Object.DestroyImmediate(_library);
        }

        [Test]
        public void Build_ResolvesNamesToIndices_AndCarriesTheConfig()
        {
            var goblin = MakeConfig("goblin", poolSize: 8, scale: 1.5f);
            var torch = MakeConfig("torch", poolSize: 2);
            SetEntries(
                new ViewArchetypeLibrary.Entry { Name = "goblin", Config = goblin },
                new ViewArchetypeLibrary.Entry { Name = "torch", Config = torch });

            _catalog.Build(_library);

            Assert.AreEqual(2, _catalog.Count);
            Assert.AreEqual(0, _catalog.IndexOf("goblin"));
            Assert.AreEqual(1, _catalog.IndexOf("torch"));
            Assert.AreEqual(-1, _catalog.IndexOf("wyvern"), "an unknown name is -1, not an exception");

            Assert.AreEqual("goblin", _catalog[0].ViewKey.ToString());
            Assert.AreEqual(8, _catalog[0].PoolSize);
            Assert.AreEqual(1.5f, _catalog[0].Scale, 1e-5f);

            Object.DestroyImmediate(goblin);
            Object.DestroyImmediate(torch);
        }

        [Test]
        public void Blob_LooksUpByNameHash_WithoutAManagedString()
        {
            var goblin = MakeConfig("goblin");
            SetEntries(new ViewArchetypeLibrary.Entry { Name = "goblin", Config = goblin });

            _catalog.Build(_library);

            ref var table = ref _catalog.Table.Value;
            Assert.AreEqual(0, table.IndexOf(ViewArchetypeLibrary.HashName("goblin")));
            Assert.AreEqual(-1, table.IndexOf(ViewArchetypeLibrary.HashName("wyvern")));

            Object.DestroyImmediate(goblin);
        }

        [Test]
        public void TwoArchetypesSharingAPrefab_ReportTheLargestPoolSizeOnce()
        {
            // The provisioner's warm count only grows, so the larger request is the one that matters;
            // reporting the key twice would also double-count against a chunk's reference.
            var small = MakeConfig("goblin", poolSize: 2);
            var large = MakeConfig("goblin", poolSize: 9);
            SetEntries(
                new ViewArchetypeLibrary.Entry { Name = "goblin", Config = small },
                new ViewArchetypeLibrary.Entry { Name = "goblin-elite", Config = large });

            _catalog.Build(_library);
            var sizes = _catalog.PoolSizesByKey();

            Assert.AreEqual(1, sizes.Count);
            Assert.AreEqual(9, sizes["goblin"]);

            Object.DestroyImmediate(small);
            Object.DestroyImmediate(large);
        }

        [Test]
        public void MalformedRows_AreSkippedWithAWarning_NotThrown()
        {
            LogAssert.ignoreFailingMessages = true;

            var good = MakeConfig("goblin");
            SetEntries(
                new ViewArchetypeLibrary.Entry { Name = "", Config = good },          // no name
                new ViewArchetypeLibrary.Entry { Name = "orphan", Config = null },     // no config
                new ViewArchetypeLibrary.Entry { Name = "goblin", Config = good },
                new ViewArchetypeLibrary.Entry { Name = "goblin", Config = good });    // duplicate

            Assert.DoesNotThrow(() => _catalog.Build(_library));
            Assert.AreEqual(1, _catalog.Count, "one broken row must not stop a session starting");
            Assert.AreEqual(0, _catalog.IndexOf("goblin"));

            LogAssert.ignoreFailingMessages = false;
            Object.DestroyImmediate(good);
        }

        [Test]
        public void Rebuilding_ReplacesTheBlob_AndDisposeReleasesIt()
        {
            var goblin = MakeConfig("goblin");
            SetEntries(new ViewArchetypeLibrary.Entry { Name = "goblin", Config = goblin });
            _catalog.Build(_library);
            var first = _catalog.Table;

            _catalog.Build(_library);

            Assert.IsTrue(_catalog.Table.IsCreated);
            Assert.IsFalse(first.IsCreated, "the previous blob must not be leaked");

            _catalog.Dispose();
            Assert.IsFalse(_catalog.Table.IsCreated);

            Object.DestroyImmediate(goblin);
        }
    }
}
