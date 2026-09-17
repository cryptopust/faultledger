using System.Diagnostics;
using System.Text.Json;

namespace FaultLedger.Architecture.Tests;

public sealed class ProjectBoundaryTests
{
    [Theory]
    [InlineData("Domain", new string[0])]
    [InlineData("Application", new[] { "FaultLedger.Domain" })]
    [InlineData("Infrastructure", new[] { "FaultLedger.Application", "FaultLedger.Domain" })]
    [InlineData("Api", new[] { "FaultLedger.Application", "FaultLedger.Infrastructure" })]
    [InlineData("SimulatedConsumer", new string[0])]
    public async Task ProductionProject_EvaluatedReferences_RespectDependencyDirection(
        string projectName, string[] expectedReferences)
    {
        using JsonDocument evaluation = await EvaluateProjectAsync(projectName);
        JsonElement items = evaluation.RootElement.GetProperty("Items");
        string[] actualReferences = items.GetProperty("ProjectReference").EnumerateArray()
            .Select(item => Path.GetFileNameWithoutExtension(item.GetProperty("FullPath").GetString()
                ?? throw new InvalidDataException("Project reference path is missing.")))
            .Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expectedReferences.Order(StringComparer.Ordinal), actualReferences);

        if (projectName is "Domain" or "Application")
        {
            Assert.Empty(items.GetProperty("PackageReference").EnumerateArray());
            Assert.All(items.GetProperty("FrameworkReference").EnumerateArray(), item =>
                Assert.Equal("Microsoft.NETCore.App", item.GetProperty("Identity").GetString()));
        }

        string[] packages = items.GetProperty("PackageReference").EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString() ?? string.Empty).ToArray();
        if (projectName == "Infrastructure")
        {
            Assert.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", packages);
            Assert.Contains("Microsoft.EntityFrameworkCore", packages);
            Assert.Contains("Microsoft.EntityFrameworkCore.Relational", packages);
            Assert.Contains("Microsoft.EntityFrameworkCore.Design", packages);
        }
        else if (projectName != "SimulatedConsumer")
        {
            Assert.DoesNotContain(packages, package => package.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || package.StartsWith("Npgsql", StringComparison.Ordinal));
        }

        JsonElement properties = evaluation.RootElement.GetProperty("Properties");
        Assert.Equal("net10.0", properties.GetProperty("TargetFramework").GetString());
        Assert.Equal("enable", properties.GetProperty("Nullable").GetString());
        Assert.Equal("enable", properties.GetProperty("ImplicitUsings").GetString());
        foreach (string property in new[]
                 {
                     "TreatWarningsAsErrors", "Deterministic", "EnableNETAnalyzers",
                     "EnforceCodeStyleInBuild", "ManagePackageVersionsCentrally"
                 })
        {
            Assert.Equal("true", properties.GetProperty(property).GetString());
        }
    }

    private static async Task<JsonDocument> EvaluateProjectAsync(string projectName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FaultLedger.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add($"src/FaultLedger.{projectName}/FaultLedger.{projectName}.csproj");
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-getItem:ProjectReference,PackageReference,FrameworkReference");
        startInfo.ArgumentList.Add("-getProperty:TargetFramework,Nullable,ImplicitUsings,TreatWarningsAsErrors,Deterministic,EnableNETAnalyzers,EnforceCodeStyleInBuild,ManagePackageVersionsCentrally");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start());
        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            string diagnostics = await error;
            Assert.True(process.ExitCode == 0, diagnostics);
            return JsonDocument.Parse(await output);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }
}
