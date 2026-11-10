using PKBank.App.Services;
using Xunit;

namespace PKBank.App.Smoke.Tests;

public sealed class UpdateTests
{
    [Theory]
    [InlineData("0.1.0", "0.2.0", true)]
    [InlineData("0.1.0", "v0.1.1", true)]
    [InlineData("0.2.0", "0.2.0", false)]
    [InlineData("0.2.0", "0.1.9", false)]
    [InlineData("1.0.0", "0.9.9", false)]
    [InlineData("2.0.0", "10.0.0", true)]
    public void VersionCompare(string current, string latest, bool expected)
        => Assert.Equal(expected, AppUpdateService.IsNewer(current, latest));
}
