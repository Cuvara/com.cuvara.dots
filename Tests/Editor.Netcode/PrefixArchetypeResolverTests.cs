using System;
using Cuvara.DOTS.Netcode;
using NUnit.Framework;

namespace Cuvara.DOTS.Tests.Netcode
{
    /// <summary>
    /// The generalisation of the reference implementation's hardcoded <c>"enemy-"</c> check.
    /// </summary>
    public sealed class PrefixArchetypeResolverTests
    {
        private static PrefixArchetypeResolver Resolver(params PrefixArchetypeResolver.Rule[] rules) =>
            new PrefixArchetypeResolver("player-local", "player-remote", rules);

        [Test]
        public void PrefixRule_Wins_OverTheLocalRemoteDefault()
        {
            var resolver = Resolver(new PrefixArchetypeResolver.Rule("enemy-", "goblin"));

            Assert.IsTrue(resolver.TryResolve("enemy-17", isLocal: false, out var name));
            Assert.AreEqual("goblin", name);
        }

        [Test]
        public void PrefixRule_Wins_EvenWhenTheIdIsFlaggedLocal()
        {
            // "this is a monster" and "this is me" answer different questions; a server handing the
            // local player an enemy-shaped id has a bigger problem than presentation, and silently
            // rendering it as a player would hide it.
            var resolver = Resolver(new PrefixArchetypeResolver.Rule("enemy-", "goblin"));

            Assert.IsTrue(resolver.TryResolve("enemy-17", isLocal: true, out var name));
            Assert.AreEqual("goblin", name);
        }

        [Test]
        public void Rules_MatchInTheOrderGiven_NotLongestFirst()
        {
            // Declaration order is the contract, because it is the thing the caller can see in the
            // list it wrote. A caller wanting the specific rule to win puts it first.
            var specificFirst = Resolver(
                new PrefixArchetypeResolver.Rule("enemy-elite-", "goblin-elite"),
                new PrefixArchetypeResolver.Rule("enemy-", "goblin"));

            Assert.IsTrue(specificFirst.TryResolve("enemy-elite-3", isLocal: false, out var elite));
            Assert.AreEqual("goblin-elite", elite);

            var generalFirst = Resolver(
                new PrefixArchetypeResolver.Rule("enemy-", "goblin"),
                new PrefixArchetypeResolver.Rule("enemy-elite-", "goblin-elite"));

            Assert.IsTrue(generalFirst.TryResolve("enemy-elite-3", isLocal: false, out var shadowed));
            Assert.AreEqual("goblin", shadowed, "the earlier rule shadows the later one, by design");
        }

        [Test]
        public void NoRuleMatches_FallsBackToLocalOrRemote()
        {
            var resolver = Resolver(new PrefixArchetypeResolver.Rule("enemy-", "goblin"));

            Assert.IsTrue(resolver.TryResolve("a-uuid", isLocal: true, out var local));
            Assert.AreEqual("player-local", local);

            Assert.IsTrue(resolver.TryResolve("a-uuid", isLocal: false, out var remote));
            Assert.AreEqual("player-remote", remote);
        }

        [Test]
        public void NoRulesAtAll_IsLegal()
        {
            Assert.IsTrue(Resolver().TryResolve("a-uuid", isLocal: false, out var name));
            Assert.AreEqual("player-remote", name);
        }

        [Test]
        public void MissingDefaults_ThrowAtConstruction_NotAtSpawnTime()
        {
            // A resolver with no fallback fails on the first unmatched id, which in a real session is
            // minutes after the mistake was made and nowhere near it.
            Assert.Throws<ArgumentException>(() => new PrefixArchetypeResolver(null, "player-remote"));
            Assert.Throws<ArgumentException>(() => new PrefixArchetypeResolver("player-local", ""));
        }
    }
}
