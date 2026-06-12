using AwesomeAssertions;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class OpcUaServer_AreCredentialsValid
{
    [Fact]
    public void Correct_credentials_return_true()
    {
        var result = OpcUaServer.AreCredentialsValid("admin", "admin", "secret", "secret");

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("admin", "wrong", "secret", "secret")]
    [InlineData("admin", "admin", "secret", "wrong")]
    [InlineData("admin", "wrong", "secret", "wrong")]
    [InlineData("admin", "", "secret", "secret")]
    [InlineData("admin", "admin", "secret", "")]
    public void Incorrect_credentials_return_false(string expectedUser, string actualUser, string expectedPassword, string actualPassword)
    {
        var result = OpcUaServer.AreCredentialsValid(expectedUser, actualUser, expectedPassword, actualPassword);

        result.Should().BeFalse();
    }

    [Fact]
    public void Null_credentials_matching_return_true()
    {
        var result = OpcUaServer.AreCredentialsValid(null, null, null, null);

        result.Should().BeTrue();
    }

    [Fact]
    public void Null_expected_vs_empty_actual_return_true()
    {
        var result = OpcUaServer.AreCredentialsValid(null, "", null, "");

        result.Should().BeTrue();
    }

    [Fact]
    public void Null_password_vs_non_null_password_returns_false()
    {
        var result = OpcUaServer.AreCredentialsValid("admin", "admin", null, "secret");

        result.Should().BeFalse();
    }
}
