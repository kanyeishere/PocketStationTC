using PocketStation.Domain;
using PocketStation.Infrastructure.Serialization;

namespace PocketStation.Api;

public static class IncomingEnvelopeFactory
{
    public static IncomingEnvelope FromPayload(string type, object payload) => new()
    {
        Type = type,
        Payload = PocketJson.ToElement(payload)
    };
}
