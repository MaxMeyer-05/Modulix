using Modulix.Extensions;

namespace Modulix.Tests.Extensions;

[Trait("Category", "Extensions")]
[Trait("SubCategory", "PortExtension")]
public class PortExtensionTests
{
    [Theory]
    [InlineData(1024)]
    [InlineData(8080)]
    [InlineData(49151)]
    public void GetAvailablePort_UnusedSelectedPort_ReturnsSelectedPort(int selectedPort)
    {
        Assert.Equal(selectedPort, PortExtension.GetAvailablePort([1025, 9000], selectedPort));
    }

    [Fact]
    public void GetAvailablePort_SelectedPortAlreadyUsed_ThrowsInsteadOfChoosingAnotherPort()
    {
        var exception = Assert.Throws<ArgumentException>(() => PortExtension.GetAvailablePort([8080], 8080));

        Assert.Contains("8080", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1024)]
    [InlineData(49150)]
    [InlineData(49151)]
    public void GetAvailablePort_AutomaticSelection_ReturnsFirstFreePortIncludingUpperBoundary(int firstFreePort)
    {
        var usedPorts = firstFreePort == 0 ? [] : Enumerable.Range(1024, firstFreePort - 1024).ToArray();

        var result = PortExtension.GetAvailablePort(usedPorts, 0);

        Assert.Equal(firstFreePort == 0 ? 1024 : firstFreePort, result);
    }

    [Fact]
    public void GetAvailablePort_AllPortsUsed_ThrowsInsteadOfReturningInvalidPort()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PortExtension.GetAvailablePort(Enumerable.Range(1024, 49151 - 1024 + 1), 0));
    }

    [Fact]
    public void GetAvailablePort_DuplicateAndOutOfRangeUsedPorts_DoNotSkipFreePorts()
    {
        Assert.Equal(1025, PortExtension.GetAvailablePort([80, 1024, 1024, 65535], 0));
    }
}