using System;
using Microsoft.Extensions.Logging;

namespace ViciOne.Suite.DataPort;

internal static partial class OpcUaServerDataPortLog
{
    [LoggerMessage(2, LogLevel.Error, "Cannot send values to '{Server}:{Port}'.")]
    internal static partial void LogSendFailure(this ILogger logger, string server, int port, Exception exception);

    [LoggerMessage(3, LogLevel.Warning, "Account '{Username}' is locked due to too many failed login attempts. Lockout ends at {LockoutEnd} (UTC).")]
    internal static partial void LogAccountLocked(this ILogger logger, string? username, DateTimeOffset? lockoutEnd);

    [LoggerMessage(4, LogLevel.Warning, "Failed authentication attempt for user '{User}' from session '{SessionId}'.")]
    internal static partial void LogFailedAuthentication(this ILogger logger, string user, string sessionId);

    [LoggerMessage(5, LogLevel.Information, "User '{User}' authenticated successfully from session '{SessionId}'.")]
    internal static partial void LogSuccessfulAuthentication(this ILogger logger, string user, string sessionId);

    [LoggerMessage(6, LogLevel.Warning, "Anonymous access used from session '{SessionId}'.")]
    internal static partial void LogAnonymousAccess(this ILogger logger, string sessionId);

    [LoggerMessage(8, LogLevel.Warning,
        "OPC UA Server started with anonymous access enabled. " +
        "Consider setting UserAuthenticationType to 'Basic'.")]
    internal static partial void LogAnonymousAccessEnabled(this ILogger logger);

    [LoggerMessage(9, LogLevel.Warning,
        "OPC UA Server started with AutoAcceptUntrustedCertificates=true. " +
        "This is insecure and allows man-in-the-middle attacks. " +
        "Do not use in production environments.")]
    internal static partial void LogAutoAcceptUntrustedCertificates(this ILogger logger);

    [LoggerMessage(10, LogLevel.Warning,
        "OPC UA Server started with TransportQuotas disabled. " +
        "This leaves the server without message size limits and may expose it to denial-of-service attacks. " +
        "Do not use in production environments.")]
    internal static partial void LogTransportQuotasDisabled(this ILogger logger);

    [LoggerMessage(11, LogLevel.Warning,
        "OPC UA Server started with SecurityPolicy=None (no encryption, no signature). " +
        "Any device on the network can eavesdrop or inject data. " +
        "Do not use in production environments.")]
    internal static partial void LogInsecureSecurityPolicy(this ILogger logger);

    [LoggerMessage(12, LogLevel.Warning, "Login attempt for '{Username}' throttled. Remaining delay: {RemainingDelay}.")]
    internal static partial void LogLoginThrottled(this ILogger logger, string? username, TimeSpan remainingDelay);

    [LoggerMessage(13, LogLevel.Warning, "Anonymous access rejected for session '{SessionId}'. The server authenticates its users.")]
    internal static partial void LogAnonymousAccessRejected(this ILogger logger, string sessionId);

    [LoggerMessage(14, LogLevel.Debug, "Ignoring the stop of the OPC UA server at '{Server}:{Port}' because it is not running.")]
    internal static partial void LogStopWithoutStart(this ILogger logger, string server, int port);

    [LoggerMessage(15, LogLevel.Error, "Cannot shut the OPC UA server at '{Server}:{Port}' down. Clients may still be connected.")]
    internal static partial void LogServerShutdownFailed(this ILogger logger, string server, int port, Exception exception);

    [LoggerMessage(16, LogLevel.Warning, "Ignoring the stop of the OPC UA server at '{Server}:{Port}' by a data port that is not one of those keeping it running. The data ports that started it are still using the server.")]
    internal static partial void LogStopByDataPortThatIsNotKeepingItRunning(this ILogger logger, string server, int port);

    [LoggerMessage(17, LogLevel.Error, "Cannot remove the nodes of a released data port from the OPC UA server at '{Server}:{Port}'. Clients may still browse to them.")]
    internal static partial void LogNodeRemovalFailed(this ILogger logger, string server, int port, Exception exception);
}
