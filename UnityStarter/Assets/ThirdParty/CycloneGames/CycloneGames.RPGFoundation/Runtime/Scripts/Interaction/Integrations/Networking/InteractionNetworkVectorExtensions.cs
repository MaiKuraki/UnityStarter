using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Runtime.Interaction.Integrations.Networking
{
    public static class InteractionNetworkVectorExtensions
    {
        public static NetworkVector3 ToNetworkVector3(this InteractionVector3 value)
        {
            return new NetworkVector3(value.X, value.Y, value.Z);
        }

        public static InteractionVector3 ToInteractionVector3(this NetworkVector3 value)
        {
            return new InteractionVector3(value.X, value.Y, value.Z);
        }
    }
}
