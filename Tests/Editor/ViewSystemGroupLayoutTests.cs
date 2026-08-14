using System;
using System.Linq;
using Cuvara.DOTS.Views;
using NUnit.Framework;
using Unity.Entities;

namespace Cuvara.DOTS.Tests.Editor
{
    /// <summary>
    /// Asserts the system group layout by reflection, because a wrong
    /// <see cref="UpdateInGroupAttribute"/> does not fail a build, does not throw at runtime, and
    /// shows up only as views lagging the simulation by a frame.
    /// </summary>
    public sealed class ViewSystemGroupLayoutTests
    {
        private static Type GroupOf<T>()
        {
            var attribute = typeof(T).GetCustomAttributes(typeof(UpdateInGroupAttribute), false)
                .Cast<UpdateInGroupAttribute>()
                .SingleOrDefault();

            Assert.IsNotNull(attribute, $"{typeof(T).Name} must declare an explicit [UpdateInGroup]");
            return attribute.GroupType;
        }

        private static Type[] UpdateAfterOf<T>()
        {
            return typeof(T).GetCustomAttributes(typeof(UpdateAfterAttribute), false)
                .Cast<UpdateAfterAttribute>()
                .Select(attribute => attribute.SystemType)
                .ToArray();
        }

        [Test]
        public void PresentationGroup_IsInPresentation_NotSimulation()
        {
            // In SimulationSystemGroup the sync would read pre-TransformSystemGroup values and the
            // views would trail the entities by a frame.
            Assert.AreEqual(typeof(PresentationSystemGroup), GroupOf<CuvaraViewPresentationGroup>());
        }

        [Test]
        public void LifecycleAndSyncGroups_NestUnderThePackageGroup()
        {
            Assert.AreEqual(typeof(CuvaraViewPresentationGroup), GroupOf<CuvaraViewLifecycleGroup>());
            Assert.AreEqual(typeof(CuvaraViewPresentationGroup), GroupOf<CuvaraViewTransformSyncGroup>());
        }

        [Test]
        public void SyncGroup_RunsAfterLifecycleGroup()
        {
            CollectionAssert.Contains(UpdateAfterOf<CuvaraViewTransformSyncGroup>(), typeof(CuvaraViewLifecycleGroup));
        }

        [Test]
        public void LifecycleSystems_AreInTheLifecycleGroup_DespawnBeforeSpawn()
        {
            Assert.AreEqual(typeof(CuvaraViewLifecycleGroup), GroupOf<EntityViewDespawnSystem>());
            Assert.AreEqual(typeof(CuvaraViewLifecycleGroup), GroupOf<EntityViewSpawnSystem>());
            CollectionAssert.Contains(UpdateAfterOf<EntityViewSpawnSystem>(), typeof(EntityViewDespawnSystem));
        }

        [Test]
        public void SyncSystem_IsInTheSyncGroup()
        {
            Assert.AreEqual(typeof(CuvaraViewTransformSyncGroup), GroupOf<EntityViewTransformSyncSystem>());
        }

        [Test]
        public void NoViewSystem_LandsInADefaultGroup()
        {
            var systems = new[]
            {
                typeof(EntityViewSpawnSystem),
                typeof(EntityViewDespawnSystem),
                typeof(EntityViewTransformSyncSystem),
            };

            foreach (var system in systems)
            {
                var attributes = system.GetCustomAttributes(typeof(UpdateInGroupAttribute), false);
                Assert.AreEqual(1, attributes.Length, $"{system.Name} must sit in exactly one explicit group");
            }
        }
    }
}
