using SandForge.Core;
using SandForge.Domain;
using Xunit;

namespace SandForge.Core.Tests;

public sealed class CompatibilityCatalogTests
{
    [Fact]
    public void CatalogPublishesExplicitVersions()
    {
        var service = new CompatibilityService(new TemplateEngine());
        IReadOnlyList<ContractDescriptor> contracts = service.ListContracts();

        Assert.Equal(5, contracts.Count);
        Assert.Equal(2, service.FindContract("template")?.CurrentVersion);
        Assert.Contains(1, service.FindContract("template")!.DeprecatedVersions);
        Assert.Equal(1, service.FindContract("report")?.CurrentVersion);
        Assert.All(contracts, contract => Assert.NotEmpty(contract.SchemaFile));
    }

    [Fact]
    public void ContractLookupIsCaseInsensitive()
    {
        var service = new CompatibilityService(new TemplateEngine());

        ContractDescriptor? contract = service.FindContract("PACKAGE-MANIFEST");

        Assert.NotNull(contract);
        Assert.Equal(ContractSyntax.Json, contract.Syntax);
        Assert.Equal(1, contract.CurrentVersion);
    }
}
