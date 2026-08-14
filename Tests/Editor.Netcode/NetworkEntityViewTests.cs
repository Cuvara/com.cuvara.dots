using Cuvara.DOTS.Configuration;
using Cuvara.DOTS.Groups;
using Cuvara.DOTS.Netcode;
using Cuvara.DOTS.Simulation;
using Cuvara.DOTS.Views;
using Cuvara.Netcode.View;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cuvara.DOTS.Tests.Netcode
{
    /// <summary>
    /// The adapter end to end: <c>IEntityView</c> calls in, entities and pooled views out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Driven through the <b>public groups</b> — <c>NetcodeSystemGroup</c> then
    /// <c>ViewSystemGroup</c> — rather than by ticking named systems. That is the package's stated
    /// ordering contract, so a test that names systems would pass while the contract broke.
    /// </para>
    /// <para>
    /// The three <c>IEntityView</c> methods are called directly, in the order
    /// <c>WorldViewBinder.Tick</c> calls them (spawn, then state, then despawn-by-absence), rather
    /// than through the binder itself. The binder needs <c>WorldState</c>, which needs
    /// <c>Shared.GameLogic</c> — a second optional dependency this assembly would then have to be
    /// constrained on, to test a class that is not ours. What is ours is the adapter's response to
    /// that call sequence.
    /// </para>
    /// </remarks>
    public sealed class NetworkEntityViewTests
    {
        private const string LocalArchetype = "player-local";
        private const string RemoteArchetype = "player-remote";
        private const string EnemyArchetype = "goblin";

        private World _world;
        private EntityManager _entityManager;
        private EntityViewRegistry _registry;
        private ViewConfigCatalog _catalog;
        private ViewArchetypeLibrary _library;
        private ViewConfig _localConfig;
        private ViewConfig _remoteConfig;
        private ViewConfig _enemyConfig;
        private DotsEntityView _view;

        [SetUp]
        public void SetUp()
        {
            _world = new World("Cuvara.DOTS.NetworkEntityViewTests");
            _entityManager = _world.EntityManager;

            _registry = new EntityViewRegistry(new StubViewAssetProvider());
            DotsViewBootstrap.Install(_world, _registry);

            _localConfig = ScriptableObject.CreateInstance<ViewConfig>();
            _localConfig.Configure("player", uniformScale: 1.2f);

            _remoteConfig = ScriptableObject.CreateInstance<ViewConfig>();
            _remoteConfig.Configure("player");

            // The half-height lift the reference implementation baked into its (x, 0.5f, y) literal,
            // authored where it belongs: on the art, not on the world.
            _enemyConfig = ScriptableObject.CreateInstance<ViewConfig>();
            _enemyConfig.Configure("goblin", uniformScale: 0.8f, position: new Vector3(0f, 0.5f, 0f));

            _library = ScriptableObject.CreateInstance<ViewArchetypeLibrary>();
            _library.Configure(
                new ViewArchetypeLibrary.Entry { Name = LocalArchetype, Config = _localConfig },
                new ViewArchetypeLibrary.Entry { Name = RemoteArchetype, Config = _remoteConfig },
                new ViewArchetypeLibrary.Entry { Name = EnemyArchetype, Config = _enemyConfig });

            _catalog = new ViewConfigCatalog();
            _catalog.Build(_library);
            _catalog.Install(_world);

            _view = NewView();
            DotsNetcodeBootstrap.Install(_world, _view);
        }

        [TearDown]
        public void TearDown()
        {
            DotsNetcodeBootstrap.Uninstall(_world);
            _catalog.Dispose();
            Object.DestroyImmediate(_library);
            Object.DestroyImmediate(_localConfig);
            Object.DestroyImmediate(_remoteConfig);
            Object.DestroyImmediate(_enemyConfig);
            DotsViewBootstrap.Uninstall(_world);
            _world.Dispose();
        }

        private DotsEntityView NewView(bool writeHealth = false) => new DotsEntityView(
            _catalog,
            new PrefixArchetypeResolver(LocalArchetype, RemoteArchetype, new PrefixArchetypeResolver.Rule("enemy-", EnemyArchetype)),
            SnapshotSpaceMapping.XZPlane,
            writeHealth);

        /// <summary>One frame: the netcode group drains, then the view group presents.</summary>
        private void Tick()
        {
            _world.GetExistingSystemManaged<NetcodeSystemGroup>().Update();
            _world.GetExistingSystemManaged<ViewSystemGroup>().Update();
        }

        private Entity Find(string id)
        {
            using var query = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkEntity>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            var wanted = new FixedString64Bytes(id);

            for (var i = 0; i < entities.Length; i++)
            {
                if (_entityManager.GetComponentData<NetworkEntity>(entities[i]).Id.Equals(wanted))
                {
                    return entities[i];
                }
            }

            return Entity.Null;
        }

        private static int EntityCount(World world)
        {
            using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkEntity>());
            return query.CalculateEntityCount();
        }

        [Test]
        public void Spawn_ThenState_ProducesAPositionedViewInTheSameFrame()
        {
            // The frame-landing claim in DotsEntityView's remarks, asserted: enqueue before the
            // netcode group runs and the pooled instance is at the right place after the view group,
            // with no second Tick.
            var view = (IEntityView)_view;
            view.Spawn("uuid-a", isLocal: false);
            view.SetState("uuid-a", 3f, 7f, 90, 100);

            Tick();

            var entity = Find("uuid-a");
            Assert.AreNotEqual(Entity.Null, entity, "the drain created the mirror entity");
            Assert.AreEqual(new float3(3f, 0f, 7f), _entityManager.GetComponentData<LocalTransform>(entity).Position);

            var viewId = _entityManager.GetComponentData<EntityViewLink>(entity).ViewId;
            Assert.AreEqual(new Vector3(3f, 0f, 7f), _registry.Get(viewId).transform.position);
        }

        [Test]
        public void ServerXY_LandsOnTheGroundPlane_AndTheLiftComesFromTheConfig()
        {
            // The two halves of the sample's (x, 0.5f, y) literal, now separable: the entity is on
            // the plane the mapping describes, and only the enemy's view is lifted, because only the
            // enemy's ViewConfig asks for it.
            var view = (IEntityView)_view;
            view.Spawn("enemy-1", isLocal: false);
            view.SetState("enemy-1", 3f, 7f, 30, 30);
            view.Spawn("uuid-a", isLocal: false);
            view.SetState("uuid-a", 3f, 7f, 90, 100);

            Tick();

            var enemy = Find("enemy-1");
            var player = Find("uuid-a");

            Assert.AreEqual(new float3(3f, 0f, 7f), _entityManager.GetComponentData<LocalTransform>(enemy).Position,
                "the entity itself is never lifted — gameplay maths is 2D");

            var enemyView = _registry.Get(_entityManager.GetComponentData<EntityViewLink>(enemy).ViewId);
            var playerView = _registry.Get(_entityManager.GetComponentData<EntityViewLink>(player).ViewId);

            Assert.AreEqual(new Vector3(3f, 0.5f, 7f), enemyView.transform.position, "lifted by the config offset");
            Assert.AreEqual(new Vector3(3f, 0f, 7f), playerView.transform.position, "the player config asks for no lift");
        }

        [Test]
        public void Archetype_ComesFromTheCatalog_NotFromAHardcodedPrefix()
        {
            var view = (IEntityView)_view;
            view.Spawn("enemy-1", isLocal: false);
            view.Spawn("uuid-a", isLocal: false);
            view.Spawn("uuid-me", isLocal: true);

            Tick();

            Assert.AreEqual(_catalog.IndexOf(EnemyArchetype), _entityManager.GetComponentData<ViewConfigRef>(Find("enemy-1")).Index);
            Assert.AreEqual(_catalog.IndexOf(RemoteArchetype), _entityManager.GetComponentData<ViewConfigRef>(Find("uuid-a")).Index);
            Assert.AreEqual(_catalog.IndexOf(LocalArchetype), _entityManager.GetComponentData<ViewConfigRef>(Find("uuid-me")).Index);

            // The config's scale reached the instance, which is what proves the index was used
            // rather than merely written.
            var localView = _registry.Get(_entityManager.GetComponentData<EntityViewLink>(Find("uuid-me")).ViewId);
            Assert.AreEqual(1.2f, localView.transform.localScale.x, 1e-4f);
        }

        [Test]
        public void IsLocal_IsCarriedOntoTheEntity()
        {
            var view = (IEntityView)_view;
            view.Spawn("uuid-me", isLocal: true);
            view.Spawn("uuid-a", isLocal: false);

            Tick();

            Assert.IsTrue(_entityManager.GetComponentData<NetworkEntity>(Find("uuid-me")).IsLocal);
            Assert.IsFalse(_entityManager.GetComponentData<NetworkEntity>(Find("uuid-a")).IsLocal);
        }

        [Test]
        public void Despawn_DestroysTheEntity_AndRecyclesTheViewInTheSameFrame()
        {
            var view = (IEntityView)_view;
            view.Spawn("uuid-a", isLocal: false);
            view.SetState("uuid-a", 1f, 1f, 100, 100);
            Tick();

            Assert.AreEqual(1, _registry.Count);

            view.Despawn("uuid-a");
            Tick();

            Assert.AreEqual(Entity.Null, Find("uuid-a"), "the mirror entity is gone");
            Assert.AreEqual(0, _registry.Count, "and its view went back to the pool");
        }

        [Test]
        public void RespawnAfterDespawn_Works_AndIsANewEntity()
        {
            // An AOI exit followed by a re-entry is the common case, and the id is identical across
            // it. A stale id → Entity mapping would silently refuse the second spawn.
            var view = (IEntityView)_view;
            view.Spawn("uuid-a", isLocal: false);
            Tick();
            var first = Find("uuid-a");

            view.Despawn("uuid-a");
            Tick();

            view.Spawn("uuid-a", isLocal: false);
            view.SetState("uuid-a", 5f, 5f, 100, 100);
            Tick();

            var second = Find("uuid-a");
            Assert.AreNotEqual(Entity.Null, second);
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(new float3(5f, 0f, 5f), _entityManager.GetComponentData<LocalTransform>(second).Position);
        }

        [Test]
        public void DuplicateSpawn_IsIgnored_NotReplaced()
        {
            var view = (IEntityView)_view;
            view.Spawn("uuid-a", isLocal: false);
            view.Spawn("uuid-a", isLocal: false);

            Tick();

            Assert.AreEqual(1, EntityCount(_world));
        }

        [Test]
        public void StateForAnUnknownId_IsDropped_NotImplicitlySpawned()
        {
            // SetState carries no isLocal, so an implicit spawn would have to guess it — and the one
            // guess available is wrong exactly for the local player.
            ((IEntityView)_view).SetState("uuid-ghost", 1f, 1f, 10, 10);

            Tick();

            Assert.AreEqual(0, EntityCount(_world));
        }

        [Test]
        public void WireHp_LandsOnNetworkEntityState_AndNotOnHealth_ByDefault()
        {
            // Health means "destroy at zero" in this package. Mirroring server hp into it by default
            // would let a client-side system destroy entities the server is still listing.
            var view = (IEntityView)_view;
            view.Spawn("enemy-1", isLocal: false);
            view.SetState("enemy-1", 0f, 0f, 12, 30);

            Tick();

            var entity = Find("enemy-1");
            var state = _entityManager.GetComponentData<NetworkEntityState>(entity);
            Assert.AreEqual(12, state.Hp);
            Assert.AreEqual(30, state.MaxHp);
            Assert.IsFalse(_entityManager.HasComponent<Health>(entity), "opt-in, and off by default");
        }

        [Test]
        public void WriteHealth_MirrorsWireHpIntoHealth_WhenAskedFor()
        {
            var opted = NewView(writeHealth: true);
            DotsNetcodeBootstrap.Install(_world, opted);

            var view = (IEntityView)opted;
            view.Spawn("enemy-1", isLocal: false);
            view.SetState("enemy-1", 0f, 0f, 12, 30);

            Tick();

            var health = _entityManager.GetComponentData<Health>(Find("enemy-1"));
            Assert.AreEqual(12, health.Current);
            Assert.AreEqual(30, health.Max);
        }

        [Test]
        public void UnknownArchetype_IsNotPresented_AndDoesNotThrow()
        {
            var stranded = new DotsEntityView(
                _catalog,
                new PrefixArchetypeResolver("no-such-archetype", "no-such-archetype"),
                SnapshotSpaceMapping.XZPlane);
            DotsNetcodeBootstrap.Install(_world, stranded);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no-such-archetype"));

            ((IEntityView)stranded).Spawn("uuid-a", isLocal: false);
            Tick();

            Assert.AreEqual(0, EntityCount(_world), "an unconfigured id is invisible, not rendered as something else");
            Assert.AreEqual(0, stranded.Count);
        }

        [Test]
        public void SessionReset_DespawnsEverything()
        {
            // What WorldViewBinder.Reset does on a map transfer: a Despawn for every live id.
            var view = (IEntityView)_view;
            view.Spawn("uuid-a", isLocal: false);
            view.Spawn("uuid-b", isLocal: false);
            view.Spawn("enemy-1", isLocal: false);
            Tick();
            Assert.AreEqual(3, EntityCount(_world));

            view.Despawn("uuid-a");
            view.Despawn("uuid-b");
            view.Despawn("enemy-1");
            Tick();

            Assert.AreEqual(0, EntityCount(_world));
            Assert.AreEqual(0, _registry.Count);
            Assert.AreEqual(0, _view.Count);
        }

        [Test]
        public void QueueIsFullyDrained_EveryTick()
        {
            var view = (IEntityView)_view;
            for (var i = 0; i < 50; i++)
            {
                view.Spawn($"uuid-{i}", isLocal: false);
                view.SetState($"uuid-{i}", i, i, 100, 100);
            }

            Assert.AreEqual(100, _view.PendingCommands);

            Tick();

            Assert.AreEqual(0, _view.PendingCommands, "a partial drain would leave entities at the origin");
            Assert.AreEqual(50, EntityCount(_world));
        }
    }
}
