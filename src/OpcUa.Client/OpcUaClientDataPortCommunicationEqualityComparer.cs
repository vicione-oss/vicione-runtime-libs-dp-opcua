using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ViciOne.Suite.DataPort;

internal class OpcUaClientDataPortCommunicationEqualityComparer : IEqualityComparer<OpcUaClientDataPortCommunication>
{
    public bool Equals(OpcUaClientDataPortCommunication? x, OpcUaClientDataPortCommunication? y)
    {
        if (x is null && y is null)
            return true;
        if (x is null || y is null)
            return false;

        return x.ApplicationName == y.ApplicationName
            && x.ApplicationUri == y.ApplicationUri
            && x.Server == y.Server
            && x.Port == y.Port
            && x.Endpoint == y.Endpoint
            && x.UserAuthenticationType == y.UserAuthenticationType
            && x.User == y.User
            && x.Password == y.Password
            && x.ApplicationCertificateSubject == y.ApplicationCertificateSubject
            && x.ApplicationCertificatesStoreType == y.ApplicationCertificatesStoreType
            && x.ApplicationCertificatesStorePath == y.ApplicationCertificatesStorePath
            && x.TrustedCertificatesStoreType == y.TrustedCertificatesStoreType
            && x.TrustedCertificatesStorePath == y.TrustedCertificatesStorePath
            && x.TrustedIssuerCertificatesStoreType == y.TrustedIssuerCertificatesStoreType
            && x.TrustedIssuerCertificatesStorePath == y.TrustedIssuerCertificatesStorePath
            && x.AutoAcceptUntrustedCertificates == y.AutoAcceptUntrustedCertificates;
    }

    public int GetHashCode([DisallowNull] OpcUaClientDataPortCommunication obj)
    {
        HashCode hashCode = new();

        hashCode.Add(obj.ApplicationName);
        hashCode.Add(obj.ApplicationUri);
        hashCode.Add(obj.Server);
        hashCode.Add(obj.Port);
        hashCode.Add(obj.Endpoint);
        hashCode.Add(obj.UserAuthenticationType);
        hashCode.Add(obj.User);
        hashCode.Add(obj.Password);
        hashCode.Add(obj.ApplicationCertificateSubject);
        hashCode.Add(obj.ApplicationCertificatesStoreType);
        hashCode.Add(obj.ApplicationCertificatesStorePath);
        hashCode.Add(obj.TrustedCertificatesStoreType);
        hashCode.Add(obj.TrustedCertificatesStorePath);
        hashCode.Add(obj.TrustedIssuerCertificatesStoreType);
        hashCode.Add(obj.TrustedIssuerCertificatesStorePath);
        hashCode.Add(obj.AutoAcceptUntrustedCertificates);

        return hashCode.ToHashCode();
    }
}
