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
  public enum ConnectionType { OUTBOUND, INBOUND };
  List<string> IPAddresses = new();


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
