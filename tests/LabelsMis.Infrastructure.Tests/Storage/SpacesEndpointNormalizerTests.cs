using LabelsMis.Infrastructure.Storage;

namespace LabelsMis.Infrastructure.Tests.Storage;

public class SpacesEndpointNormalizerTests
{
    [Theory]
    [InlineData("nyc3.digitaloceanspaces.com", "my-bucket", "https://nyc3.digitaloceanspaces.com")]
    [InlineData("https://nyc3.digitaloceanspaces.com/", "my-bucket", "https://nyc3.digitaloceanspaces.com")]
    [InlineData("https://my-bucket.nyc3.digitaloceanspaces.com", "my-bucket", "https://nyc3.digitaloceanspaces.com")]
    public void NormalizeServiceUrl_FormatsEndpoint(string input, string bucket, string expected) =>
        SpacesEndpointNormalizer.NormalizeServiceUrl(input, bucket).Should().Be(expected);

    [Fact]
    public void ParseRegionFromServiceUrl_ReturnsDatacenter() =>
        SpacesEndpointNormalizer.ParseRegionFromServiceUrl("https://nyc3.digitaloceanspaces.com")
            .Should().Be("nyc3");
}
