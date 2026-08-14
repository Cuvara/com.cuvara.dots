using NUnit.Framework;

namespace Cuvara.DOTS.Tests.Editor
{
    /// <summary>
    /// Smoke test proving the runtime assembly is compiled and referenceable from edit-mode tests.
    /// Replace once real editor code lands.
    /// </summary>
    public sealed class PackageMarkerEditorTests
    {
        [Test]
        public void PackageName_MatchesManifest()
        {
            Assert.AreEqual("com.cuvara.dots", PackageMarker.PackageName);
        }
    }
}
