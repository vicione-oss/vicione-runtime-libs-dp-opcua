using System;
using System.Collections.Generic;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// A node that addresses the OPC UA value of its parent instead of a node of its own.
/// </summary>
/// <param name="Node">The configured node.</param>
/// <param name="Kind">Which part of the parent value it addresses.</param>
/// <param name="Channels">Every channel the engine exchanges its value on: one per direction the
/// tree links it in, because the engine mints a connector for each, and none when it links none.</param>
/// <param name="ParentChannel">The channel the parent value is exchanged on.</param>
/// <param name="ValueType">The type the value is converted from and to.</param>
internal sealed record EnvelopeChild(INode Node, EnvelopeChildKind Kind, IReadOnlyList<string> Channels, string ParentChannel, Type ValueType)
{
    /// <summary>
    /// The channel this port exchanges the value on. A port is handed only the channels of the
    /// direction it runs in, so it sees at most one.
    /// </summary>
    internal string Channel => Channels.Count == 0 ? string.Empty : Channels[0];

    internal static EnvelopeChild Create(INode node, EnvelopeChildKind kind, IReadOnlyList<string> channels, string parentChannel)
        => new(node, kind, channels, parentChannel, node.ValueType ?? DefaultValueTypeOf(kind));

    private static Type DefaultValueTypeOf(EnvelopeChildKind kind)
        => kind switch
        {
            EnvelopeChildKind.StatusCode => typeof(uint),
            EnvelopeChildKind.SourceTimestamp or EnvelopeChildKind.ServerTimestamp => typeof(DateTime),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown envelope child kind."),
        };
}
