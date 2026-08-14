using System.Collections.Generic;
using System.Threading.Tasks;
using Cuvara.DOTS.Groups;
using Cuvara.DOTS.Provisioning;
using Cuvara.DOTS.Views;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Cuvara.DOTS.Samples.HybridViews
{
    /// <summary>
    /// Drop this on one GameObject in an otherwise empty scene, press play, and watch the Console.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sample runs a fixed timeline rather than reacting to input, so the interesting
    /// transitions — a shared key surviving one chunk's release, and the same key being torn down
    /// when the second chunk lets go — happen on their own and are logged as they happen.
    /// </para>
    /// <para>
    /// Everything here uses only <c>Cuvara.DOTS.Runtime</c> and the four pinned Unity DOTS
    /// packages. No VContainer, no GameFoundation/UniT, no Addressables, no Entities.Graphics.
    /// </para>
    /// </remarks>
    public sealed class HybridViewsSample : MonoBehaviour
    {
        private const string ChunkAlpha = "chunk.alpha";
        private const string ChunkBeta = "chunk.beta";

        private const string KeyCube = "cube";
        private const string KeySphere = "sphere";
        private const string KeyCapsule = "capsule";

        [Header("View definitions (the sample's stand-in for an asset catalogue)")]
        [SerializeField]
        private List<PrimitiveViewDefinition> _definitions = new List<PrimitiveViewDefinition>
        {
            new PrimitiveViewDefinition { Key = KeyCube, Primitive = PrimitiveType.Cube, Color = new Color(0.85f, 0.35f, 0.25f) },
            new PrimitiveViewDefinition { Key = KeySphere, Primitive = PrimitiveType.Sphere, Color = new Color(0.25f, 0.6f, 0.9f) },
            new PrimitiveViewDefinition { Key = KeyCapsule, Primitive = PrimitiveType.Capsule, Color = new Color(0.4f, 0.8f, 0.4f) },
        };

        [Header("Timeline")]
        [Tooltip("Seconds between the scripted steps. Raise it if the Console scrolls too fast to read.")]
        [SerializeField]
        private float _stepSeconds = 4f;

        [Tooltip("Instances each chunk asks to have ready per key.")]
        [SerializeField]
        private int _warmCountPerKey = 4;

        private World _world;
        private EntityViewRegistry _registry;
        private PrimitiveViewAssetProvider _provider;
        private ChunkViewProvisioner _provisioner;
        private Transform _viewRoot;

        private readonly List<Entity> _cubeEntities = new List<Entity>();
        private readonly List<Entity> _sphereEntities = new List<Entity>();
        private readonly List<Entity> _capsuleEntities = new List<Entity>();

        private Task _pendingWarm;
        private int _step;
        private float _nextStepTime;

        private void Start()
        {
            _world = World.DefaultGameObjectInjectionWorld;
            if (_world == null)
            {
                Debug.LogError("[HybridViews] No default world. The sample needs the default ECS world bootstrap enabled.");
                enabled = false;
                return;
            }

            // Parenting every view under one transform keeps the hierarchy readable and gives the
            // pool somewhere to park recycled instances.
            _viewRoot = new GameObject("HybridViews Root").transform;

            _provider = new PrimitiveViewAssetProvider(_definitions, _viewRoot);
            _registry = new EntityViewRegistry(_provider, _viewRoot);
            // The registry is the ILiveViewCounter: it is what lets ReleaseChunk refuse to tear
            // down assets that live views are still standing on. Omitting it is what the 0.4.0
            // sample had to work around.
            _provisioner = new ChunkViewProvisioner(_provider, _registry);

            // The one call that wires the package into a world. Everything else below is ordinary
            // ECS code that happens to add EntityViewRequest.
            DotsViewBootstrap.Install(_world, _registry);

            Debug.Log("[HybridViews] installed. Timeline: warm alpha -> spawn -> warm beta -> despawn some -> release alpha -> release beta.");
            _nextStepTime = Time.time;
        }

        private void OnDestroy()
        {
            // Order matters: recycle the views first (Uninstall clears the registry), then drop the
            // chunk references, then destroy whatever the pool is still holding.
            if (_world != null && _world.IsCreated) DotsViewBootstrap.Uninstall(_world);
            _provisioner?.ReleaseAll();
            _provider?.DestroyAll();
            if (_viewRoot != null) Destroy(_viewRoot.gameObject);
        }

        private void Update()
        {
            // A warm is a Task. Nothing advances until it finishes — with this provider that is the
            // same frame, but a real loader would take several, and the sample must not assume.
            if (_pendingWarm != null)
            {
                if (!_pendingWarm.IsCompleted) return;
                if (_pendingWarm.IsFaulted) Debug.LogError($"[HybridViews] prewarm failed: {_pendingWarm.Exception}");
                _pendingWarm = null;
            }

            if (Time.time < _nextStepTime) return;
            _nextStepTime = Time.time + _stepSeconds;

            switch (_step++)
            {
                case 0: WarmAlpha(); break;
                case 1: SpawnEntities(); break;
                case 2: WarmBeta(); break;
                case 3: DespawnSome(); break;
                case 4: ReleaseAlpha(); break;
                case 5: ReleaseBeta(); break;
                case 6: Summary(); break;
                default: enabled = false; break;
            }
        }

        // ---------------------------------------------------------------- steps

        private void WarmAlpha()
        {
            Debug.Log($"[HybridViews] --- step 1: warm '{ChunkAlpha}' with [{KeyCube}, {KeySphere}] x{_warmCountPerKey}");

            // The key is listed twice on purpose: the provisioner de-duplicates on intake, so this
            // still contributes exactly one reference. Counting occurrences would leak.
            _pendingWarm = _provisioner.PrewarmChunkAsync(
                ChunkAlpha,
                new[] { KeyCube, KeySphere, KeyCube },
                _warmCountPerKey);

            LogRefCounts("after warming alpha (note: cube listed twice, refcount is still 1)");
        }

        private void SpawnEntities()
        {
            Debug.Log("[HybridViews] --- step 2: create entities with EntityViewRequest");

            var entityManager = _world.EntityManager;

            for (var i = 0; i < 4; i++) _cubeEntities.Add(CreateEntity(entityManager, KeyCube, radius: 4f, height: 0f, index: i, of: 4));
            for (var i = 0; i < 4; i++) _sphereEntities.Add(CreateEntity(entityManager, KeySphere, radius: 6.5f, height: 1.5f, index: i, of: 4));

            // Cold on purpose: 'capsule' belongs to chunk.beta, which is not warm yet. The spawn
            // system leaves these requests in place and retries, so these two entities exist and
            // move but have no view until step 3. This is the documented deferral, seen from the
            // outside.
            for (var i = 0; i < 2; i++) _capsuleEntities.Add(CreateEntity(entityManager, KeyCapsule, radius: 2.5f, height: 3f, index: i, of: 2));

            Debug.Log($"[HybridViews] created {_cubeEntities.Count + _sphereEntities.Count + _capsuleEntities.Count} entities; " +
                      $"{_capsuleEntities.Count} of them request the cold key '{KeyCapsule}' and stay invisible until it is warm.");
        }

        private void WarmBeta()
        {
            Debug.Log($"[HybridViews] --- step 3: warm '{ChunkBeta}' with [{KeySphere}, {KeyCapsule}] — '{KeySphere}' is shared with alpha");

            _pendingWarm = _provisioner.PrewarmChunkAsync(
                ChunkBeta,
                new[] { KeySphere, KeyCapsule },
                _warmCountPerKey);

            LogRefCounts($"after warming beta ('{KeySphere}' should now be 2 — the capsules become visible within a frame or two)");
        }

        private void DespawnSome()
        {
            Debug.Log("[HybridViews] --- step 4: destroy half the entities; views must come back to the pool, not be leaked");

            var before = _registry.Count;
            var entityManager = _world.EntityManager;

            DestroyHalf(entityManager, _cubeEntities);
            DestroyHalf(entityManager, _sphereEntities);

            // The registry count does not drop here: EntityViewDespawnSystem runs in presentation,
            // which is later this frame. Reading it now would show the pre-destroy number.
            Debug.Log($"[HybridViews] destroyed entities; live views before={before}, " +
                      $"recycles so far={_provider.ReleaseInstanceCount}, instantiations so far={_provider.InstantiateCount}. " +
                      "Recycled instances are reused by the next spawn instead of instantiating more.");
        }

        private void ReleaseAlpha()
        {
            Debug.Log($"[HybridViews] --- step 5: destroy the remaining '{KeyCube}'/'{KeySphere}' entities, THEN release '{ChunkAlpha}'");

            // Destroying the entities is not enough on its own: the views are recycled by the
            // despawn system, which has not run yet this frame. Ask for the release now and it is
            // refused rather than silently destroying on-screen instances.
            var entityManager = _world.EntityManager;
            DestroyAll(entityManager, _cubeEntities);

            var premature = _provisioner.ReleaseChunk(ChunkAlpha);
            Debug.Log($"[HybridViews] releasing '{ChunkAlpha}' before the despawn system ran: " +
                      $"Released={premature.Released}, LiveViewCount={premature.LiveViewCount}, " +
                      $"BlockingKey='{premature.BlockingKey}'. Refused, and nothing changed.");

            // Run the view group so the destroyed entities give their views back, then retry.
            _world.GetExistingSystemManaged<ViewSystemGroup>().Update();

            Debug.Log($"[HybridViews] refcounts before release: {Describe()}");
            var released = _provisioner.ReleaseChunk(ChunkAlpha);
            Debug.Log($"[HybridViews] ReleaseChunk('{ChunkAlpha}') returned Released={released.Released}. " +
                      $"'{KeyCube}' 1 -> 0, so the provider tore it down. " +
                      $"'{KeySphere}' 2 -> 1, so it was NOT torn down — chunk.beta still lists it, and the spheres keep rendering.");
            LogRefCounts("after releasing alpha");

            // A second release of the same chunk is a no-op, not a double decrement. Proving it is
            // cheaper than trusting it.
            var again = _provisioner.ReleaseChunk(ChunkAlpha);
            Debug.Log($"[HybridViews] releasing '{ChunkAlpha}' a second time returned Released={again.Released}, WasTracked={again.WasTracked} (no-op, not a refusal), '{KeySphere}' still {_provisioner.GetReferenceCount(KeySphere)}.");
        }

        private void ReleaseBeta()
        {
            Debug.Log($"[HybridViews] --- step 6: destroy the rest, then release '{ChunkBeta}'");

            var entityManager = _world.EntityManager;
            DestroyAll(entityManager, _sphereEntities);
            DestroyAll(entityManager, _capsuleEntities);
            _world.GetExistingSystemManaged<ViewSystemGroup>().Update(); // recycle before releasing

            _provisioner.ReleaseChunk(ChunkBeta);
            Debug.Log($"[HybridViews] ReleaseChunk('{ChunkBeta}') — '{KeySphere}' 1 -> 0 and '{KeyCapsule}' 1 -> 0, both torn down.");
            LogRefCounts("after releasing beta (everything should be 0)");
        }

        private void Summary()
        {
            Debug.Log($"[HybridViews] --- done. instantiated={_provider.InstantiateCount}, acquires={_provider.AcquireCount}, " +
                      $"recycles={_provider.ReleaseInstanceCount}, key teardowns={_provider.ReleaseKeyCount}, " +
                      $"live views={_registry.Count}, tracked chunks={_provisioner.ChunkCount}, tracked keys={_provisioner.TrackedKeyCount}. " +
                      "Acquires above instantiations is the pool doing its job.");
        }

        // ---------------------------------------------------------------- helpers

        private Entity CreateEntity(EntityManager entityManager, string key, float radius, float height, int index, int of)
        {
            var entity = entityManager.CreateEntity();
            var phase = math.PI * 2f * index / of;

            var start = new float3(radius, height, 0f);
            entityManager.AddComponentData(entity, LocalTransform.FromPosition(start));

            // LocalToWorld has to be added explicitly. TransformSystemGroup writes into it but does
            // not add it: baking would, and creating an entity from code does not. Without it the
            // package's sync system — which reads LocalToWorld, so that parented entities work —
            // never matches this entity and its view sits at the origin forever.
            entityManager.AddComponentData(entity, new LocalToWorld
            {
                Value = float4x4.TRS(start, quaternion.identity, new float3(1f, 1f, 1f)),
            });
            entityManager.AddComponentData(entity, new OrbitMotion
            {
                Radius = radius,
                Speed = 0.6f + 0.1f * index,
                Phase = phase,
                Height = height,
            });

            // The only line that involves the package: ask for a view by key.
            entityManager.AddComponentData(entity, new EntityViewRequest { ViewKey = new FixedString64Bytes(key) });

#if UNITY_EDITOR
            entityManager.SetName(entity, $"{key}-{index}");
#endif
            return entity;
        }

        private void DestroyHalf(EntityManager entityManager, List<Entity> entities)
        {
            var half = entities.Count / 2;
            for (var i = 0; i < half; i++)
            {
                if (entityManager.Exists(entities[i])) entityManager.DestroyEntity(entities[i]);
            }

            entities.RemoveRange(0, half);
        }

        private void DestroyAll(EntityManager entityManager, List<Entity> entities)
        {
            for (var i = 0; i < entities.Count; i++)
            {
                if (entityManager.Exists(entities[i])) entityManager.DestroyEntity(entities[i]);
            }

            entities.Clear();
        }

        private void LogRefCounts(string note) => Debug.Log($"[HybridViews] refcounts {note}: {Describe()}");

        private string Describe()
        {
            return $"{KeyCube}={_provisioner.GetReferenceCount(KeyCube)} " +
                   $"{KeySphere}={_provisioner.GetReferenceCount(KeySphere)} " +
                   $"{KeyCapsule}={_provisioner.GetReferenceCount(KeyCapsule)} " +
                   $"(chunks={_provisioner.ChunkCount}, warm keys={_provisioner.TrackedKeyCount})";
        }
    }
}
