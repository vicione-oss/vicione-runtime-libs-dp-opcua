using System;
using System.Collections.Generic;
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

    internal static StatusCode ConvertToStatusCode(object? value, ILogger? logger = null)
    {
        if (value is string stringStatus)
        {
            if (string.IsNullOrEmpty(stringStatus))
                return StatusCodes.Good;
            if (uint.TryParse(stringStatus, out var parsedUint))
                return new StatusCode(parsedUint);
            if (stringStatus.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase))
                return new StatusCode(Convert.ToUInt32(stringStatus, 16));
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
}
