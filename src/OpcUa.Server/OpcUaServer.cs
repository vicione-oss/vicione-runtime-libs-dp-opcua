using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace ViciOne.Suite.DataPort;

internal sealed class OpcUaServer(OpcUaServerDataPortCommunication communication, ILogger<IOpcUaServer> logger, TimeProvider? timeProvider = null) : StandardServer, IOpcUaServer
{
    private readonly OpcUaServerDataPortProperties _properties = new(communication);
    private readonly LoginAttemptTracker _loginAttemptTracker = new(timeProvider);
    private AddressSpaceLayout _layout = AddressSpaceLayout.Empty;
    private DataPortNodeManager? _nodeManager;

    // Set while the server runs, so a changed layout reaches the address space clients browse. A
    // stopped server keeps its node manager until the next start replaces it with a new one.
    private bool _isRunning;

    public event Action<ReceivedWrite> ReceiveValue
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

    public void AddNodes(object owner, IReadOnlyCollection<Node> nodes)
    {
        AddressSpaceLayout layout;

        try
        {
            layout = _layout.With(owner, nodes);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"Cannot serve the nodes of this data port on the OPC UA server at '{communication.Server}:{communication.Port}'. {exception.Message}", exception);
        }

        Apply(layout);
    }

    public void RemoveNodes(object owner)
        => Apply(_layout.Without(owner));

    /// <summary>
    /// A server that is not running serves the layout once it starts. A running one changes its
    /// address space right away, without dropping the sessions of its clients.
    /// </summary>
    private void Apply(AddressSpaceLayout layout)
    {
        if (_isRunning)
            _nodeManager?.Apply(layout);

        _layout = layout;
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

        _isRunning = true;
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

        // A stop that fails leaves the server running, and the data ports with it, so the nodes of
        // a data port registered after it still have to reach the address space.
        Stop();
        _isRunning = false;

        return Task.CompletedTask;
    }

    public async Task PublishValueAsync(object owner, string channel, object? value, DateTime timestamp, StatusCode? statusCode, CancellationToken cancellationToken)
    {
        if (_nodeManager is null)
            throw new InvalidOperationException("Node manager is not initialized.");

        var node = _nodeManager.GetNodeState(owner, channel);

        if (!await _nodeManager.WriteVariableValueAsync(node, value, timestamp, statusCode, false))
            throw new InvalidOperationException($"Failed to send '{value}' to node with id '{node.NodeId.Identifier}'.");
    }

    public async Task SetNodeStatusAsync(object owner, string channel, StatusCode statusCode, CancellationToken cancellationToken)
    {
        if (_nodeManager is null)
            throw new InvalidOperationException("Node manager is not initialized.");

        var node = _nodeManager.GetNodeState(owner, channel);

        await _nodeManager.UpdateVariableStateAsync(node, statusCode);
    }

    protected override SessionManager CreateSessionManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        var manager = base.CreateSessionManager(server, configuration);

        manager.ImpersonateUser += ImpersonateUser;

        return manager;
    }

    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        _nodeManager = new(server, configuration, _layout, _properties.Namespace);
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

    /// <summary>
    /// A client may present an anonymous token whatever the server advertises, so the configured
    /// authentication type is what decides whether it gets a session.
    /// </summary>
    internal RoleBasedIdentity HandleAnonymousAccess(string sessionId)
    {
        if (_properties.UserAuthenticationType is not UserAuthenticationType.Anonymous)
        {
            logger.LogAnonymousAccessRejected(sessionId);

            throw ServiceResultException.Create(StatusCodes.BadIdentityTokenRejected,
                "Anonymous access is not allowed on this server.");
        }

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

            var lockoutEnd = _loginAttemptTracker.GetLockoutEnd(username);

            if (lockoutEnd is not null)
                logger.LogAccountLocked(username, lockoutEnd);

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
