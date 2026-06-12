using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace ViciOne.Suite.DataPort;

[CollectionDefinition(Name)]
[SuppressMessage("Maintainability", "CA1515:Erwägen Sie, öffentliche Typen intern zu machen.")]
public sealed class OpcUaTestEnvironment : ICollectionFixture<OpcUaTestSystem>
{
    internal const string Name = nameof(OpcUaTestEnvironment);
}
