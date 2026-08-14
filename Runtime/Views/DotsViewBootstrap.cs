using System;
using Unity.Entities;

namespace Cuvara.DOTS.Views
{
    /// <summary>
    /// Installs an <see cref="EntityViewRegistry"/> into a world so the view systems can find it.
    /// </summary>
    /// <remarks>
    /// Plain static helper taking a <see cref="World"/> — no DI types, so the core assembly stays
    /// installable with only the four pinned Unity dependencies. The VContainer extension in
    /// <c>Cuvara.DOTS.VContainer</c> is a two-line wrapper around this.
    /// </remarks>
    public static class DotsViewBootstrap
    {
        /// <summary>
        /// Creates (or overwrites) the registry singleton entity in <paramref name="world"/>.
        /// Idempotent: calling twice replaces the reference rather than creating a second singleton,
        /// because two singletons would make every <c>GetSingleton</c> call throw.
        /// </summary>
        public static Entity Install(World world, EntityViewRegistry registry)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            var entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<EntityViewRegistryReference>());

            Entity entity;
            if (query.IsEmpty)
            {
                entity = entityManager.CreateEntity();
                entityManager.AddComponentObject(entity, new EntityViewRegistryReference { Registry = registry });
#if UNITY_EDITOR
                entityManager.SetName(entity, "EntityViewRegistry");
#endif
            }
            else
            {
                // Mutate the existing managed instance rather than re-adding the component: the
                // managed add/set overloads differ per Entities version, the field write does not.
                entity = query.GetSingletonEntity();
                entityManager.GetComponentObject<EntityViewRegistryReference>(entity).Registry = registry;
            }

            return entity;
        }

        /// <summary>
        /// Recycles every live view and removes the singleton. Safe on a world that never had one.
        /// </summary>
        public static void Uninstall(World world)
        {
            if (world == null || !world.IsCreated) return;

            var entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<EntityViewRegistryReference>());
            if (query.IsEmpty) return;

            var entity = query.GetSingletonEntity();
            entityManager.GetComponentObject<EntityViewRegistryReference>(entity).Registry?.Clear();
            entityManager.DestroyEntity(entity);
        }
    }
}
