using System;
using System.Collections.Generic;
#if !UNITY_WEBGL
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
#endif

namespace CycloneGames.Networking.Platform
{
    public static class NetworkLocalAddressUtility
    {
        public static int GetLanIPv4Addresses(List<string> results, bool requireGateway = true)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();

#if UNITY_WEBGL
            return 0;
#else
            NetworkInterface[] interfaces;
            try
            {
                interfaces = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch
            {
                return 0;
            }

            for (int i = 0; i < interfaces.Length; i++)
            {
                NetworkInterface networkInterface = interfaces[i];
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                IPInterfaceProperties properties;
                try
                {
                    properties = networkInterface.GetIPProperties();
                }
                catch
                {
                    continue;
                }

                if (requireGateway && !HasIPv4Gateway(properties))
                {
                    continue;
                }

                UnicastIPAddressInformationCollection addresses = properties.UnicastAddresses;
                for (int addressIndex = 0; addressIndex < addresses.Count; addressIndex++)
                {
                    IPAddress address = addresses[addressIndex].Address;
                    if (address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    string value = address.ToString();
                    if (IPAddress.IsLoopback(address) || results.Contains(value))
                    {
                        continue;
                    }

                    results.Add(value);
                }
            }

            return results.Count;
#endif
        }

#if !UNITY_WEBGL
        private static bool HasIPv4Gateway(IPInterfaceProperties properties)
        {
            GatewayIPAddressInformationCollection gateways = properties.GatewayAddresses;
            for (int i = 0; i < gateways.Count; i++)
            {
                if (gateways[i].Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    return true;
                }
            }

            return false;
        }
#endif
    }
}
