#if CUVARA_DOTS_VCONTAINER
using Cuvara.DOTS.Provisioning;
using Cuvara.DOTS.Views;
using Unity.Entities;
using UnityEngine;
using VContainer;

namespace Cuvara.DOTS.DI
{
    /// <summary>
    /// VContainer registration for the DOTS view layer, mirroring
    /// <c>GameFoundationVContainer.RegisterGameFoundation</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole DI surface of the package, and it is deliberately in its own optional
    /// assembly. The core types take plain constructors and know nothing about VContainer, so
    /// <c>com.cuvara.dots</c> installs and compiles against its four pinned Unity dependencies
    /// alone.
    /// </para>
    /// <para>
    /// The caller supplies <see cref="IViewAssetProvider"/> — either
    /// <c>RegisterGameFoundationViewProvisioning</c> from the GameFoundation assembly, or their
    /// own. This assembly must not know which, or it would need a UniT reference and stop being
    /// independently optional.
    /// </para>
    /// </remarks>
    public static class DotsViewsVContainer
    {
        /// <summary>
        /// Registers <see cref="EntityViewRegistry"/> and installs it into
        /// <paramref name="world"/> (defaulting to <c>World.DefaultGameObjectInjectionWorld</c>)
        /// once the container is built.
        /// </summary>
        /// <param name="viewRoot">Optional parent for spawned views. Null parents to the scene root.</param>
        public static IContainerBuilder RegisterDotsViews(this IContainerBuilder builder, Transform viewRoot = null, World world = null)
        {
            builder.Register(container => new EntityViewRegistry(container.Resolve<IViewAssetProvider>(), viewRoot), Lifetime.Singleton)
                .AsSelf();

            // Deferred to build time: the DOTS world and the container are created independently,
            // and resolving the registry during registration would invert that.
            builder.RegisterBuildCallback(container =>
            {
                var target = world ?? World.DefaultGameObjectInjectionWorld;
                if (target == null)
                {
                    Debug.LogWarning("[Cuvara.DOTS] No DOTS world available — entity views are disabled.");
                    return;
                }

                DotsViewBootstrap.Install(target, container.Resolve<EntityViewRegistry>());
            });

            return builder;
        }
    }
}
#endif
