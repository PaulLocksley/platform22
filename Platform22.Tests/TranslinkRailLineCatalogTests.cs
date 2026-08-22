namespace Platform22.Tests;

using Platform22.Tui;
using Xunit;

public sealed class TranslinkRailLineCatalogTests
{
    [Fact]
    public void GetLinesReturnsNetworkMapRailBranches()
    {
        var lines = TranslinkRailLineCatalog.GetLines();

        Assert.Collection(lines,
            line => Assert.Equal("Airport / Gold Coast", line.Name),
            line => Assert.Equal("Beenleigh / Ferny Grove", line.Name),
            line => Assert.Equal("Caboolture / Sunshine Coast", line.Name),
            line => Assert.Equal("Cleveland / Shorncliffe", line.Name),
            line => Assert.Equal("Doomben", line.Name),
            line => Assert.Equal("Ipswich / Rosewood", line.Name),
            line => Assert.Equal("Redcliffe Peninsula / Springfield", line.Name));
    }

    [Fact]
    public void GetShortNameAnyPartsParsesRouteCodes()
    {
        var parts = TranslinkRailLineCatalog.GetShortNameAnyParts("translink:short-name-any:BD,VL");

        Assert.Equal(["BD", "VL"], parts);
    }
}
