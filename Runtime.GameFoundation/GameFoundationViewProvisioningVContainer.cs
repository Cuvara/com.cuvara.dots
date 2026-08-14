#if CUVARA_DOTS_UNIT_POOLING && CUVARA_DOTS_UNIT_RESOURCES && CUVARA_DOTS_UNITASK && CUVARA_DOTS_VCONTAINER
using Cuvara.DOTS.Provisioning;
using VContainer;

namespace Cuvara.DOTS.GameFoundation
{
    /// <summary>
    /// Registers the GameFoundation-backed view provider, mirroring
    /// <c>GameFoundationVContainer.RegisterGameFoundation</c>.
    /// </summary>
    /// <remarks>
    /// Lives in the GameFoundation assembly rather than the VContainer one so that a project with
    /// VContainer but no GameFoundation still compiles: the core registration extension must not
    /// reference UniT types. Call this <b>after</b> <c>RegisterGameFoundation</c> — it resolves
    /// <c>IAssetsManager</c> and <c>IObjectPoolManager</c> that call registers.
    /// </remarks>
    public static class GameFoundationViewProvisioningVContainer
    {
        /// <summary>
        /// Registers <see cref="GameFoundationViewAssetProvider"/> as <see cref="IViewAssetProvider"/>
        /// and a <see cref="ChunkViewProvisioner"/> over it.
        /// </summary>
        public static IContainerBuilder RegisterGameFoundationViewProvisioning(this IContainerBuilder builder, Lifetime lifetime = Lifetime.Singleton)
        {
            builder.Register<GameFoundationViewAssetProvider>(lifetime).As<IViewAssetProvider>();
            builder.Register<ChunkViewProvisioner>(lifetime).AsSelf();
            return builder;
        }
    }
}
#endif
