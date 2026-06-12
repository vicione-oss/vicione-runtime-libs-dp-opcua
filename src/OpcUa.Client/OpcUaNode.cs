using System.Collections.Generic;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

internal sealed class OpcUaNode
{
    internal required ReferenceDescription ReferenceDescription { get; init; }
    internal IReadOnlyCollection<OpcUaNode> Children { get; init; } = [];
}
