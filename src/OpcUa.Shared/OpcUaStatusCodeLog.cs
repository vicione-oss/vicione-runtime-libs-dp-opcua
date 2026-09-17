using Microsoft.Extensions.Logging;

namespace ViciOne.Suite.DataPort;

/// <summary>
/// What <see cref="OpcUaStatusCodes"/> reports when it cannot read a status code. Both providers
/// convert one, so the messages live beside the converter rather than in either provider's own log.
/// </summary>
internal static partial class OpcUaStatusCodeLog
{
    [LoggerMessage(100, LogLevel.Warning, "No OPC UA status code is named '{Status}', so BadInternalError is used instead. Names are case-sensitive and may leave out the underscore, such as 'BadEditedOutOfRange' for 'BadEdited_OutOfRange'; a code can also be given as its number, such as '0x803E0000' for 'BadNotFound'.")]
    internal static partial void LogNoSuchStatus(this ILogger logger, string status);

    [LoggerMessage(101, LogLevel.Warning, "Cannot read '{Status}' as an OPC UA status code, so BadInternalError is used instead. Link the status code as a UInt32, such as 0x803E0000 for BadNotFound, or as a String holding its name or number. A list of status codes: https://reference.opcfoundation.org/Core/Part6/v104/docs/A.2")]
    internal static partial void LogParsingStatusFailed(this ILogger logger, object? status);
}
