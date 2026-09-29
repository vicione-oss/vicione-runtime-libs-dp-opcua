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

    [LoggerMessage(4, LogLevel.Error, "Cannot release OPC UA client '{ApplicationName}' after a failed connect.")]
    internal static partial void LogReleaseAfterFailedConnectFailure(this ILogger logger, string applicationName, Exception exception);

    [LoggerMessage(5, LogLevel.Warning, "OPC UA client '{ApplicationName}' skips node '{DisplayName}' below '{BrowsedPath}': it is already on that path.")]
    internal static partial void LogBrowseCycleSkipped(this ILogger logger, string applicationName, string displayName, string browsedPath);

    [LoggerMessage(6, LogLevel.Error, "Node '{NodeId}' refused a value written with {Envelope}, so the value did not arrive either. The server does not accept a written status or timestamp for this node; unlink the 'Status code' or 'Source timestamp' child of the data point, or link it only to servers that do.")]
    internal static partial void LogEnvelopeWriteRefused(this ILogger logger, string nodeId, string envelope);

    [LoggerMessage(7, LogLevel.Error, "OPC UA client '{ApplicationName}' cannot take over the session it reconnected.")]
    internal static partial void LogReconnectTakeOverFailure(this ILogger logger, string applicationName, Exception exception);
}
