using System.Diagnostics.CodeAnalysis;

namespace ViciOne.Suite.DataPort;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Names match OPC UA SDK security policy naming conventions")]
public enum ServerSecurityPolicy
{
    None,
    Basic256Sha256_Sign,
    Basic256Sha256_SignAndEncrypt,
    Aes256_Sha256_RsaPss_SignAndEncrypt,
}
