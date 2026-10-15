using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace MonitorAgent.Modules.Customers.Application;

internal static class InvitationTokens
{
    public static string New() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
