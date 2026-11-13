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
    const int SECONDS_TIMEOUT_PEER_LISTED = 10;


    internal TestNetwork(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      return TestHandshakeOutbound(ref message)
        && TestHandshakeInbound(ref message);
    }

    static bool TestHandshakeOutbound(ref string message)
    {
      Console.WriteLine("  1. Handshake Outbound: Complete version and verack with the seed peer.");

      const string NAME_CASE = "Handshake outbound";

      CommunicationFake communication = new();
      SocketFake socketPeer = communication.QueueOutbound();

      NetworkTestbench network = BlockchainNetwork.StartNode(communication);
      string iPSeed = network.ConfigurationNetwork.SeedAddresses[0];

      try
      {
        if (!socketPeer.TryReceive(NAME_CASE, COMMAND_VERSION, out _, ref message))
          return false;

        SendVersion(socketPeer, Network.Configuration.NODE_NETWORK);
        socketPeer.SendMessage(COMMAND_VERACK, 0, []).Wait();

        return socketPeer.TryReceive(NAME_CASE, COMMAND_VERACK, out _, ref message)
          && socketPeer.TryReceive(NAME_CASE, COMMAND_GETADDR, out _, ref message)
          && IsPeerListed(NAME_CASE, network, iPSeed, ref message);
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool TestHandshakeInbound(ref string message)
    {
      Console.WriteLine("  2. Handshake Inbound: Accept an inbound peer and store its IP.");

      const string NAME_CASE = "Handshake inbound";
      const string IP_PEER = "10.0.0.2";

      CommunicationFake communication = new();
      NetworkTestbench network = BlockchainNetwork.StartNode(communication);
      SocketFake socketPeer = communication.ConnectInbound(IP_PEER);

      try
      {
        SendVersion(socketPeer, Network.Configuration.NODE_NETWORK);

        if (!socketPeer.TryReceive(NAME_CASE, COMMAND_VERACK, out _, ref message)
          || !socketPeer.TryReceive(NAME_CASE, COMMAND_VERSION, out _, ref message))
          return false;

        socketPeer.SendMessage(COMMAND_VERACK, 0, []).Wait();

        if (!IsPeerListed(NAME_CASE, network, IP_PEER, ref message))
          return false;

        socketPeer.SendMessage(COMMAND_GETADDR, 0, []).Wait();

        if (!socketPeer.TryReceive(NAME_CASE, COMMAND_ADDR, out byte[] payloadAddr, ref message))
          return false;

        if (ParseIPsAddr(payloadAddr).Contains(IP_PEER))
          return true;

        message = $"{NAME_CASE}: the addr answer lacks the inbound peer's IP {IP_PEER}.";
        return false;
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool IsPeerListed(string nameCase, NetworkTestbench network, string iP, ref string message)
    {
      if (SpinWait.SpinUntil(() => network.IsPeerListed(iP), TimeSpan.FromSeconds(SECONDS_TIMEOUT_PEER_LISTED)))
        return true;

      message = $"{nameCase}: the node does not list the peer {iP} in Peers after the handshake.";
      return false;
    }

    static List<string> ParseIPsAddr(byte[] payload)
    {
      int startIndex = 0;
      int countAddresses = VarInt.GetInt(payload, ref startIndex);

      List<string> iPs = new();

      for (int i = 0; i < countAddresses; i++)
        iPs.Add(new IPAddress(payload.AsSpan(startIndex + i * LENGTH_ADDRESS + OFFSET_IP_ADDRESS, 16)).MapToIPv4().ToString());

      return iPs;
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
    }

    class BlockchainNetwork : BlockchainTestbench
    {
      const string NAME_TOKEN = "TestNetwork";


      BlockchainNetwork(LiteDatabase liteDatabase, Blockchain blockchainParent)
        : base(NAME_TOKEN, liteDatabase, blockchainParent)
      { }

      internal static NetworkTestbench StartNode(CommunicationFake communication)
      {
        CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);
        CreateWalletFile(NAME_TOKEN);

        BlockchainNetwork blockchain = new(CreateLiteDatabase(), new BlockchainParentFake(CreateLiteDatabase()));
        blockchain.Load();

        NetworkTestbench network = new(blockchain, communication);
        network.ConfigurationNetwork.CountMaxOutboundConnections = 1;
        network.Start();

        return network;
      }
    }
  }
}
