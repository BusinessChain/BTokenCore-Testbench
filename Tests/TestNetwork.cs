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
    const uint PROTOCOL_VERSION = 1;
    const int SECONDS_TIMEOUT_PEER_LISTED = 10;


    internal TestNetwork(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      return TestHandshakeOutbound(ref message);
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
        if (!socketPeer.TryReceive(NAME_CASE, COMMAND_VERSION, ref message))
          return false;

        SendVersion(socketPeer, Network.Configuration.NODE_NETWORK);
        socketPeer.SendMessage(COMMAND_VERACK, 0, []).Wait();

        if (!socketPeer.TryReceive(NAME_CASE, COMMAND_VERACK, ref message)
          || !socketPeer.TryReceive(NAME_CASE, COMMAND_GETADDR, ref message))
          return false;

        if (SpinWait.SpinUntil(() => network.IsPeerListed(iPSeed), TimeSpan.FromSeconds(SECONDS_TIMEOUT_PEER_LISTED)))
          return true;

        message = $"{NAME_CASE}: the node does not list the seed peer {iPSeed} in Peers after the handshake.";
        return false;
      }
      finally
      {
        socketPeer.Dispose();
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
