using System.Reflection;

namespace FaultLedger.Domain.Tests;

public sealed class DomainDependencyTests
{
    [Fact]
    public void DomainAssembly_ReferencesOnlyPermittedBclAssemblies()
    {
        Assembly domain = Assembly.Load("FaultLedger.Domain");
        string[] allowedAssemblies =
        [
            "System.Runtime", "System.Collections", "System.Collections.Immutable",
            "System.Linq", "System.Memory", "System.Numerics.Vectors", "System.ObjectModel",
            "System.Runtime.Numerics", "System.Text.RegularExpressions", "netstandard"
        ];

        Assert.All(domain.GetReferencedAssemblies(), reference =>
            Assert.Contains(reference.Name, allowedAssemblies));
    }
}
