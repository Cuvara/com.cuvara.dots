using NUnit.Framework;

namespace Cuvara.DOTS.Tests.Editor
{
    /// <summary>
    /// TEMPORARY. Proves the CI gate can go red before anyone trusts it green.
    /// </summary>
    /// <remarks>
    /// A gate that has only ever passed is indistinguishable from a gate that
    /// cannot fail — which is exactly what com.cuvara.netcode had. This test
    /// fails on purpose so the first run of this workflow is red, and it is
    /// deleted in the next commit so the second run is green. Both runs stay in
    /// the PR's check history as the evidence.
    /// </remarks>
    public sealed class CiGateProofTests
    {
        [Test]
        public void DeliberateFailure_ProvesTheGateCanGoRed()
        {
            Assert.Fail("Intentional. Removed in the next commit — see the class remarks.");
        }
    }
}
