using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;

using BTokenCore;


namespace BTokenCore_Testbench;

public partial class Testbench
{
  // Das Netzwerk holt die Konfiguration vom Token ab.
  int Port;
  UInt32 ProtocolVersion = 70015;
  ulong NetworkServicesLocal = 0;
  ulong NetworkServicesRemote = 0;
  string UserAgent = "/BTokenCore:0.0.0/";
  byte RelayOption = 0x01;

  public enum ConnectionType { OUTBOUND, INBOUND };
  List<string> IPAddresses = new();


  public async Task StartPeerInboundConnector()
  {
    TcpListener tcpListener = new(IPAddress.Any, Port);

    try
    {
      Log($"Start TCP listener on port {Port}.");
      tcpListener.Start(COUNT_MAX_INBOUND_CONNECTIONS);
    }
    catch (Exception ex)
    {
      Log($"Failed to start TCP listener on port {Port}.\n {ex.Message}");
      return;
    }

    while (true)
    {
      try
      {
        TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);

        IPAddress remoteIP = ((IPEndPoint)tcpClient.Client.RemoteEndPoint).Address;

        Log($"Received inbound request on port {Port} from {remoteIP}.");

        if (!ValidateInboundPeer(remoteIP))
        {
          tcpClient.Dispose();
          continue;
        }

        CreatePeerInbound(tcpClient, remoteIP);
      }
      catch (Exception ex)
      {
        Log($"{ex.GetType().Name} in peer connector background process:\n {ex.Message}");

        await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
      }
    }
  }

  bool ValidateInboundPeer(IPAddress remoteIP)
  {
    string rejectionString = "";

    lock (LOCK_Peers)
    {
      if (Peers.Any(p => p.IPAddress.Equals(remoteIP)))
        rejectionString = $"Peer {remoteIP} already connected.";
      else if (Peers.Count(p => p.Connection == ConnectionType.INBOUND) >= COUNT_MAX_INBOUND_CONNECTIONS)
        rejectionString = $"Max number ({COUNT_MAX_INBOUND_CONNECTIONS}) of inbound connections reached.";
    }

    if (rejectionString == "")
    {
      if (remoteIP.ToString() != "84.74.69.100")
        rejectionString = $"Peer {remoteIP} not on whitelist.";
      else
        foreach (FileInfo iPDisposed in DirectoryPeersDisposed.EnumerateFiles())
          if (iPDisposed.Name.Contains(remoteIP.ToString()) && iPDisposed.Name.Contains(ConnectionType.INBOUND.ToString()))
          {
            int secondsBanned = TIMESPAN_PEER_BANNED_SECONDS -
              (int)(DateTime.Now - iPDisposed.CreationTime).TotalSeconds;

            if (secondsBanned > 0)
            {
              rejectionString = $"{iPDisposed.Name} is banned for {secondsBanned} seconds.";
              break;
            }
          }
    }

    if (rejectionString != "")
    {
      Log($"Inbound peer {remoteIP} rejected: \n{rejectionString}");
      return false;
    }

    return true;
  }
  
  async Task CreatePeerInbound(TcpClient tcpClient, IPAddress iP)
  {
    try
    {
      Peer peer = new(CreateStateMachineProtocol(), tcpClient, C, iP);

      await peer.Start();

      lock (LOCK_Peers)
        Peers.Add(peer);
    }
    catch (Exception ex)
    {
      tcpClient.Dispose();
    }
  }
}