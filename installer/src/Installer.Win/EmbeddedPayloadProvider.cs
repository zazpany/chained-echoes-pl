using System.Reflection;
using ChainedEchoesPolishInstaller.Core;

namespace ChainedEchoesPolishInstaller;

internal sealed class EmbeddedPayloadProvider : IPayloadProvider
{
    private const string Prefix = "ChainedEchoesPolishInstaller.Payload.";

    public Stream OpenRead(string payloadName)
    {
        var resource = Prefix + payloadName;
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException(
                $"Instalator nie zawiera wymaganego payloadu: {payloadName}",
                resource);
    }
}
