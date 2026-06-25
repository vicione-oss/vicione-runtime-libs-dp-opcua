using System.Collections.Generic;
using AwesomeAssertions;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class TransportQuotasConfiguration_Defaults
{
    [Fact]
    public void DefaultMaxStringLength_is_1MB()
        => TransportQuotasConfiguration.DefaultMaxStringLength.Should().Be(1_048_576);

    [Fact]
    public void DefaultMaxByteStringLength_is_4MB()
        => TransportQuotasConfiguration.DefaultMaxByteStringLength.Should().Be(4_194_304);

    [Fact]
    public void DefaultMaxArrayLength_is_65535()
        => TransportQuotasConfiguration.DefaultMaxArrayLength.Should().Be(65_535);

    [Fact]
    public void DefaultMaxMessageSize_is_4MB()
        => TransportQuotasConfiguration.DefaultMaxMessageSize.Should().Be(4_194_304);

    [Fact]
    public void DefaultMaxBufferSize_is_65535()
        => TransportQuotasConfiguration.DefaultMaxBufferSize.Should().Be(65_535);

    [Fact]
    public void DefaultOperationTimeout_is_120000ms()
        => TransportQuotasConfiguration.DefaultOperationTimeout.Should().Be(120_000);

    [Fact]
    public void DefaultMaxSessionCount_is_100()
        => TransportQuotasConfiguration.DefaultMaxSessionCount.Should().Be(100);

    [Fact]
    public void DefaultMaxSubscriptionCount_is_500()
        => TransportQuotasConfiguration.DefaultMaxSubscriptionCount.Should().Be(500);

    [Fact]
    public void DefaultMinSessionTimeout_is_10000ms()
        => TransportQuotasConfiguration.DefaultMinSessionTimeout.Should().Be(10_000);

    [Fact]
    public void DefaultMaxSessionTimeout_is_600000ms()
        => TransportQuotasConfiguration.DefaultMaxSessionTimeout.Should().Be(600_000);
}

public sealed class TransportQuotasConfiguration_EnvironmentVariables
{
    private static TransportQuotasConfiguration CreateConfig(Dictionary<string, string> envVars)
        => new(name => envVars.TryGetValue(name, out var value) ? value : null);

    [Fact]
    public void MaxStringLength_returns_env_var_value_when_set()
    {
        var config = CreateConfig(new() { [TransportQuotasConfiguration.MaxStringLengthEnvVar] = "2097152" });

        config.MaxStringLength.Should().Be(2_097_152);
    }

    [Fact]
    public void MaxStringLength_returns_default_when_env_var_is_invalid()
    {
        var config = CreateConfig(new() { [TransportQuotasConfiguration.MaxStringLengthEnvVar] = "not-a-number" });

        config.MaxStringLength.Should().Be(TransportQuotasConfiguration.DefaultMaxStringLength);
    }

    [Fact]
    public void MaxMessageSize_returns_env_var_value_when_set()
    {
        var config = CreateConfig(new() { [TransportQuotasConfiguration.MaxMessageSizeEnvVar] = "8388608" });

        config.MaxMessageSize.Should().Be(8_388_608);
    }

    [Fact]
    public void MaxSessionCount_returns_env_var_value_when_set()
    {
        var config = CreateConfig(new() { [TransportQuotasConfiguration.MaxSessionCountEnvVar] = "50" });

        config.MaxSessionCount.Should().Be(50);
    }

    [Fact]
    public void MaxSubscriptionCount_returns_env_var_value_when_set()
    {
        var config = CreateConfig(new() { [TransportQuotasConfiguration.MaxSubscriptionCountEnvVar] = "250" });

        config.MaxSubscriptionCount.Should().Be(250);
    }

    [Fact]
    public void OperationTimeout_returns_env_var_value_when_set()
    {
        var config = CreateConfig(new() { [TransportQuotasConfiguration.OperationTimeoutEnvVar] = "60000" });

        config.OperationTimeout.Should().Be(60_000);
    }

    [Fact]
    public void MaxSessionTimeout_returns_env_var_value_when_set()
    {
        var config = CreateConfig(new() { [TransportQuotasConfiguration.MaxSessionTimeoutEnvVar] = "300000" });

        config.MaxSessionTimeout.Should().Be(300_000);
    }

    [Fact]
    public void MinSessionTimeout_returns_env_var_value_when_set()
    {
        var config = CreateConfig(new() { [TransportQuotasConfiguration.MinSessionTimeoutEnvVar] = "5000" });

        config.MinSessionTimeout.Should().Be(5_000);
    }
}

public sealed class OpcUaSetup_CreateConfiguration_TransportQuotas
{
    private static TransportQuotasConfiguration CreateConfig(Dictionary<string, string>? envVars = null)
        => new(name => envVars is not null && envVars.TryGetValue(name, out var value) ? value : null);

    private static OpcUaServerDataPortProperties CreateDefaultProperties()
        => new(new OpcUaServerDataPortCommunication());

    [Fact]
    public void TransportQuotas_are_explicitly_set_with_default_values()
    {
        var configuration = OpcUaSetup.CreateConfiguration(CreateDefaultProperties(), CreateConfig());

        configuration.TransportQuotas.MaxStringLength.Should().Be(TransportQuotasConfiguration.DefaultMaxStringLength);
        configuration.TransportQuotas.MaxByteStringLength.Should().Be(TransportQuotasConfiguration.DefaultMaxByteStringLength);
        configuration.TransportQuotas.MaxArrayLength.Should().Be(TransportQuotasConfiguration.DefaultMaxArrayLength);
        configuration.TransportQuotas.MaxMessageSize.Should().Be(TransportQuotasConfiguration.DefaultMaxMessageSize);
        configuration.TransportQuotas.MaxBufferSize.Should().Be(TransportQuotasConfiguration.DefaultMaxBufferSize);
        configuration.TransportQuotas.OperationTimeout.Should().Be(TransportQuotasConfiguration.DefaultOperationTimeout);
    }

    [Fact]
    public void TransportQuotas_are_null_when_disabled()
    {
        var properties = new OpcUaServerDataPortProperties(new OpcUaServerDataPortCommunication { TransportQuotas = false });

        var configuration = OpcUaSetup.CreateConfiguration(properties, CreateConfig());

        configuration.TransportQuotas.Should().BeNull();
    }

    [Fact]
    public void TransportQuotas_respects_environment_variable_overrides()
    {
        var config = CreateConfig(new()
        {
            [TransportQuotasConfiguration.MaxStringLengthEnvVar] = "2097152",
            [TransportQuotasConfiguration.MaxMessageSizeEnvVar] = "8388608",
        });

        var configuration = OpcUaSetup.CreateConfiguration(CreateDefaultProperties(), config);

        configuration.TransportQuotas.MaxStringLength.Should().Be(2_097_152);
        configuration.TransportQuotas.MaxMessageSize.Should().Be(8_388_608);
        configuration.TransportQuotas.MaxByteStringLength.Should().Be(TransportQuotasConfiguration.DefaultMaxByteStringLength);
    }
}

public sealed class OpcUaSetup_CreateConfiguration_ServerLimits
{
    private static TransportQuotasConfiguration CreateConfig(Dictionary<string, string>? envVars = null)
        => new(name => envVars is not null && envVars.TryGetValue(name, out var value) ? value : null);

    private static OpcUaServerDataPortProperties CreateDefaultProperties()
        => new(new OpcUaServerDataPortCommunication());

    [Fact]
    public void ServerConfiguration_has_explicit_session_limits()
    {
        var configuration = OpcUaSetup.CreateConfiguration(CreateDefaultProperties(), CreateConfig());

        configuration.ServerConfiguration.MaxSessionCount.Should().Be(TransportQuotasConfiguration.DefaultMaxSessionCount);
        configuration.ServerConfiguration.MaxSubscriptionCount.Should().Be(TransportQuotasConfiguration.DefaultMaxSubscriptionCount);
        configuration.ServerConfiguration.MinSessionTimeout.Should().Be(TransportQuotasConfiguration.DefaultMinSessionTimeout);
        configuration.ServerConfiguration.MaxSessionTimeout.Should().Be(TransportQuotasConfiguration.DefaultMaxSessionTimeout);
    }

    [Fact]
    public void ServerConfiguration_respects_environment_variable_overrides()
    {
        var config = CreateConfig(new()
        {
            [TransportQuotasConfiguration.MaxSessionCountEnvVar] = "50",
            [TransportQuotasConfiguration.MaxSubscriptionCountEnvVar] = "250",
        });

        var configuration = OpcUaSetup.CreateConfiguration(CreateDefaultProperties(), config);

        configuration.ServerConfiguration.MaxSessionCount.Should().Be(50);
        configuration.ServerConfiguration.MaxSubscriptionCount.Should().Be(250);
    }
}
