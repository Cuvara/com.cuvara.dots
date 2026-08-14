using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace Cuvara.DOTS.Views
{
    /// <summary>
    /// Turns <see cref="EntityViewRequest"/> into a live view plus an <see cref="EntityViewLink"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not Bursted, and not schedulable.</b> Spawning goes through the managed pool, so
    /// <see cref="OnUpdate"/> runs on the main thread by definition. There is nothing to gain by
    /// pretending otherwise, and no measurement here claims a win.
    /// </para>
    /// <para>
    /// A request whose key is not warm is <b>left in place and retried next frame</b> rather than
    /// force-loading synchronously. The pool would happily load on demand and hitch; deferring
    /// keeps the hitch in <see cref="Provisioning.ChunkViewProvisioner"/>'s prewarm, where it is
    /// asynchronous and expected. The visible cost is that an entity spawned before its chunk
    /// finished warming stays invisible for a few frames.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct EntityViewSpawnSystem : ISystem
    {
        private EntityQuery _pending;

        public void OnCreate(ref SystemState state)
        {
            _pending = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<EntityViewRequest>()
                .WithNone<EntityViewLink>()
                .Build(ref state);

            state.RequireForUpdate(_pending);
            state.RequireForUpdate<EntityViewRegistryReference>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var registry = SystemAPI.ManagedAPI.GetSingleton<EntityViewRegistryReference>().Registry;
            if (registry == null) return;

            var entities = _pending.ToEntityArray(Allocator.Temp);
            var entityManager = state.EntityManager;

            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var key = entityManager.GetComponentData<EntityViewRequest>(entity).ViewKey.ToString();
                if (!registry.IsWarm(key)) continue; // retried next frame, see remarks

                var position = entityManager.HasComponent<LocalTransform>(entity)
                    ? entityManager.GetComponentData<LocalTransform>(entity).Position
                    : default;

                var viewId = registry.Spawn(key, position);
                if (viewId == 0) continue;

                entityManager.AddComponentData(entity, new EntityViewLink { ViewId = viewId });
                entityManager.AddComponentData(entity, new EntityViewLinkCleanup { ViewId = viewId });
                entityManager.RemoveComponent<EntityViewRequest>(entity);
            }

            entities.Dispose();
        }
    }
}
