using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace ViciOne.Suite.DataPort;

internal sealed class OpcUaServer(OpcUaServerDataPortCommunication communication, ILogger<IOpcUaServer> logger) : StandardServer, IOpcUaServer
{
    private readonly OpcUaServerDataPortProperties _properties = new(communication);
    private readonly Dictionary<Guid, Node> _nodes = [];
    private readonly LoginAttemptTracker _loginAttemptTracker = new();
    private DataPortNodeManager? _nodeManager;

    public event Action<string, DateTime, object> ReceiveValue
    {
        add
        {
            if (_nodeManager is null)
                throw new InvalidOperationException("Node manager is not initialized.");

            _nodeManager.ReceiveValue += value;
        }

        remove
        {
            if (_nodeManager is null)
                throw new InvalidOperationException("Node manager is not initialized.");

            _nodeManager.ReceiveValue -= value;
        }
    }

    public void AddNodes(IReadOnlyCollection<Node> nodes)
    {
        if (_nodeManager is not null)
            throw new InvalidOperationException("Node manager is already initialized.");

        foreach (var node in nodes)
        {
            if (_nodes.TryGetValue(node.Id, out var existingNode))
            {
                existingNode.TransferredChannels.AddRange(node.TransferredChannels.Except(existingNode.TransferredChannels));
                existingNode.AffectedChannels.AddRange(node.AffectedChannels.Except(existingNode.AffectedChannels));
                continue;
            }
            _nodes.Add(node.Id, node);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var configuration = OpcUaSetup.CreateConfiguration(_properties);

        LogInsecureConfigurationWarnings();

        Utils.SetLogger(logger);
        await ValidateConfigAsync(configuration, cancellationToken);
        Start(configuration);

        if (_nodeManager is null)
            throw new InvalidOperationException("Server did not initialize correctly.");
    }

    internal void LogInsecureConfigurationWarnings()
    {
        if (_properties.UserAuthenticationType == UserAuthenticationType.Anonymous)
            logger.LogAnonymousAccessEnabled();

        if (_properties.AutoAcceptUntrustedCertificates)
            logger.LogAutoAcceptUntrustedCertificates();

        if (!_properties.TransportQuotas)
            logger.LogTransportQuotasDisabled();

        if (_properties.SecurityPolicy == ServerSecurityPolicy.None)
            logger.LogInsecureSecurityPolicy();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Utils.Logger == logger)
            Utils.SetLogger(new TraceEventLogger());

        Stop();

        return Task.CompletedTask;
    }

    public async Task PublishValueAsync(string channel, object? value, DateTime timestamp, CancellationToken cancellationToken)
    {
        if (_nodeManager is null)
            throw new InvalidOperationException("Node manager is not initialized.");

        var node = _nodeManager.GetNodeState(channel);

        if (!await _nodeManager.WriteVariableValueAsync(node, value, timestamp, false))
            throw new InvalidOperationException($"Failed to send '{value}' to node with id '{node.NodeId.Identifier}'.");
    }

    public async Task SetNodeStatusAsync(string channel, object? value, CancellationToken cancellationToken)
    {
        if (_nodeManager is null)
            throw new InvalidOperationException("Node manager is not initialized.");

        var node = _nodeManager.GetNodeState(channel);

        await _nodeManager.UpdateVariableStateAsync(node, OpcUaStatusCodes.ConvertToStatusCode(value, logger));
    }

    protected override SessionManager CreateSessionManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        var manager = base.CreateSessionManager(server, configuration);

        if (string.IsNullOrEmpty(_properties.User) && string.IsNullOrEmpty(_properties.Password))
            return manager;

        manager.ImpersonateUser += ImpersonateUser;

        return manager;
    }

    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        _nodeManager = new(server, configuration, _nodes.Values.GetRoutes(), _properties.Namespace);
        return new MasterNodeManager(server, configuration, null, _nodeManager);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _nodeManager?.Dispose();
            _nodeManager = null;
        }
    }

    /// <summary>
    /// Parts of the method are copied from the reference implementation of the OPC UA server.
    /// <see href="https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Applications/Quickstarts.Servers/ReferenceServer/ReferenceServer.cs"/>
    /// </summary>
    private void ImpersonateUser(Session session, ImpersonateEventArgs args)
    {
        var sessionId = session?.Id?.ToString() ?? "unknown";

        args.Identity = args.NewIdentity switch
        {
            UserNameIdentityToken userNameToken => VerifyPassword(userNameToken, sessionId),
            AnonymousIdentityToken => HandleAnonymousAccess(sessionId),
            _ => throw ServiceResultException.Create(StatusCodes.BadIdentityTokenInvalid, "Not supported user token type: {0}.", args.NewIdentity),
        };
    }

    internal RoleBasedIdentity HandleAnonymousAccess(string sessionId)
    {
        logger.LogAnonymousAccess(sessionId);
        return new RoleBasedIdentity(new UserIdentity(), [Role.Anonymous]);
    }

    /// <summary>
    /// Parts of the method are copied from the reference implementation of the OPC UA server.
    /// <see href="https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Applications/Quickstarts.Servers/ReferenceServer/ReferenceServer.cs"/>
    /// </summary>
    internal RoleBasedIdentity VerifyPassword(UserNameIdentityToken userNameToken, string sessionId)
    {
        var username = userNameToken.UserName ?? string.Empty;

        if (_loginAttemptTracker.IsLockedOut(username))
        {
            logger.LogAccountLocked(username, _loginAttemptTracker.GetLockoutEnd(username));

            var lockInfo = new TranslationInfo(
                "AccountLocked",
                "en-US",
                "Account is locked due to too many failed login attempts.",
                username);

            throw new ServiceResultException(new ServiceResult(
                StatusCodes.BadUserAccessDenied,
                "AccountLocked",
                LoadServerProperties().ProductUri,
                new LocalizedText(lockInfo)));
        }

        var remainingDelay = _loginAttemptTracker.GetRemainingDelay(username);

        if (remainingDelay > TimeSpan.Zero)
        {
            logger.LogLoginThrottled(username, remainingDelay);

            var throttleInfo = new TranslationInfo(
                "TooManyAttempts",
                "en-US",
                $"Too many login attempts. Try again later. Remaining delay: {remainingDelay.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)}.",
                username);

            throw new ServiceResultException(new ServiceResult(
                StatusCodes.BadUserAccessDenied,
                "TooManyAttempts",
                LoadServerProperties().ProductUri,
                new LocalizedText(throttleInfo)));
        }

        if (!AreCredentialsValid(_properties.User, username, _properties.Password, userNameToken.DecryptedPassword))
        {
            _loginAttemptTracker.RecordFailure(username);
            logger.LogFailedAuthentication(username, sessionId);
            logger.LogAccountLocked(username, _loginAttemptTracker.GetLockoutEnd(username));

            // construct translation object with default text.
            var info = new TranslationInfo(
                "InvalidPassword",
                "en-US",
                "Invalid username or password.",
                username);

            // create an exception with a vendor defined sub-code.
            throw new ServiceResultException(new ServiceResult(
                StatusCodes.BadUserAccessDenied,
                "InvalidPassword",
                LoadServerProperties().ProductUri,
                new LocalizedText(info)));
        }

        _loginAttemptTracker.ResetFailures(username);
        logger.LogSuccessfulAuthentication(username, sessionId);

        return new RoleBasedIdentity(new UserIdentity(userNameToken),
               [Role.AuthenticatedUser]);
    }

    /// <summary>
    /// Performs a timing-safe comparison of user credentials using <see cref="CryptographicOperations.FixedTimeEquals"/>.
    /// Uses non-short-circuit AND (&amp;) to ensure both comparisons always execute, preventing timing information leakage.
    /// </summary>
    internal static bool AreCredentialsValid(string? expectedUser, string? actualUser, string? expectedPassword, string? actualPassword)
    {
        var expectedUserBytes = Encoding.UTF8.GetBytes(expectedUser ?? string.Empty);
        var actualUserBytes = Encoding.UTF8.GetBytes(actualUser ?? string.Empty);
        var expectedPasswordBytes = Encoding.UTF8.GetBytes(expectedPassword ?? string.Empty);
        var actualPasswordBytes = Encoding.UTF8.GetBytes(actualPassword ?? string.Empty);

        return CryptographicOperations.FixedTimeEquals(expectedUserBytes, actualUserBytes)
            & CryptographicOperations.FixedTimeEquals(expectedPasswordBytes, actualPasswordBytes);
    }

    private static async Task ValidateConfigAsync(ApplicationConfiguration configuration, CancellationToken cancellationToken)
    {
        await configuration.Validate(ApplicationType.Server).ConfigureAwait(false);

        ApplicationInstance applicationInstance = new(configuration);

        if (!await applicationInstance.CheckApplicationInstanceCertificates(false, CertificateFactory.DefaultLifeTime, cancellationToken))
            throw new InvalidOperationException("Server certificate is not valid.");
    }
}
