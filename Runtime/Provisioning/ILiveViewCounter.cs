namespace Cuvara.DOTS.Provisioning
{
    /// <summary>
    /// Answers "is anything still standing on this asset key?" for the provisioner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exists so <see cref="ChunkViewProvisioner"/> can refuse a release that would destroy live
    /// views, without the provisioning layer knowing what a view or an entity is. The only
    /// implementation is <c>EntityViewRegistry</c>; the interface keeps the dependency pointing the
    /// right way, since provisioning is the lower layer.
    /// </para>
    /// <para>
    /// Optional: a provisioner constructed without one cannot detect live views and releases
    /// unconditionally, which is the pre-0.5.0 behaviour and is documented on the constructor as
    /// unsafe for streaming.
    /// </para>
    /// </remarks>
    public interface ILiveViewCounter
    {
        /// <summary>Number of live view instances currently spawned from <paramref name="key"/>.</summary>
        int CountLiveViews(string key);
    }
}
