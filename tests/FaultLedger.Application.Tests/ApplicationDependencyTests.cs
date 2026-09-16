using System.Reflection;

namespace FaultLedger.Application.Tests;

public sealed class ApplicationDependencyTests
{
    [Fact]
    public void ApplicationAssembly_DoesNotReferenceHostOrInfrastructure()
    {
        Assembly application = Assembly.Load("FaultLedger.Application");
        string[] forbiddenPrefixes =
        [
            "FaultLedger.Api", "FaultLedger.Infrastructure", "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore", "Npgsql"
        ];

        Assert.All(application.GetReferencedAssemblies(), reference =>
            Assert.DoesNotContain(forbiddenPrefixes, prefix =>
                (reference.Name ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal)));
    }
}
