using System;
using System.Collections.Generic;

namespace Cuvara.DOTS.Netcode
{
    /// <summary>
    /// Resolves an archetype from an id prefix, with separate defaults for the local player and for
    /// everyone else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generalisation of the reference implementation's <c>id.StartsWith("enemy-")</c>: the same
    /// rule, but the prefix and the archetype it names are constructor arguments, so the package
    /// carries the mechanism and the game carries its vocabulary. A project whose ids do not encode
    /// type at all writes its own <see cref="INetworkArchetypeResolver"/> instead.
    /// </para>
    /// <para>
    /// <b>This class is a workaround, and it has a named exit.</b> Inferring an entity's kind from
    /// the shape of its id is not something anyone would design; it exists only because
    /// <c>IEntityView.Spawn</c> takes <c>(id, isLocal)</c> and the snapshot's
    /// <c>ResolvedEntity.Type</c> is not forwarded through <c>WorldViewBinder</c> — see
    /// <see cref="INetworkArchetypeResolver"/>. If a later <c>com.cuvara.netcode</c> release does
    /// forward the type, the right move is a resolver over <c>Type</c> and the deletion of this
    /// class, not the addition of more prefix rules to it. Treat a growing rule list as the signal
    /// that the seam upstream is the thing to fix.
    /// </para>
    /// <para>
    /// Rules are matched <b>in the order given</b>, first match wins, so a caller listing
    /// <c>"enemy-elite-"</c> before <c>"enemy-"</c> gets what it asked for. Longest-prefix-wins was
    /// rejected: it makes the outcome depend on a comparison the caller cannot see in the list it
    /// wrote.
    /// </para>
    /// <para>
    /// Prefix rules are checked <i>before</i> <c>isLocal</c>, because "this is a monster" and "this
    /// is me" are answers to different questions and a server that ever hands a player an
    /// enemy-shaped id has bigger problems than presentation. The local/remote split then only
    /// decides between two player archetypes.
    /// </para>
    /// </remarks>
    public sealed class PrefixArchetypeResolver : INetworkArchetypeResolver
    {
        /// <summary>One "ids starting with this are that archetype" rule.</summary>
        public readonly struct Rule
        {
            public readonly string Prefix;
            public readonly string ArchetypeName;

            public Rule(string prefix, string archetypeName)
            {
                Prefix = prefix;
                ArchetypeName = archetypeName;
            }
        }

        private readonly Rule[] _rules;
        private readonly string _localArchetype;
        private readonly string _remoteArchetype;

        /// <param name="localArchetype">Archetype for the id equal to the local player's.</param>
        /// <param name="remoteArchetype">Archetype for every other id no rule matched.</param>
        /// <param name="rules">
        /// Prefix rules, most specific first. Null or empty is legal and gives a resolver that only
        /// distinguishes local from remote.
        /// </param>
        public PrefixArchetypeResolver(string localArchetype, string remoteArchetype, params Rule[] rules)
        {
            if (string.IsNullOrEmpty(localArchetype)) throw new ArgumentException("A local archetype name is required.", nameof(localArchetype));
            if (string.IsNullOrEmpty(remoteArchetype)) throw new ArgumentException("A remote archetype name is required.", nameof(remoteArchetype));

            _localArchetype = localArchetype;
            _remoteArchetype = remoteArchetype;
            _rules = rules ?? Array.Empty<Rule>();
        }

        /// <summary>The rules as given, in match order.</summary>
        public IReadOnlyList<Rule> Rules => _rules;

        public bool TryResolve(string id, bool isLocal, out string archetypeName)
        {
            if (!string.IsNullOrEmpty(id))
            {
                for (var i = 0; i < _rules.Length; i++)
                {
                    var rule = _rules[i];
                    if (!string.IsNullOrEmpty(rule.Prefix) &&
                        !string.IsNullOrEmpty(rule.ArchetypeName) &&
                        id.StartsWith(rule.Prefix, StringComparison.Ordinal))
                    {
                        archetypeName = rule.ArchetypeName;
                        return true;
                    }
                }
            }

            archetypeName = isLocal ? _localArchetype : _remoteArchetype;
            return true;
        }
    }
}
