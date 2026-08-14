using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace Cuvara.DOTS.Views
{
    /// <summary>Reads chunk memory into blittable samples. Order of samples is not meaningful.</summary>
    [BurstCompile]
    internal partial struct CollectViewTransformsJob : IJobEntity
    {
        public NativeList<ViewTransformSample>.ParallelWriter Samples;

        private void Execute(in EntityViewLink link, in LocalTransform transform)
        {
            Samples.AddNoResize(new ViewTransformSample
            {
                ViewId = link.ViewId,
                Position = transform.Position,
                Rotation = transform.Rotation,
                Scale = transform.Scale,
            });
        }
    }
}
