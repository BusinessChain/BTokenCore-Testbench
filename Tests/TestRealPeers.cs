using BTokenCore;

using LiteDB;

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  class TestRealPeers : Test_Testbench
  {
    const int SECONDS_TIMEOUT_HANDSHAKE = 120;
    const int SECONDS_TIMEOUT_STOP = 10;
    const int SECONDS_TIMEOUT_HEADERS = 60;


    internal TestRealPeers(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      return TestHandshakeReal(ref message)
        && TestHeadersReal(ref message);
    }

    static bool TestHandshakeReal(ref string message)
    {
      Console.WriteLine("  1. Handshake Real: Complete version handshake with a real Bitcoin peer.");

      const string NAME_CASE = "Handshake real";

      NetworkBitcoinRealPeers network = StartNetwork(out _);

      return TryAwaitHandshake(NAME_CASE, network, ref message)
        & IsNetworkStopped(NAME_CASE, network, ref message);
    }

    static bool TestHeadersReal(ref string message)
    {
      Console.WriteLine("  2. Headers Real: Download headers from HeaderRoot to the peer's tip.");

      const string NAME_CASE = "Headers real";

      NetworkBitcoinRealPeers network = StartNetwork(out BlockchainBitcoinRealPeers blockchain);

      return TryDownloadHeaders(NAME_CASE, network, blockchain, ref message)
        & IsNetworkStopped(NAME_CASE, network, ref message);
    }

    static bool TryDownloadHeaders(string nameCase, NetworkBitcoinRealPeers network, BlockchainBitcoinRealPeers blockchain, ref string message)
    {
      if (!TryAwaitHandshake(nameCase, network, ref message))
        return false;

      int heightStartPeer = network.GetHeightStartPeer();
      int heightHeaderTip = 0;

      if (!SpinWait.SpinUntil(() => (heightHeaderTip = blockchain.GetHeightHeaderTip()) >= heightStartPeer, TimeSpan.FromSeconds(SECONDS_TIMEOUT_HEADERS)))
      {
        message = $"{nameCase}: header tip at height {heightHeaderTip} after {SECONDS_TIMEOUT_HEADERS} seconds, peer announced height {heightStartPeer}.";
        return false;
      }

      Console.WriteLine($"     Header tip at height {heightHeaderTip}, peer announced height {heightStartPeer}.");

      return true;
    }

    static NetworkBitcoinRealPeers StartNetwork(out BlockchainBitcoinRealPeers blockchain)
    {
      CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);

      blockchain = new(CreateLiteDatabase(), new SemaphoreSlim(1));
      blockchain.LoadBlockchain();

      NetworkBitcoinRealPeers network = new(blockchain, new CommunicationTCP());
      network.ConfigurationNetwork.CountMaxOutboundConnections = 1;
      network.Start();

      return network;
    }

    static bool TryAwaitHandshake(string nameCase, NetworkBitcoinRealPeers network, ref string message)
    {
      List<string> iPsPeer = [];

      if (!SpinWait.SpinUntil(() => (iPsPeer = network.GetIPsPeer()).Count > 0, TimeSpan.FromSeconds(SECONDS_TIMEOUT_HANDSHAKE)))
      {
        message = $"{nameCase}: no real Bitcoin peer completed the handshake within {SECONDS_TIMEOUT_HANDSHAKE} seconds.";
        return false;
      }

      Console.WriteLine($"     Peer {iPsPeer[0]} completed the handshake.");

      return true;
    }

    static bool IsNetworkStopped(string nameCase, NetworkBitcoinRealPeers network, ref string message)
    {
      network.Stop();

      if (SpinWait.SpinUntil(() => network.GetIPsPeer().Count == 0, TimeSpan.FromSeconds(SECONDS_TIMEOUT_STOP)))
        return true;

      message = $"{nameCase}: Peers still lists a peer {SECONDS_TIMEOUT_STOP} seconds after Stop.";
      return false;
    }

    class BlockchainBitcoinRealPeers : BlockchainBitcoin
    {
      internal BlockchainBitcoinRealPeers(LiteDatabase liteDatabase, SemaphoreSlim semaphoreBlockchain)
        : base(liteDatabase, semaphoreBlockchain)
      { }

      internal int GetHeightHeaderTip()
      {
        return RunLocked(() => ChainRoot.HeaderTip.Height).Result;
      }
    }

    class NetworkBitcoinRealPeers : NetworkBitcoin
    {
      const string COMMAND_VERSION = "version";
      const int OFFSET_USER_AGENT_VERSION = 80;


      internal NetworkBitcoinRealPeers(BlockchainBitcoin blockchain, ICommunication communication)
        : base(blockchain, communication)
      { }

      internal List<string> GetIPsPeer()
      {
        lock (LOCK_Peers)
          return Peers.Select(p => p.GetIP()).ToList();
      }

      internal int GetHeightStartPeer()
      {
        byte[] payload;

        lock (LOCK_Peers)
          payload = Peers.Single().MessagesByCommand[COMMAND_VERSION].Payload;

        int index = OFFSET_USER_AGENT_VERSION;
        int lengthUserAgent = VarInt.GetInt(payload, ref index);
        index += lengthUserAgent;

        return BitConverter.ToInt32(payload, index);
      }
    }
  }
}