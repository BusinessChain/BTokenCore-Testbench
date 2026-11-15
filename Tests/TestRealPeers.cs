using BTokenCore;

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  class TestRealPeers : Test_Testbench
  {
    const int SECONDS_TIMEOUT_HANDSHAKE = 120;


    internal TestRealPeers(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      return TestHandshakeReal(ref message);
    }

    static bool TestHandshakeReal(ref string message)
    {
      Console.WriteLine("  1. Handshake Real: Complete version handshake with a real Bitcoin peer.");

      CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);

      BlockchainBitcoin blockchain = new(CreateLiteDatabase(), new SemaphoreSlim(1));
      blockchain.LoadBlockchain();

      NetworkBitcoinRealPeers network = new(blockchain, new CommunicationTCP());
      network.ConfigurationNetwork.CountMaxOutboundConnections = 1;
      network.Start();

      List<string> iPsPeer = [];

      if (!SpinWait.SpinUntil(() => (iPsPeer = network.GetIPsPeer()).Count > 0, TimeSpan.FromSeconds(SECONDS_TIMEOUT_HANDSHAKE)))
      {
        message = $"Handshake real: no real Bitcoin peer completed the handshake within {SECONDS_TIMEOUT_HANDSHAKE} seconds.";
        return false;
      }

      Console.WriteLine($"     Peer {iPsPeer[0]} completed the handshake.");

      return true;
    }

    class NetworkBitcoinRealPeers : NetworkBitcoin
    {
      internal NetworkBitcoinRealPeers(BlockchainBitcoin blockchain, ICommunication communication)
        : base(blockchain, communication)
      { }

      internal List<string> GetIPsPeer()
      {
        lock (LOCK_Peers)
          return Peers.Select(p => p.GetIP()).ToList();
      }
    }
  }
}