using System;
using System.Collections.Generic;
using Cuvara.DOTS.Provisioning;
using Unity.Mathematics;
using UnityEngine;

namespace Cuvara.DOTS.Views
{
    /// <summary>
    /// The managed side-table of the hybrid: view handle -&gt; live GameObject.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything managed about the hybrid lives here, so the systems around it stay
    /// <c>ISystem</c> structs. Plain constructor, no DI types, no statics — an instance is handed
    /// to the world through <see cref="EntityViewRegistryReference"/>.
    /// </para>
    /// <para>
    /// Handles are dense positive integers and are <b>not</b> reused after despawn. Reuse would
    /// make a stale <see cref="EntityViewLink"/> silently address someone else's view, which is a
    /// bug that reads as a rendering glitch. Wrapping at <see cref="int.MaxValue"/> is not
    /// defended against; a session would have to spawn two billion views to reach it.
    /// </para>
    /// </remarks>
    public sealed class EntityViewRegistry
    {
        private readonly IViewAssetProvider _provider;
        private readonly Dictionary<int, GameObject> _views = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, Transform> _transforms = new Dictionary<int, Transform>();
        private readonly Transform _root;
        private int _nextViewId = 1;

        public EntityViewRegistry(IViewAssetProvider provider, Transform root = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _root = root;
        }

        /// <summary>Live view count. Used by tests and diagnostics.</summary>
        public int Count => _views.Count;

        /// <summary>Whether the pool can serve <paramref name="key"/> without a synchronous load.</summary>
        public bool IsWarm(string key) => _provider.IsWarm(key);

        /// <summary>
        /// Spawns a view for the key and returns its handle, or 0 if the provider gave nothing back.
        /// </summary>
        public int Spawn(string key, float3 position)
        {
            if (string.IsNullOrEmpty(key)) return 0;

            var instance = _provider.Acquire(key, position, Quaternion.identity, _root);
            if (instance == null) return 0;

            var viewId = _nextViewId++;
            _views[viewId] = instance;
            _transforms[viewId] = instance.transform;
            return viewId;
        }

        /// <summary>Recycles the view behind the handle. Unknown or already-released handles are a no-op.</summary>
        public bool Despawn(int viewId)
        {
            if (!_views.TryGetValue(viewId, out var instance)) return false;

            _views.Remove(viewId);
            _transforms.Remove(viewId);
            if (instance != null) _provider.ReleaseInstance(instance);
            return true;
        }

        /// <summary>Resolves a handle. Null when unknown or the instance was destroyed behind our back.</summary>
        public GameObject Get(int viewId)
        {
            return _views.TryGetValue(viewId, out var instance) ? instance : null;
        }

        /// <summary>
        /// Writes one sampled ECS transform onto its GameObject. Called in a tight main-thread loop
        /// by <see cref="EntityViewTransformSyncSystem"/>, hence the cached
        /// <see cref="Transform"/> lookup — <c>GameObject.transform</c> is a native call each time.
        /// </summary>
        public void ApplyTransform(in ViewTransformSample sample)
        {
            if (!_transforms.TryGetValue(sample.ViewId, out var transform) || transform == null) return;

            transform.SetPositionAndRotation(sample.Position, sample.Rotation);
            var scale = sample.Scale;
            transform.localScale = new Vector3(scale, scale, scale);
        }

        /// <summary>Recycles every live view. For session teardown.</summary>
        public void Clear()
        {
            foreach (var pair in _views)
            {
                if (pair.Value != null) _provider.ReleaseInstance(pair.Value);
            }

            _views.Clear();
            _transforms.Clear();
        }
    }
}
