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


  public async Task<IPeer> GetInterfacePeer()
  {
    IPAddress iP = LoadIPAddress();

    TcpClient tcpClient = new TcpClient();

    try
    {
      Peer peer = new(CreateStateMachineProtocol(), tcpClient, Peer.ConnectionType.OUTBOUND, iP);

      await peer.Start();

      return peer;
    }
    catch (Exception ex)
    {
      tcpClient.Dispose();
      return null;
    }
  }


  readonly DirectoryInfo DirectoryPeers = Directory.CreateDirectory(
        Path.Combine(GetType().Name, "logPeers"));
  readonly DirectoryInfo DirectoryPeersDisposed = Directory.CreateDirectory(
    Path.Combine(DirectoryPeers.FullName, "disposed"));

  IPAddress LoadIPAddress()
  {
    if (IPAddresses.Count == 0)
    {
      IPAddresses = GetSeedAddresses();

      foreach (FileInfo iPDisposed in DirectoryPeersDisposed.EnumerateFiles())
      {
        if (iPDisposed.Name.Contains(ConnectionType.OUTBOUND.ToString()))
        {
          int secondsBanned = TIMESPAN_PEER_BANNED_SECONDS -
            (int)(DateTime.Now - iPDisposed.CreationTime).TotalSeconds;

          if (0 < secondsBanned)
          {
            IPAddresses.RemoveAll(iP => iPDisposed.Name.Contains(iP));
            continue;
          }

          iPDisposed.MoveTo(Path.Combine(
            DirectoryPeersArchive.FullName,
            iPDisposed.Name));
        }
      }

      foreach (FileInfo fileIPAddressArchive in DirectoryPeersArchive.EnumerateFiles())
      {
        string iPFromFile = fileIPAddressArchive.Name.GetIPFromFileName();

        if (!IPAddresses.Any(ip => ip == iPFromFile))
          IPAddresses.Add(iPFromFile);
      }

      foreach (FileInfo fileIPAddressActive in DirectoryPeersActive.EnumerateFiles())
        IPAddresses.RemoveAll(iP => fileIPAddressActive.Name.GetIPFromFileName() == iP);
    }

    while (iPAddresses.Count < maxCount && IPAddresses.Count > 0)
    {
      int randomIndex = randomGenerator.Next(IPAddresses.Count);

      string iPAddress = IPAddresses[randomIndex];
      IPAddresses.RemoveAt(randomIndex);

      if (!Peers.Any(p => p.IPAddress.ToString() == iPAddress))
        iPAddresses.Add(iPAddress);
    }

    return iPAddresses.Select(iP => IPAddress.Parse(iP)).ToList();
  }

  public List<string> GetSeedAddresses()
  {
    //mit DNS seeds arbeiten.
    //seed.bitcoin.sipa.be
    //dnsseed.bluematt.me
    //dnsseed.bitcoin.dashjr.org
    //seed.bitcoinstats.com
    //seed.bitnodes.io

    return new List<string>()
        {"83.229.86.158" 
        // 84.74.69.100
        };
  }


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


  Dictionary<string, MessageNetworkProtocol> CreateStateMachineProtocol()
  {
    Dictionary<string, MessageNetworkProtocol> protocol = new();

    Block blockDownload = new(Token);
    Block blockUpload = new(Token);

    AddMessageNetworkProtocol(protocol, new GetDataMessage(blockUpload));
    AddMessageNetworkProtocol(protocol, new GetHeadersMessage());
    AddMessageNetworkProtocol(protocol, new HeadersMessage(blockDownload));
    AddMessageNetworkProtocol(protocol, new BlockMessage(blockDownload));
    AddMessageNetworkProtocol(protocol, new TXMessage());
    AddMessageNetworkProtocol(protocol, new VerAckMessage());
    AddMessageNetworkProtocol(protocol, new VersionMessage());

    return protocol;
  }

  static void AddMessageNetworkProtocol(
    Dictionary<string, MessageNetworkProtocol> protocol,
    MessageNetworkProtocol message)
  {
    protocol.Add(message.GetCommand(), message);
  }
}
