using NUnit.Framework;

namespace Cuvara.DOTS.Tests
{
    /// <summary>
    /// Smoke test proving the runtime assembly is compiled and referenceable from play-mode tests.
    /// Replace once real runtime code lands.
    /// </summary>
    public sealed class PackageMarkerTests
    {
        [Test]
        public void PackageName_MatchesManifest()
        {
            Assert.AreEqual("com.cuvara.dots", PackageMarker.PackageName);
        }
    }
}
