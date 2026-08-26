using System;
using Microsoft.Extensions.Logging;

namespace ViciOne.Suite.DataPort;

internal static partial class OpcUaServerDataPortLog
{
    [LoggerMessage(0, LogLevel.Warning, "Did not find a matching OPC status code for value '{Status}', sending BadInternalError. Please do not include a hyphen after 'Good', 'Bad' etc. Also, all status codes can be parsed completely unhyphenated i.e. 'BadEdited_OutOfRange' can also be 'BadEditedOutOfRange'.")]
    internal static partial void LogNoSuchStatus(this ILogger logger, string status);

    [LoggerMessage(1, LogLevel.Warning, "Unable to parse status code '{Status}', sending BadInternalError. Please send the status code as uint i.e. 0x803E0000 is 'BadNotFound' or as a string without a hyphen after 'Good', 'Bad', etc. i.e. 'BadNotFound' or 'BadInternalError'. A list of status codes can be found here: https://reference.opcfoundation.org/Core/Part6/v104/docs/A.2")]
    internal static partial void LogParsingStatusFailed(this ILogger logger, object? status);

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
}
