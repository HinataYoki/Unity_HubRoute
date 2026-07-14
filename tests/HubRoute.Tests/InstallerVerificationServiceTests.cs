using HubRoute.Services;

namespace HubRoute.Tests;

public sealed class InstallerVerificationServiceTests
{
    /// <summary>Accepts current and historical Unity Technologies publisher variants.</summary>
    [Theory]
    [InlineData("Unity Technologies SF")]
    [InlineData("Developer ID Application: Unity Technologies ApS (TEST123)")]
    public void IsExpectedPublisher_UnityName_ReturnsTrue(string publisher)
    {
        Assert.True(InstallerVerificationService.IsExpectedPublisher(publisher));
    }

    /// <summary>Rejects unrelated publishers and missing signature descriptions.</summary>
    [Theory]
    [InlineData("Example Software Inc.")]
    [InlineData("")]
    [InlineData(null)]
    public void IsExpectedPublisher_UnrelatedName_ReturnsFalse(string? publisher)
    {
        Assert.False(InstallerVerificationService.IsExpectedPublisher(publisher));
    }

    /// <summary>Rejects an unsigned local file on every supported host platform.</summary>
    [Fact]
    public async Task VerifyAsync_UnsignedFile_ReturnsUntrusted()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.exe");
        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await File.WriteAllTextAsync(filePath, "not an executable", cancellationToken);
            var service = new InstallerVerificationService();

            var result = await service.VerifyAsync(filePath, cancellationToken);

            Assert.False(result.IsTrusted);
            Assert.False(string.IsNullOrWhiteSpace(result.Description));
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
