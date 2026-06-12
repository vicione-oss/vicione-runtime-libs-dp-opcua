using System;
using Microsoft.Extensions.Logging;

namespace ViciOne.Suite.DataPort;

internal static partial class OpcUaClientDataPortLog
{
    [LoggerMessage(0, LogLevel.Error, "Cannot close session of OPC UA client '{ApplicationName}'.")]
    internal static partial void LogCloseSessionFailure(this ILogger logger, string applicationName, Exception exception);

    [LoggerMessage(1, LogLevel.Error, "Write value for node '{NodeId}' failed with '{StatusCode}'.")]
    internal static partial void LogWriteFailure(this ILogger logger, string nodeId, string statusCode);

    [LoggerMessage(2, LogLevel.Error, "Keep alive action failed for '{ApplicationName}'.")]
    internal static partial void LogKeepAliveFailure(this ILogger logger, string applicationName, Exception exception);

    [LoggerMessage(3, LogLevel.Debug, "Subscription exchanged. Maybe the subscription could not be transfered after reconnect of '{ApplicationName}'.")]
    internal static partial void LogSubscriptionExchanged(this ILogger logger, string applicationName);
}
