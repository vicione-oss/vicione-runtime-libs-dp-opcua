using System;
using System.Globalization;

namespace ViciOne.Suite.DataPort;

internal sealed class TransportQuotasConfiguration
{
    internal const string MaxStringLengthEnvVar = "OPCUA_MAX_STRING_LENGTH";
    internal const string MaxByteStringLengthEnvVar = "OPCUA_MAX_BYTE_STRING_LENGTH";
    internal const string MaxArrayLengthEnvVar = "OPCUA_MAX_ARRAY_LENGTH";
    internal const string MaxMessageSizeEnvVar = "OPCUA_MAX_MESSAGE_SIZE";
    internal const string MaxBufferSizeEnvVar = "OPCUA_MAX_BUFFER_SIZE";
    internal const string OperationTimeoutEnvVar = "OPCUA_OPERATION_TIMEOUT";
    internal const string MaxSessionCountEnvVar = "OPCUA_MAX_SESSION_COUNT";
    internal const string MaxSubscriptionCountEnvVar = "OPCUA_MAX_SUBSCRIPTION_COUNT";
    internal const string MinSessionTimeoutEnvVar = "OPCUA_MIN_SESSION_TIMEOUT";
    internal const string MaxSessionTimeoutEnvVar = "OPCUA_MAX_SESSION_TIMEOUT";

    internal const int DefaultMaxStringLength = 1_048_576;       // 1 MB
    internal const int DefaultMaxByteStringLength = 4_194_304;   // 4 MB
    internal const int DefaultMaxArrayLength = 65_535;
    internal const int DefaultMaxMessageSize = 4_194_304;        // 4 MB
    internal const int DefaultMaxBufferSize = 65_535;
    internal const int DefaultOperationTimeout = 120_000;        // 2 min

    internal const int DefaultMaxSessionCount = 100;
    internal const int DefaultMaxSubscriptionCount = 500;
    internal const int DefaultMinSessionTimeout = 10_000;        // 10 sec
    internal const int DefaultMaxSessionTimeout = 600_000;       // 10 min

    internal static TransportQuotasConfiguration Default { get; } = new(Environment.GetEnvironmentVariable);

    private readonly Func<string, string?> _readEnv;

    internal TransportQuotasConfiguration(Func<string, string?> readEnv) => _readEnv = readEnv;

    internal int MaxStringLength => ReadEnvInt(MaxStringLengthEnvVar, DefaultMaxStringLength);
    internal int MaxByteStringLength => ReadEnvInt(MaxByteStringLengthEnvVar, DefaultMaxByteStringLength);
    internal int MaxArrayLength => ReadEnvInt(MaxArrayLengthEnvVar, DefaultMaxArrayLength);
    internal int MaxMessageSize => ReadEnvInt(MaxMessageSizeEnvVar, DefaultMaxMessageSize);
    internal int MaxBufferSize => ReadEnvInt(MaxBufferSizeEnvVar, DefaultMaxBufferSize);
    internal int OperationTimeout => ReadEnvInt(OperationTimeoutEnvVar, DefaultOperationTimeout);

    internal int MaxSessionCount => ReadEnvInt(MaxSessionCountEnvVar, DefaultMaxSessionCount);
    internal int MaxSubscriptionCount => ReadEnvInt(MaxSubscriptionCountEnvVar, DefaultMaxSubscriptionCount);
    internal int MinSessionTimeout => ReadEnvInt(MinSessionTimeoutEnvVar, DefaultMinSessionTimeout);
    internal int MaxSessionTimeout => ReadEnvInt(MaxSessionTimeoutEnvVar, DefaultMaxSessionTimeout);

    private int ReadEnvInt(string name, int defaultValue)
    {
        var value = _readEnv(name);
        return value is not null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }
}
