using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;

using BTokenCore;

using LiteDB;

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  class TestNetwork : Test_Testbench
  {
    const string COMMAND_VERSION = "version";
    const string COMMAND_VERACK = "verack";
    const string COMMAND_GETADDR = "getaddr";
    const string COMMAND_ADDR = "addr";
    const uint PROTOCOL_VERSION = 1;
    const int LENGTH_ADDRESS = 30;
    const int OFFSET_IP_ADDRESS = 12;
    const string IP_PEER = "10.0.0.2";
    const string IP_READER = "10.0.0.9";
    const int SECONDS_TIMEOUT_PEER_LISTED = 10;
    const int SECONDS_TIMEOUT_REQUEST = 10;
    const int LENGTH_TIME_ADDRESS = 4;


    internal TestNetwork(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      return TestHandshakeOutbound(ref message)
        && TestHandshakeInbound(ref message)
        && TestServicesMissing(ref message)
        && TestHandshakeTimeout(ref message)
        && TestOutboundLimit(ref message)
        && TestOutboundFailure(ref message)
        && TestInboundDuplicate(ref message)
        && TestInboundEviction(ref message);
    }

    static bool TestHandshakeOutbound(ref string message)
    {
      Console.WriteLine("  1. Handshake Outbound: Complete version and verack with the seed peer.");

      const string NAME_CASE = "Handshake outbound";

      NetworkTestbench network = StartNodeWithSeedPeer(1, out _, out SocketFake socketSeed);

      try
      {
        return TryHandshakeOutbound(NAME_CASE, socketSeed, ref message)
          && IsPeerListed(NAME_CASE, network, network.ConfigurationNetwork.SeedAddresses[0], ref message);
      }
      finally
      {
        socketSeed.Dispose();
      }
    }

    static bool TestHandshakeInbound(ref string message)
    {
      Console.WriteLine("  2. Handshake Inbound: Accept an inbound peer and store its IP.");

      const string NAME_CASE = "Handshake inbound";

      CommunicationFake communication = new();
      NetworkTestbench network = BlockchainNetwork.StartNode(communication);
      SocketFake socketPeer = communication.ConnectInbound(IP_PEER);

      try
      {
        if (!TryHandshakeInbound(NAME_CASE, socketPeer, ref message)
          || !IsPeerListed(NAME_CASE, network, IP_PEER, ref message)
          || !TryGetAddresses(NAME_CASE, socketPeer, out List<string> iPs, ref message))
          return false;

        if (iPs.Contains(IP_PEER))
          return true;

        message = $"{NAME_CASE}: the addr answer lacks the inbound peer's IP {IP_PEER}.";
        return false;
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool TestServicesMissing(ref string message)
    {
      Console.WriteLine("  3. Services Missing: Disconnect an inbound peer lacking NODE_NETWORK.");

      const string NAME_CASE = "Services missing";

      SocketFake socketPeer = ConnectInboundWithVersion(0, out NetworkTestbench network);

      try
      {
        if (!socketPeer.IsDisconnected(NAME_CASE, ref message))
          return false;

        if (!network.IsPeerListed(IP_PEER))
          return true;

        message = $"{NAME_CASE}: the node lists the disconnected peer {IP_PEER} in Peers.";
        return false;
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool TestHandshakeTimeout(ref string message)
    {
      Console.WriteLine("  4. Handshake Timeout: Disconnect an inbound peer that never sends verack.");

      const string NAME_CASE = "Handshake timeout";
      const int SECONDS_MARGIN = 10;

      Stopwatch stopwatch = Stopwatch.StartNew();
      SocketFake socketPeer = ConnectInboundWithVersion(Network.Configuration.NODE_NETWORK, out NetworkTestbench network);

      try
      {
        if (!socketPeer.IsDisconnected(NAME_CASE, ref message, Network.TIMEOUT_REQUEST_SECONDS + SECONDS_MARGIN))
          return false;

        if (stopwatch.Elapsed < TimeSpan.FromSeconds(Network.TIMEOUT_REQUEST_SECONDS - 1))
        {
          message = $"{NAME_CASE}: the node disconnected the peer after {stopwatch.Elapsed.TotalSeconds:F0} seconds, before TIMEOUT_REQUEST_SECONDS.";
          return false;
        }

        if (!network.IsPeerListed(IP_PEER))
          return true;

        message = $"{NAME_CASE}: the node lists the timed out peer {IP_PEER} in Peers.";
        return false;
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool TestOutboundLimit(ref string message)
    {
      Console.WriteLine("  5. Outbound Limit: Hold back connections beyond CountMaxOutboundConnections.");

      const string NAME_CASE = "Outbound limit";
      const string IP_LEARNED = "10.0.1.1";
      const int SECONDS_HOLD = 8;

      NetworkTestbench network = StartNodeWithSeedPeer(1, out CommunicationFake communication, out SocketFake socketSeed);

      try
      {
        if (!TryHandshakeOutbound(NAME_CASE, socketSeed, ref message))
          return false;

        SendAddr(socketSeed, [(IP_LEARNED, network.ConfigurationNetwork.Port)]);

        if (communication.IsRequested(IP_LEARNED, SECONDS_HOLD))
        {
          message = $"{NAME_CASE}: the node connects to {IP_LEARNED} although CountMaxOutboundConnections is reached.";
          return false;
        }

        network.ConfigurationNetwork.CountMaxOutboundConnections = 2;

        if (communication.IsRequested(IP_LEARNED, SECONDS_TIMEOUT_REQUEST))
          return true;

        message = $"{NAME_CASE}: the node does not connect to {IP_LEARNED} after CountMaxOutboundConnections was raised.";
        return false;
      }
      finally
      {
        socketSeed.Dispose();
      }
    }

    static bool TestOutboundFailure(ref string message)
    {
      Console.WriteLine("  6. Outbound Failure: Drop the IP of a failed outbound connection.");

      const string NAME_CASE = "Outbound failure";
      const string IP_LEARNED = "10.0.1.1";

      NetworkTestbench network = StartNodeWithSeedPeer(2, out CommunicationFake communication, out SocketFake socketSeed);

      try
      {
        if (!TryHandshakeOutbound(NAME_CASE, socketSeed, ref message))
          return false;

        SendAddr(socketSeed, [(IP_LEARNED, network.ConfigurationNetwork.Port)]);

        if (!communication.IsRequested(IP_LEARNED, SECONDS_TIMEOUT_REQUEST))
        {
          message = $"{NAME_CASE}: the node does not try to connect to {IP_LEARNED}.";
          return false;
        }

        if (!TryReadAddressList(NAME_CASE, communication, out List<string> iPs, ref message))
          return false;

        if (!iPs.Contains(IP_LEARNED))
          return true;

        message = $"{NAME_CASE}: the node keeps {IP_LEARNED} after its outbound connection failed.";
        return false;
      }
      finally
      {
        socketSeed.Dispose();
      }
    }

    static bool TestInboundDuplicate(ref string message)
    {
      Console.WriteLine("  7. Inbound Duplicate: Reject a second inbound connection from one IP.");

      const string NAME_CASE = "Inbound duplicate";

      CommunicationFake communication = new();
      NetworkTestbench network = BlockchainNetwork.StartNode(communication);
      SocketFake socketPeer = communication.ConnectInbound(IP_PEER);
      SocketFake socketDuplicate = null;

      try
      {
        if (!TryHandshakeInbound(NAME_CASE, socketPeer, ref message)
          || !IsPeerListed(NAME_CASE, network, IP_PEER, ref message))
          return false;

        socketDuplicate = communication.ConnectInbound(IP_PEER);

        if (!socketDuplicate.IsDisconnected(NAME_CASE, ref message))
          return false;

        if (network.IsPeerListed(IP_PEER))
          return true;

        message = $"{NAME_CASE}: the node dropped the first peer {IP_PEER} when rejecting the duplicate.";
        return false;
      }
      finally
      {
        socketPeer.Dispose();
        socketDuplicate?.Dispose();
      }
    }

    static bool TestInboundEviction(ref string message)
    {
      Console.WriteLine("  8. Inbound Eviction: Evict the unprotected inbound peer, keep the TX deliverer.");

      const string NAME_CASE = "Inbound eviction";
      const string IP_UNPROTECTED = "10.0.0.3";
      const string IP_NEW = "10.0.0.4";

      CommunicationFake communication = new();
      NetworkTestbench network = BlockchainNetwork.StartNode(communication, countMaxInbound: 2);
      SocketFake socketProtected = communication.ConnectInbound(IP_PEER);
      SocketFake socketUnprotected = communication.ConnectInbound(IP_UNPROTECTED);
      SocketFake socketNew = null;

      try
      {
        if (!TryHandshakeInbound(NAME_CASE, socketProtected, ref message)
          || !TryHandshakeInbound(NAME_CASE, socketUnprotected, ref message)
          || !IsPeerListed(NAME_CASE, network, IP_PEER, ref message)
          || !IsPeerListed(NAME_CASE, network, IP_UNPROTECTED, ref message))
          return false;

        network.MarkTXDelivered(IP_PEER);

        socketNew = communication.ConnectInbound(IP_NEW);

        if (!socketUnprotected.IsDisconnected(NAME_CASE, ref message)
          || !TryHandshakeInbound(NAME_CASE, socketNew, ref message)
          || !IsPeerListed(NAME_CASE, network, IP_NEW, ref message))
          return false;

        if (network.IsPeerListed(IP_PEER))
          return true;

        message = $"{NAME_CASE}: the node evicted the TX deliverer {IP_PEER}.";
        return false;
      }
      finally
      {
        socketProtected.Dispose();
        socketUnprotected.Dispose();
        socketNew?.Dispose();
      }
    }

    static NetworkTestbench StartNodeWithSeedPeer(int countMaxOutbound, out CommunicationFake communication, out SocketFake socketSeed)
    {
      communication = new();
      socketSeed = communication.QueueOutbound();

      return BlockchainNetwork.StartNode(communication, countMaxOutbound);
    }

    static bool TryHandshakeOutbound(string nameCase, SocketFake socketPeer, ref string message)
    {
      if (!socketPeer.TryReceive(nameCase, COMMAND_VERSION, out _, ref message))
        return false;

      SendVersion(socketPeer, Network.Configuration.NODE_NETWORK);
      socketPeer.SendMessage(COMMAND_VERACK, 0, []).Wait();

      return socketPeer.TryReceive(nameCase, COMMAND_VERACK, out _, ref message)
        && socketPeer.TryReceive(nameCase, COMMAND_GETADDR, out _, ref message);
    }

    static bool TryHandshakeInbound(string nameCase, SocketFake socketPeer, ref string message)
    {
      SendVersion(socketPeer, Network.Configuration.NODE_NETWORK);

      if (!socketPeer.TryReceive(nameCase, COMMAND_VERACK, out _, ref message)
        || !socketPeer.TryReceive(nameCase, COMMAND_VERSION, out _, ref message))
        return false;

      socketPeer.SendMessage(COMMAND_VERACK, 0, []).Wait();

      return true;
    }

    static void SendAddr(SocketFake socketPeer, List<(string iP, int port)> addresses)
    {
      byte[] payload = [
        .. VarInt.GetBytes(addresses.Count),
        .. addresses.SelectMany(a => (byte[])[
          .. new byte[LENGTH_TIME_ADDRESS],
          .. BitConverter.GetBytes(Network.Configuration.NODE_NETWORK),
          .. IPAddress.Parse(a.iP).MapToIPv6().GetAddressBytes(),
          .. BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness((ushort)a.port))])];

      socketPeer.SendMessage(COMMAND_ADDR, payload.Length, payload).Wait();
    }

    static SocketFake ConnectInboundWithVersion(ulong networkServices, out NetworkTestbench network)
    {
      CommunicationFake communication = new();
      network = BlockchainNetwork.StartNode(communication);

      SocketFake socketPeer = communication.ConnectInbound(IP_PEER);
      SendVersion(socketPeer, networkServices);

      return socketPeer;
    }

    static bool IsPeerListed(string nameCase, NetworkTestbench network, string iP, ref string message)
    {
      if (SpinWait.SpinUntil(() => network.IsPeerListed(iP), TimeSpan.FromSeconds(SECONDS_TIMEOUT_PEER_LISTED)))
        return true;

      message = $"{nameCase}: the node does not list the peer {iP} in Peers after the handshake.";
      return false;
    }

    static bool TryGetAddresses(string nameCase, SocketFake socketPeer, out List<string> iPs, ref string message)
    {
      iPs = new();

      socketPeer.SendMessage(COMMAND_GETADDR, 0, []).Wait();

      if (!socketPeer.TryReceive(nameCase, COMMAND_ADDR, out byte[] payload, ref message))
        return false;

      int startIndex = 0;
      int countAddresses = VarInt.GetInt(payload, ref startIndex);

      for (int i = 0; i < countAddresses; i++)
        iPs.Add(new IPAddress(payload.AsSpan(startIndex + i * LENGTH_ADDRESS + OFFSET_IP_ADDRESS, 16)).MapToIPv4().ToString());

      return true;
    }

    static bool TryReadAddressList(string nameCase, CommunicationFake communication, out List<string> iPs, ref string message)
    {
      iPs = null;

      SocketFake socketReader = communication.ConnectInbound(IP_READER);

      try
      {
        return TryHandshakeInbound(nameCase, socketReader, ref message)
          && TryGetAddresses(nameCase, socketReader, out iPs, ref message);
      }
      finally
      {
        socketReader.Dispose();
      }
    }

    static void SendVersion(SocketFake socketPeer, ulong networkServices)
    {
      byte[] payload = [.. BitConverter.GetBytes(PROTOCOL_VERSION), .. BitConverter.GetBytes(networkServices)];

      socketPeer.SendMessage(COMMAND_VERSION, payload.Length, payload).Wait();
    }

    class NetworkTestbench : NetworkBToken
    {
      internal NetworkTestbench(BlockchainBToken blockchain, ICommunication communication)
        : base(blockchain, communication)
      { }

      internal bool IsPeerListed(string iP)
      {
        lock (LOCK_Peers)
          return Peers.Any(p => p.GetIP() == iP);
      }

      internal void MarkTXDelivered(string iP)
      {
        lock (LOCK_Peers)
          ((TXMessage)Peers.Single(p => p.GetIP() == iP).MessagesByCommand[TXMessage.Command]).TimeDeliveredLast = DateTime.UtcNow;
      }
    }

    class BlockchainNetwork : BlockchainTestbench
    {
      const string NAME_TOKEN = "TestNetwork";


      BlockchainNetwork(LiteDatabase liteDatabase, Blockchain blockchainParent)
        : base(NAME_TOKEN, liteDatabase, blockchainParent)
      { }

      internal static NetworkTestbench StartNode(CommunicationFake communication, int countMaxOutbound = 1, int countMaxInbound = 32)
      {
        CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);
        CreateWalletFile(NAME_TOKEN);

        BlockchainNetwork blockchain = new(CreateLiteDatabase(), new BlockchainParentFake(CreateLiteDatabase()));
        blockchain.Load();

        NetworkTestbench network = new(blockchain, communication);
        network.ConfigurationNetwork.CountMaxOutboundConnections = countMaxOutbound;
        network.ConfigurationNetwork.CountMaxInboundConnections = countMaxInbound;
        network.Start();

        return network;
      }
    }
  }
}
