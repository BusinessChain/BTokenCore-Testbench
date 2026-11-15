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
    const int SECONDS_INTERVAL_PROGRESS = 5;
    const int SECONDS_TIMEOUT_BLOCKS = 120;
    const int COUNT_BLOCKS_DOWNLOAD = 10;


    internal TestRealPeers(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      return TestHandshakeReal(ref message)
        && TestHeadersReal(ref message)
        && TestBlocksReal(ref message);
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

    static bool TestBlocksReal(ref string message)
    {
      Console.WriteLine("  3. Blocks Real: Download and insert ten real blocks after HeaderRoot.");

      const string NAME_CASE = "Blocks real";

      NetworkBitcoinRealPeers network = StartNetwork(out BlockchainBitcoinRealPeers blockchain);

      return TryDownloadBlocks(NAME_CASE, network, blockchain, ref message)
        & IsNetworkStopped(NAME_CASE, network, ref message);
    }

    static bool TryDownloadBlocks(string nameCase, NetworkBitcoinRealPeers network, BlockchainBitcoinRealPeers blockchain, ref string message)
    {
      int heightTarget = blockchain.RunLocked(blockchain.GetHeight).Result + COUNT_BLOCKS_DOWNLOAD;

      if (!TryDownloadHeaders(nameCase, network, blockchain, ref message))
        return false;

      int heightBlockTip = 0;

      if (!WaitUntil(
        () => (heightBlockTip = blockchain.RunLocked(blockchain.GetHeight).Result) >= heightTarget,
        SECONDS_TIMEOUT_BLOCKS,
        "block download",
        () => $"block tip at height {heightBlockTip}, target height {heightTarget}"))
      {
        message = $"{nameCase}: block tip at height {heightBlockTip} after {SECONDS_TIMEOUT_BLOCKS} seconds, target height {heightTarget}.";
        return false;
      }

      Console.WriteLine($"     Block tip at height {heightBlockTip}.");

      return true;
    }

    static bool TryDownloadHeaders(string nameCase, NetworkBitcoinRealPeers network, BlockchainBitcoinRealPeers blockchain, ref string message)
    {
      if (!TryAwaitHandshake(nameCase, network, ref message))
        return false;

      int heightStartPeer = network.GetHeightStartPeer();
      int heightHeaderTip = 0;

      if (!WaitUntil(
        () => (heightHeaderTip = blockchain.GetHeightHeaderTip()) >= heightStartPeer,
        SECONDS_TIMEOUT_HEADERS,
        "header download",
        () => $"header tip at height {heightHeaderTip}, peer announced height {heightStartPeer}"))
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

      NetworkBitcoinRealPeers network = new(blockchain, new CommunicationLogging());
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

      int countPeers = 0;

      if (WaitUntil(
        () => (countPeers = network.GetIPsPeer().Count) == 0,
        SECONDS_TIMEOUT_STOP,
        "disconnection",
        () => $"{countPeers} peers still in Peers"))
        return true;

      message = $"{nameCase}: Peers still lists a peer {SECONDS_TIMEOUT_STOP} seconds after Stop.";
      return false;
    }

    static bool WaitUntil(Func<bool> condition, int secondsTimeout, string nameWait, Func<string> getState)
    {
      DateTime timeDeadline = DateTime.UtcNow.AddSeconds(secondsTimeout);

      while (true)
      {
        TimeSpan timeLeft = timeDeadline - DateTime.UtcNow;

        if (timeLeft <= TimeSpan.Zero)
          return false;

        TimeSpan timeSlice = TimeSpan.FromSeconds(SECONDS_INTERVAL_PROGRESS);

        if (SpinWait.SpinUntil(condition, timeLeft < timeSlice ? timeLeft : timeSlice))
          return true;

        int secondsLeft = (int)(timeDeadline - DateTime.UtcNow).TotalSeconds;

        if (secondsLeft > 0)
          Console.WriteLine($"     Waiting for {nameWait}: {getState()}, {secondsLeft} seconds left.");
      }
    }

    static void WriteLog(string iP, string text)
    {
      Console.WriteLine($"     {DateTime.Now:HH:mm:ss.fff} {iP}: {text}");
    }

    class CommunicationLogging : ICommunication
    {
      ICommunication CommunicationTCP = new CommunicationTCP();


      public void StartListenerCommunicationInbound(int port)
      {
        CommunicationTCP.StartListenerCommunicationInbound(port);
      }

      public async Task<ISocketCommunication> AcceptSocketCommunicationInbound()
      {
        return new SocketLogging(await CommunicationTCP.AcceptSocketCommunicationInbound());
      }

      public ISocketCommunication GetSocketCommunication(int port, string address)
      {
        return new SocketLogging(CommunicationTCP.GetSocketCommunication(port, address));
      }
    }

    class SocketLogging : ISocketCommunication
    {
      const string COMMAND_BLOCK = "block";
      const string COMMAND_HEADERS = "headers";
      static readonly string[] COMMANDS_INVENTORY = ["inv", "getdata", "notfound"];
      const int LENGTH_HEADER = 80;
      const int LENGTH_INVENTORY = 36;
      const int LENGTH_INVENTORY_TYPE = 4;
      const int LENGTH_HASH = 32;

      ISocketCommunication SocketTCP;
      string CommandReceived;
      bool IsDisposed;


      internal SocketLogging(ISocketCommunication socketTCP)
      {
        SocketTCP = socketTCP;
      }

      public async Task Start(byte[] magicBytes)
      {
        WriteLog(GetIP(), "connecting");

        try
        {
          await SocketTCP.Start(magicBytes);
        }
        catch (Exception exception)
        {
          WriteLog(GetIP(), $"connect failed: {exception.Message}");
          throw;
        }

        WriteLog(GetIP(), "connected");
      }

      public async Task SendMessage(string commandString, int lengthDataPayload, byte[] payload)
      {
        WriteLog(GetIP(), $"sent {DescribeMessage(commandString, lengthDataPayload, payload)}");

        await SocketTCP.SendMessage(commandString, lengthDataPayload, payload);
      }

      public async Task<string> ReceiveCommandMessageNext()
      {
        try
        {
          CommandReceived = await SocketTCP.ReceiveCommandMessageNext();
        }
        catch (Exception exception)
        {
          WriteLogReceiveFailed(exception);
          throw;
        }

        return CommandReceived;
      }

      public async Task<int> ReceivePayloadNext(byte[] buffer)
      {
        int lengthDataPayload;

        try
        {
          lengthDataPayload = await SocketTCP.ReceivePayloadNext(buffer);
        }
        catch (Exception exception)
        {
          WriteLogReceiveFailed(exception);
          throw;
        }

        WriteLog(GetIP(), $"received {DescribeMessage(CommandReceived, lengthDataPayload, buffer)}");

        return lengthDataPayload;
      }

      public void Dispose()
      {
        if (!IsDisposed)
          WriteLog(GetIP(), "disconnected");

        IsDisposed = true;
        SocketTCP.Dispose();
      }

      public string GetIP()
      {
        return SocketTCP.GetIP();
      }

      void WriteLogReceiveFailed(Exception exception)
      {
        if (!IsDisposed)
          WriteLog(GetIP(), $"receive failed: {exception.GetType().Name}: {exception.Message}");
      }

      static string DescribeMessage(string command, int lengthDataPayload, byte[] payload)
      {
        string description = $"{command} ({lengthDataPayload} bytes)";

        if (command == COMMAND_BLOCK && lengthDataPayload >= LENGTH_HEADER)
          return $"{description} hash {FormatHash(Crypto.ComputeHash256(payload.AsSpan(0, LENGTH_HEADER)))}";

        if (lengthDataPayload == 0)
          return description;

        int index = 0;
        int count = VarInt.GetInt(payload, ref index);

        if (command == COMMAND_HEADERS)
          return $"{description} count {count}";

        if (COMMANDS_INVENTORY.Contains(command) && count > 0 && index + LENGTH_INVENTORY <= lengthDataPayload)
          return $"{description} count {count}, first type {BitConverter.ToUInt32(payload, index)} hash {FormatHash(payload.AsSpan(index + LENGTH_INVENTORY_TYPE, LENGTH_HASH).ToArray())}";

        return description;
      }

      static string FormatHash(byte[] hash)
      {
        return Convert.ToHexString(hash.Reverse().ToArray()).ToLowerInvariant();
      }
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