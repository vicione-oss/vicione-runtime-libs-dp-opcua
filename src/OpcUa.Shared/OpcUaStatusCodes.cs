using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace ViciOne.Suite.DataPort;

internal static class OpcUaStatusCodes
{
    private static readonly Dictionary<string, StatusCode> s_statusCodes = GetAvailableOpcUaStatusCodes();

    private static Dictionary<string, StatusCode> GetAvailableOpcUaStatusCodes()
    {
        var statusCodes = typeof(StatusCodes).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                    .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(uint))
                    .ToDictionary(f => f.Name, f => new StatusCode((uint)(f.GetRawConstantValue() ?? uint.MaxValue)));

        foreach (var (name, code) in statusCodes.Where(s => s.Key.Contains('_', StringComparison.InvariantCulture)).ToArray())
            statusCodes.TryAdd(name.Replace("_", string.Empty, StringComparison.InvariantCulture), code);

        return statusCodes;
    }

    /// <summary>
    /// Reads a status code from a <see cref="uint"/>, or from a string holding a decimal number, a
    /// hexadecimal number with a <c>0x</c> prefix, or the exact, case-sensitive name of a code with
    /// or without its underscore. Anything else is reported and read as
    /// <see cref="StatusCodes.BadInternalError"/>.
    /// </summary>
    internal static StatusCode ConvertToStatusCode(object? value, ILogger? logger = null)
    {
        if (value is string stringStatus)
        {
            if (uint.TryParse(stringStatus, out var parsedUint))
                return new StatusCode(parsedUint);
            if (stringStatus.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase)
                && uint.TryParse(stringStatus.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hexStatus))
            {
                return new StatusCode(hexStatus);
            }
            if (s_statusCodes.TryGetValue(stringStatus, out var status))
                return status;

            logger?.LogNoSuchStatus(stringStatus);

            return StatusCodes.BadInternalError;
        }

        if (value is uint uintStatus)
            return new StatusCode(uintStatus);

        logger?.LogParsingStatusFailed(value);

        return StatusCodes.BadInternalError;
    }

    /// <summary>
    /// The symbolic name of a status code, such as <c>BadNotFound</c>, which
    /// <see cref="ConvertToStatusCode"/> reads back as the same code without its info bits.
    /// </summary>
    /// <remarks>
    /// Not <c>StatusCode.ToString()</c>: that appends the info bits of a code that carries any
    /// ("Good [0600]" for a value at its high limit), and returns the empty string for a code the
    /// stack has no name for. A code without a name is written as its number so that it round trips
    /// through the hexadecimal form.
    /// </remarks>
    internal static string NameOf(StatusCode statusCode)
        => StatusCode.LookupSymbolicId(statusCode.Code) is { Length: > 0 } name
            ? name
            : $"0x{statusCode.Code:X8}";
}
