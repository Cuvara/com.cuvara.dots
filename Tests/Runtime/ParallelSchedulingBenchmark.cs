using System.Text;
using Cuvara.DOTS.Simulation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
// Aliased rather than importing System.Diagnostics: that namespace also has a Debug, and importing
// it alongside UnityEngine makes every Debug.Log in this file CS0104-ambiguous.
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Cuvara.DOTS.Tests
{
    /// <summary>
    /// Measures the parallel schedule against the single-threaded one, so "this is faster" is a
    /// number rather than an argument.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The comparison is the same job run two ways</b> — <c>Run()</c> versus
    /// <c>ScheduleParallel().Complete()</c> — not a job against a hand-written loop. Both paths
    /// execute byte-identical Bursted code over identical chunks, so the difference measured is
    /// exactly what the change bought: worker parallelism, minus scheduling overhead. Comparing
    /// against a `foreach` would fold in codegen differences and measure the wrong thing.
    /// </para>
    /// <para>
    /// <b>These numbers are a shape, not a spec.</b> They come from whatever machine ran them — a
    /// shared CI runner is capped at a handful of cores and is noisy — so the useful output is the
    /// <i>crossover</i>: the entity count below which scheduling costs more than it saves. That
    /// point moves with core count; the fact that one exists does not.
    /// </para>
    /// <para>
    /// Nothing here asserts a timing. A performance assertion on a shared runner is a flaky test,
    /// and a flaky test in a gate is worse than no measurement — it teaches people to re-run until
    /// green. The timings are logged; the assertions are about correctness.
    /// </para>
    /// </remarks>
    public sealed class ParallelSchedulingBenchmark
    {
        private static readonly int[] EntityCounts = { 64, 256, 1024, 4096, 16384, 65536 };

        private const int Warmup = 5;
        private const int Iterations = 30;

        private World _world;
        private EntityManager _entityManager;

        [SetUp]
        public void SetUp()
        {
            _world = new World("Cuvara.DOTS.ParallelBenchmark");
            _entityManager = _world.EntityManager;
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private void Populate(int count)
        {
            _entityManager.DestroyEntity(_entityManager.UniversalQuery);

            var archetype = _entityManager.CreateArchetype(
                typeof(LocalTransform), typeof(SpinSpeed), typeof(MoveData));

            using var entities = _entityManager.CreateEntity(archetype, count, Allocator.Temp);
            for (var i = 0; i < entities.Length; i++)
            {
                _entityManager.SetComponentData(entities[i], LocalTransform.FromPosition(i, 0f, 0f));
                _entityManager.SetComponentData(entities[i], new SpinSpeed { RadiansPerSecond = 1f + (i % 7) });
                _entityManager.SetComponentData(entities[i], new MoveData
                {
                    Velocity = new float3(1f, 0f, 0.5f),
                    BoundsMin = new float3(-1000f, -1000f, -1000f),
                    BoundsMax = new float3(1000f, 1000f, 1000f),
                });
            }
        }

        [Test]
        public void SpinJob_ParallelVersusSingleThreaded()
        {
            Report("SpinJob", count =>
            {
                Populate(count);
                using var query = _entityManager.CreateEntityQuery(
                    ComponentType.ReadWrite<LocalTransform>(), ComponentType.ReadOnly<SpinSpeed>());

                return (
                    Time(() => new SpinJob { DeltaTime = 0.016f }.Run(query)),
                    Time(() => new SpinJob { DeltaTime = 0.016f }.ScheduleParallel(query, default).Complete()));
            });
        }

        [Test]
        public void MoveBounceJob_ParallelVersusSingleThreaded()
        {
            Report("MoveBounceJob", count =>
            {
                Populate(count);
                using var query = _entityManager.CreateEntityQuery(
                    ComponentType.ReadWrite<LocalTransform>(), ComponentType.ReadWrite<MoveData>());

                return (
                    Time(() => new MoveBounceJob { DeltaTime = 0.016f }.Run(query)),
                    Time(() => new MoveBounceJob { DeltaTime = 0.016f }.ScheduleParallel(query, default).Complete()));
            });
        }

        /// <summary>
        /// The determinism check the parallel schedule has to earn: identical input through both
        /// paths must give bit-identical output.
        /// </summary>
        /// <remarks>
        /// Not a formality. A parallel job whose result depends on iteration order is a bug that
        /// reproduces roughly one run in ten, and these systems feed positions that a predictor may
        /// later reconcile against. Bit-identical rather than approximately equal, because "close
        /// enough" is how a drift bug survives its own test.
        /// </remarks>
        [Test]
        public void BothSchedules_ProduceBitIdenticalResults()
        {
            const int count = 4096;

            float3[] Run(bool parallel)
            {
                Populate(count);
                using var query = _entityManager.CreateEntityQuery(
                    ComponentType.ReadWrite<LocalTransform>(), ComponentType.ReadWrite<MoveData>());

                for (var step = 0; step < 8; step++)
                {
                    var job = new MoveBounceJob { DeltaTime = 0.016f };
                    if (parallel) job.ScheduleParallel(query, default).Complete();
                    else job.Run(query);
                }

                using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                var positions = new float3[transforms.Length];
                for (var i = 0; i < transforms.Length; i++) positions[i] = transforms[i].Position;
                return positions;
            }

            var single = Run(parallel: false);
            var parallelResult = Run(parallel: true);

            Assert.AreEqual(single.Length, parallelResult.Length);
            for (var i = 0; i < single.Length; i++)
            {
                Assert.IsTrue(single[i].Equals(parallelResult[i]),
                    $"entity {i} diverged: single-threaded {single[i]} vs parallel {parallelResult[i]}");
            }
        }

        private static double Time(System.Action action)
        {
            for (var i = 0; i < Warmup; i++) action();

            var clock = Stopwatch.StartNew();
            for (var i = 0; i < Iterations; i++) action();
            clock.Stop();

            return clock.Elapsed.TotalMilliseconds / Iterations;
        }

        private static void Report(string name, System.Func<int, (double Single, double Parallel)> measure)
        {
            var report = new StringBuilder();
            report.AppendLine($"[benchmark] {name} — ms per invocation, {Iterations} iterations after {Warmup} warmup");
            report.AppendLine($"[benchmark] processors reported by the runtime: {SystemInfo.processorCount}");
            report.AppendLine("[benchmark]   entities |   Run() |  Parallel |  speedup");

            var crossover = -1;
            foreach (var count in EntityCounts)
            {
                var (single, parallel) = measure(count);
                var speedup = parallel > 0d ? single / parallel : 0d;
                if (crossover < 0 && speedup > 1d) crossover = count;

                report.AppendLine(
                    $"[benchmark] {count,10} | {single,7:F4} | {parallel,9:F4} | {speedup,7:F2}x");
            }

            report.AppendLine(crossover < 0
                ? "[benchmark] crossover: NONE — the parallel schedule never won at these counts on this machine"
                : $"[benchmark] crossover: parallel first wins at {crossover} entities on this machine");

            Debug.Log(report.ToString());
        }
    }
}
