using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

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
    const string COMMAND_PING = "ping";
    const string COMMAND_PONG = "pong";
    const int LENGTH_NONCE_PING = 8;
    const string COMMAND_GETDATA = "getdata";
    const string COMMAND_BLOCK = "block";
    const string COMMAND_HEADERS = "headers";
    const string COMMAND_NOTFOUND = "notfound";
    const int LENGTH_INVENTORY_TYPE = 4;
    const int LENGTH_INVENTORY = 36;
    const Network.Inventory.InventoryType MSG_BLOCK = Network.Inventory.InventoryType.MSG_BLOCK;
    const Network.Inventory.InventoryType MSG_SEGMENT_SWEEP = Network.Inventory.InventoryType.MSG_SEGMENT_SWEEP;
    const string COMMAND_SEGMENT = "segment";
    const string COMMAND_INV = "inv";
    const string COMMAND_TX = "tx";
    const Network.Inventory.InventoryType MSG_TX = Network.Inventory.InventoryType.MSG_TX;
    const int HEIGHT_TIP_WINDOW = 2060;
    const uint PROTOCOL_VERSION = 1;
    const int LENGTH_ADDRESS = 30;
    const int OFFSET_IP_ADDRESS = 12;
    const string IP_PEER = "10.0.0.2";
    const string IP_READER = "10.0.0.9";
    const string IP_PEER_OTHER = "10.0.0.3";
    const string COMMAND_FEEFILTER = "feefilter";
    const string COMMAND_FRAMING = "framing";
    static readonly byte[] MAGIC_BYTES = [0xB7, 0xD0, 0xC3, 0xE5];
    const string IP_SEED_BITCOIN = "10.0.5.1";
    const string COMMAND_GETHEADERS = "getheaders";
    const int LENGTH_PROTOCOL_VERSION = 4;
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
        && TestInboundEviction(ref message)
        && TestAddressPort(ref message)
        && TestAddressDOS(ref message)
        && TestBlockDownload(Testbench.Wallet, ref message)
        && TestBlockRefusal(Testbench.Wallet, ref message)
        && TestUnrequestedBlock(ref message)
        && TestRequestTimeout(Testbench.Wallet, ref message)
        && TestBlockServing(Testbench.Wallet, ref message)
        && TestServingDOS(Testbench.Wallet, ref message)
        && TestBlockAnnouncement(Testbench.Wallet, ref message)
        && TestSegmentServing(Testbench.Wallet, ref message)
        && TestUnrequestedSegment(ref message)
        && BlockchainNetwork.TryCreateBlocks(Testbench.Wallet, [], out List<Block> blocksWindow, out _, ref message, HEIGHT_TIP_WINDOW)
        && TestSegmentRequest(blocksWindow, ref message)
        && TestSegmentRefusal(blocksWindow, ref message)
        && TestTXRequest(Testbench.Wallet, ref message)
        && TestTXRelay(Testbench.Wallet, ref message)
        && TestTXTimeout(Testbench.Wallet, ref message)
        && TestTXNotFound(Testbench.Wallet, ref message)
        && TestFeeFilter(Testbench.Wallet, ref message)
        && TestTXThrottle(Testbench.Wallet, ref message)
        && TestOwnTX(Testbench.Wallet, ref message)
        && TestFramingSend(ref message)
        && TestSplitReads(ref message)
        && TestMagicMismatch(ref message)
        && TestPayloadLength(ref message)
        && TestStreamEnd(ref message)
        && TestGetheadersLocator(ref message)
        && TestLocatorLimit(ref message)
        && TestHeadersInsertion(ref message)
        && TestReturnIdle(ref message)
        && TestEndToEnd(blocksWindow, ref message);
    }

    static bool TestEndToEnd(List<Block> blocksWindow, ref string message)
    {
      Console.WriteLine("  38. End To End: A fresh node syncs blocks and segments from a peer.");

      const string NAME_CASE = "End to end";
      const string IP_FRESH = "10.0.6.1";
      const int SECONDS_TIMEOUT_SYNC = 300;

      CommunicationFake communicationServing = new();
      CommunicationFake communicationFresh = new();
      communicationFresh.LinkOutbound(communicationServing, IP_FRESH);

      BlockchainNetwork.StartNode(communicationServing, prepare: b => b.AnchorAndDeliver(blocksWindow));
      NetworkTestbench networkFresh = BlockchainNetwork.StartNode(
        communicationFresh,
        prepareParent: p =>
        {
          p.MineAnchors(blocksWindow);
          p.MineBlock([]);
        });

      return IsAtHeight(NAME_CASE, networkFresh, blocksWindow[^1].Header.Height, ref message, SECONDS_TIMEOUT_SYNC)
        && ((BlockchainNetwork)networkFresh.Blockchain).IsAccountsSynchronized(NAME_CASE, SECONDS_TIMEOUT_SYNC, ref message);
    }

    static bool TestReturnIdle(ref string message)
    {
      Console.WriteLine("  37. Return Idle: Request blocks after an empty headers message.");

      const string NAME_CASE = "Return idle";

      bool isSent = TrySendHeadersOnTip(NAME_CASE, out SocketFake socketSeed, out List<Header> headersNew, ref message);

      try
      {
        if (!isSent)
          return false;

        SendHeaders(socketSeed);

        if (!TryReceiveRequest(NAME_CASE, socketSeed, MSG_BLOCK, out byte[] hash, ref message))
          return false;

        if (hash.IsAllBytesEqual(headersNew[0].Hash))
          return true;

        message = $"{NAME_CASE}: the node requests block {hash.ToHexString()}, expected the first received header {headersNew[0].Hash.ToHexString()}.";
        return false;
      }
      finally
      {
        socketSeed.Dispose();
      }
    }

    static bool TestHeadersInsertion(ref string message)
    {
      Console.WriteLine("  36. Headers Insertion: Insert received headers, request the next from the new tip.");

      bool isInserted = TrySendHeadersOnTip("Headers insertion", out SocketFake socketSeed, out _, ref message);
      socketSeed.Dispose();

      return isInserted;
    }

    static bool TrySendHeadersOnTip(string nameCase, out SocketFake socketSeed, out List<Header> headersNew, ref string message)
    {
      const int COUNT_BLOCKS = 10;
      const int COUNT_HEADERS_NEW = 3;

      socketSeed = BlockchainNetwork.StartNodeBitcoin(COUNT_BLOCKS, out List<Header> headers);
      headersNew = [];

      if (!TryHandshakeBitcoin(nameCase, socketSeed, out _, ref message))
        return false;

      for (Header headerPrevious = headers[^1]; headersNew.Count < COUNT_HEADERS_NEW; headerPrevious = headersNew[^1])
      {
        HeaderBToken header = new(
          headerHash: null,
          hashPrevious: headerPrevious.Hash,
          merkleRootHash: RandomNumberGenerator.GetBytes(Crypto.LENGTH_HASH256),
          hashAccounts: new byte[Crypto.LENGTH_HASH256],
          nonce: 0);

        header.ComputeHash();
        headersNew.Add(header);
      }

      SendHeaders(socketSeed, [.. headersNew]);

      if (!TryReceiveGetheaders(nameCase, socketSeed, out List<byte[]> locator, ref message))
        return false;

      if (locator.Count == 1 && locator[0].IsAllBytesEqual(headersNew[^1].Hash))
        return true;

      message = $"{nameCase}: the next getheaders holds the locator {string.Join(", ", locator.Select(h => h.ToHexString()))}, " +
        $"expected only the last received header {headersNew[^1].Hash.ToHexString()}.";
      return false;
    }

    static bool TestLocatorLimit(ref string message)
    {
      Console.WriteLine("  35. Locator Limit: Serve headers after the locator, reject an oversized locator.");

      const string NAME_CASE = "Locator limit";
      const int COUNT_BLOCKS = 10;
      const int HEIGHT_LOCATOR = 3;
      const int COUNT_HASHES_LOCATOR_OVERSIZED = 102;

      SocketFake socketSeed = BlockchainNetwork.StartNodeBitcoin(COUNT_BLOCKS, out List<Header> headers);

      try
      {
        if (!TryHandshakeBitcoin(NAME_CASE, socketSeed, out _, ref message))
          return false;

        SendGetheaders(socketSeed, [headers[HEIGHT_LOCATOR - 1].Hash]);

        if (!socketSeed.TryReceive(NAME_CASE, COMMAND_HEADERS, out byte[] payload, ref message))
          return false;

        List<Header> headersExpected = headers.Skip(HEIGHT_LOCATOR).ToList();
        byte[] payloadExpected = [.. VarInt.GetBytes(headersExpected.Count), .. headersExpected.SelectMany(h => (byte[])[.. h.Serialize(), 0x00])];

        if (!payload.IsAllBytesEqual(payloadExpected))
        {
          message = $"{NAME_CASE}: the node serves {payload.ToHexString()}, expected the headers at heights {HEIGHT_LOCATOR + 1} to {COUNT_BLOCKS}.";
          return false;
        }

        SendGetheaders(socketSeed, [.. Enumerable.Range(0, COUNT_HASHES_LOCATOR_OVERSIZED).Select(i => RandomNumberGenerator.GetBytes(Crypto.LENGTH_HASH256))]);

        return socketSeed.IsDisconnected(NAME_CASE, ref message);
      }
      finally
      {
        socketSeed.Dispose();
      }
    }

    static void SendGetheaders(SocketFake socketPeer, List<byte[]> locator)
    {
      byte[] payload = [
        .. BitConverter.GetBytes(PROTOCOL_VERSION),
        .. VarInt.GetBytes(locator.Count),
        .. locator.SelectMany(h => h),
        .. new byte[Crypto.LENGTH_HASH256]];

      socketPeer.SendMessage(COMMAND_GETHEADERS, payload.Length, payload).Wait();
    }

    static bool TestGetheadersLocator(ref string message)
    {
      Console.WriteLine("  34. Getheaders Locator: Send getheaders with an exponential locator after verack.");

      const string NAME_CASE = "Getheaders locator";
      const int COUNT_BLOCKS = 10;

      SocketFake socketSeed = BlockchainNetwork.StartNodeBitcoin(COUNT_BLOCKS, out List<Header> headers);

      try
      {
        if (!TryHandshakeBitcoin(NAME_CASE, socketSeed, out List<byte[]> locator, ref message))
          return false;

        List<byte[]> locatorExpected = [.. new[] { 10, 9, 7, 3 }.Select(h => headers[h - 1].Hash), headers[0].HashPrevious];

        if (locator.Count == locatorExpected.Count && locator.Zip(locatorExpected).All(l => l.First.IsAllBytesEqual(l.Second)))
          return true;

        message = $"{NAME_CASE}: the locator holds {string.Join(", ", locator.Select(h => h.ToHexString()))}, " +
          $"expected the hashes at heights 10, 9, 7, 3 and 0.";
        return false;
      }
      finally
      {
        socketSeed.Dispose();
      }
    }

    static bool TryHandshakeBitcoin(string nameCase, SocketFake socketPeer, out List<byte[]> locator, ref string message)
    {
      locator = null;

      if (!socketPeer.TryReceive(nameCase, COMMAND_VERSION, out _, ref message))
        return false;

      SendVersion(socketPeer, Network.Configuration.NODE_NETWORK);
      socketPeer.SendMessage(COMMAND_VERACK, 0, []).Wait();

      return TryReceiveGetheaders(nameCase, socketPeer, out locator, ref message);
    }

    static bool TryReceiveGetheaders(string nameCase, SocketFake socketPeer, out List<byte[]> locator, ref string message)
    {
      locator = null;

      if (!socketPeer.TryReceive(nameCase, COMMAND_GETHEADERS, out byte[] payload, ref message))
        return false;

      int startIndex = LENGTH_PROTOCOL_VERSION;
      int countHashes = VarInt.GetInt(payload, ref startIndex);

      locator = [.. Enumerable.Range(0, countHashes).Select(i => payload[(startIndex + i * Crypto.LENGTH_HASH256)..(startIndex + (i + 1) * Crypto.LENGTH_HASH256)])];
      return true;
    }

    static bool TestStreamEnd(ref string message)
    {
      Console.WriteLine("  33. Stream End: Throw IOException when the stream ends early.");

      const int LENGTH_PAYLOAD = 10;
      const int COUNT_BYTES_MISSING = 5;

      byte[] messageCut = BuildMessage(MAGIC_BYTES, COMMAND_FRAMING, LENGTH_PAYLOAD, new byte[LENGTH_PAYLOAD])[..^COUNT_BYTES_MISSING];

      return IsStreamEndDetected("stream closed before any byte", [], s => s.ReceiveCommandMessageNext(), ref message)
        && IsStreamEndDetected("stream closed in the middle of the payload", messageCut, async s =>
        {
          await s.ReceiveCommandMessageNext();
          await s.ReceivePayloadNext(new byte[LENGTH_PAYLOAD]);
        }, ref message);
    }

    static bool IsStreamEndDetected(string nameCase, byte[] bytesWritten, Func<SocketTCP, Task> receive, ref string message)
    {
      using ConnectionLoopback connection = new();

      connection.StreamRaw.Write(bytesWritten);
      connection.ClientRaw.Close();

      try
      {
        receive(connection.Socket).GetAwaiter().GetResult();
      }
      catch (IOException)
      {
        return true;
      }
      catch (Exception exception)
      {
        message = $"Stream end, {nameCase}: SocketTCP throws {exception.GetType().Name} instead of IOException: {exception.Message}";
        return false;
      }

      message = $"Stream end, {nameCase}: SocketTCP receives without throwing an IOException.";
      return false;
    }

    static bool TestPayloadLength(ref string message)
    {
      Console.WriteLine("  32. Payload Length: Reject payload lengths beyond the buffer or negative.");

      const int SIZE_BUFFER = 16;

      return IsPayloadLengthAccepted("declared length equal to the buffer size", SIZE_BUFFER, SIZE_BUFFER, true, ref message)
        && IsPayloadLengthAccepted("declared length exceeding the buffer size", SIZE_BUFFER, SIZE_BUFFER + 1, false, ref message)
        && IsPayloadLengthAccepted("negative declared length", SIZE_BUFFER, -1, false, ref message);
    }

    static bool IsPayloadLengthAccepted(string nameCase, int sizeBuffer, int lengthDeclared, bool isAcceptedExpected, ref string message)
    {
      using ConnectionLoopback connection = new();

      connection.StreamRaw.Write(BuildMessage(MAGIC_BYTES, COMMAND_FRAMING, lengthDeclared, new byte[Math.Max(lengthDeclared, 0)]));
      connection.Socket.ReceiveCommandMessageNext().GetAwaiter().GetResult();

      string nameRule = $"Payload length, {nameCase}";
      Action receive = () => connection.Socket.ReceivePayloadNext(new byte[sizeBuffer]).GetAwaiter().GetResult();

      if (isAcceptedExpected)
        return IsAccepted(nameRule, receive, ref message);

      return IsRejected(nameRule, receive, ref message);
    }

    static bool TestMagicMismatch(ref string message)
    {
      Console.WriteLine("  31. Magic Mismatch: Reject a message with other magic bytes.");

      using ConnectionLoopback connection = new();

      byte[] magicBytesOther = [.. MAGIC_BYTES.Select(b => (byte)~b)];

      connection.StreamRaw.Write(BuildMessage(magicBytesOther, COMMAND_FRAMING, 0, []));

      return IsRejected("Magic mismatch", () => connection.Socket.ReceiveCommandMessageNext().GetAwaiter().GetResult(), ref message);
    }

    static bool TestSplitReads(ref string message)
    {
      Console.WriteLine("  30. Split Reads: Reassemble a message written one byte at a time.");

      const string NAME_CASE = "Split reads";
      const int MILLISECONDS_PAUSE = 1;
      const int SECONDS_TIMEOUT_RECEIVE = 10;

      using ConnectionLoopback connection = new();
      connection.ClientRaw.NoDelay = true;

      byte[] payload = [.. Enumerable.Range(1, 10).Select(i => (byte)i)];
      byte[] buffer = new byte[2 * payload.Length];

      Task<(string command, int lengthDataPayload)> receive = Task.Run(async () =>
      {
        string command = await connection.Socket.ReceiveCommandMessageNext();
        return (command, await connection.Socket.ReceivePayloadNext(buffer));
      });

      foreach (byte b in BuildMessage(MAGIC_BYTES, COMMAND_FRAMING, payload.Length, payload))
      {
        connection.StreamRaw.WriteByte(b);
        connection.StreamRaw.Flush();
        Thread.Sleep(MILLISECONDS_PAUSE);
      }

      if (!receive.Wait(TimeSpan.FromSeconds(SECONDS_TIMEOUT_RECEIVE)))
      {
        message = $"{NAME_CASE}: SocketTCP received no message within {SECONDS_TIMEOUT_RECEIVE} seconds.";
        return false;
      }

      (string commandReceived, int lengthReceived) = receive.Result;

      if (commandReceived == COMMAND_FRAMING && buffer[..lengthReceived].IsAllBytesEqual(payload))
        return true;

      message = $"{NAME_CASE}: SocketTCP receives command {commandReceived} with payload {buffer[..lengthReceived].ToHexString()}, " +
        $"expected {COMMAND_FRAMING} with {payload.ToHexString()}.";
      return false;
    }

    static bool TestFramingSend(ref string message)
    {
      Console.WriteLine("  29. Framing Send: Frame magic, padded command, length, checksum and payload.");

      const int LENGTH_DATA_PAYLOAD = 6;

      using ConnectionLoopback connection = new();

      byte[] payload = [.. Enumerable.Range(1, 10).Select(i => (byte)i)];

      connection.Socket.SendMessage(COMMAND_FRAMING, LENGTH_DATA_PAYLOAD, payload).GetAwaiter().GetResult();

      byte[] messageExpected = BuildMessage(MAGIC_BYTES, COMMAND_FRAMING, LENGTH_DATA_PAYLOAD, payload[..LENGTH_DATA_PAYLOAD]);
      byte[] messageSent = new byte[messageExpected.Length];

      connection.StreamRaw.ReadExactly(messageSent);

      if (messageSent.IsAllBytesEqual(messageExpected))
        return true;

      message = $"Framing send: SocketTCP sends {messageSent.ToHexString()}, expected {messageExpected.ToHexString()}.";
      return false;
    }

    static byte[] BuildMessage(byte[] magicBytes, string command, int lengthDeclared, byte[] payload)
    {
      return [
        .. magicBytes,
        .. Encoding.ASCII.GetBytes(command.PadRight(SocketTCP.SIZE_COMMAND, '\0')),
        .. BitConverter.GetBytes(lengthDeclared),
        .. Crypto.ComputeHash256(payload)[..SocketTCP.SIZE_CHECKSUM],
        .. payload];
    }

    class ConnectionLoopback : IDisposable
    {
      const int MILLISECONDS_TIMEOUT_READ = 5000;

      TcpListener Listener = new(IPAddress.Loopback, 0);
      internal TcpClient ClientRaw = new();
      internal NetworkStream StreamRaw;
      internal SocketTCP Socket;


      internal ConnectionLoopback()
      {
        Listener.Start();
        ClientRaw.Connect(IPAddress.Loopback, ((IPEndPoint)Listener.LocalEndpoint).Port);

        Socket = new SocketTCP(Listener.AcceptTcpClient());
        Socket.Start(MAGIC_BYTES).GetAwaiter().GetResult();

        StreamRaw = ClientRaw.GetStream();
        StreamRaw.ReadTimeout = MILLISECONDS_TIMEOUT_READ;
      }

      public void Dispose()
      {
        Socket.Dispose();
        ClientRaw.Dispose();
        Listener.Stop();
      }
    }

    static bool TestTXTimeout(Wallet wallet, ref string message)
    {
      Console.WriteLine("  24. TX Timeout: Disconnect a peer that does not answer a TX request.");

      const string NAME_CASE = "TX timeout";

      if (!TryStartNodeWithTXRequested(NAME_CASE, wallet, out NetworkTestbench network, out SocketFake socketPeer, out _, ref message))
        return false;

      try
      {
        network.AgeTXRequests(IP_PEER, Network.TIMEOUT_REQUEST_SECONDS);

        return socketPeer.IsDisconnected(NAME_CASE, ref message);
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool TestTXNotFound(Wallet wallet, ref string message)
    {
      Console.WriteLine("  25. TX Notfound: Clear TX requests answered by notfound, keep the peer.");

      const string NAME_CASE = "TX notfound";
      const int SECONDS_HOLD = 3;

      if (!TryStartNodeWithTXRequested(NAME_CASE, wallet, out NetworkTestbench network, out SocketFake socketPeer, out byte[] hashRequested, ref message))
        return false;

      try
      {
        SendInventory(socketPeer, MSG_TX, COMMAND_NOTFOUND, hashRequested);

        if (!TrySynchronize(NAME_CASE, socketPeer, ref message))
          return false;

        network.AgeTXRequests(IP_PEER, Network.TIMEOUT_REQUEST_SECONDS);

        string messageHold = "";

        if (!socketPeer.IsDisconnected(NAME_CASE, ref messageHold, SECONDS_HOLD) && network.IsPeerListed(IP_PEER))
          return true;

        message = $"{NAME_CASE}: the node disconnects the peer for a TX request it answered with notfound.";
        return false;
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool TryStartNodeWithTXRequested(string nameCase, Wallet wallet, out NetworkTestbench network, out SocketFake socketPeer, out byte[] hashRequested, ref string message)
    {
      network = null;
      socketPeer = null;
      hashRequested = null;

      if (!BlockchainNetwork.TryCreateBlocks(wallet, [t => { }], out List<Block> blocks, out List<BlockchainBToken.TXBToken> spends, ref message))
        return false;

      hashRequested = spends[0].Hash;
      network = StartNodeWithInboundPeer(out socketPeer, out _, b => b.AnchorAndDeliver(blocks));

      if (TryHandshakeInbound(nameCase, socketPeer, ref message)
        && IsPeerListed(nameCase, network, IP_PEER, ref message)
        && IsOnlyTXRequested(nameCase, socketPeer, [hashRequested], hashRequested, ref message))
        return true;

      socketPeer.Dispose();
      return false;
    }

    static bool TestTXRelay(Wallet wallet, ref string message)
    {
      Console.WriteLine("  23. TX Relay: Relay a received TX to the other peers and serve it.");

      const string NAME_CASE = "TX relay";

      if (!BlockchainNetwork.TryCreateBlocks(wallet, [t => { }], out List<Block> blocks, out List<BlockchainBToken.TXBToken> spends, ref message))
        return false;

      BlockchainBToken.TXBToken spend = spends[0];

      NetworkTestbench network = StartNodeWithTwoPeers(blocks, out SocketFake socketPeer, out SocketFake socketOther);

      try
      {
        if (!TryHandshakeTwoPeers(NAME_CASE, network, socketPeer, socketOther, ref message)
          || !IsOnlyTXRequested(NAME_CASE, socketPeer, [spend.Hash], spend.Hash, ref message))
          return false;

        socketPeer.SendMessage(COMMAND_TX, spend.TXRaw.Length, spend.TXRaw).Wait();

        if (!IsTXAnnounced(NAME_CASE, socketOther, spend.Hash, ref message))
          return false;

        SendInventory(socketOther, MSG_TX, COMMAND_GETDATA, spend.Hash);

        if (!socketOther.TryReceive(NAME_CASE, COMMAND_TX, out byte[] tXRaw, ref message))
          return false;

        if (!tXRaw.IsAllBytesEqual(spend.TXRaw))
        {
          message = $"{NAME_CASE}: the node serves the TX {tXRaw.ToHexString()}, expected the relayed spend {spend.TXRaw.ToHexString()}.";
          return false;
        }

        if (!IsAnsweredNotFound(NAME_CASE, socketOther, MSG_TX, ref message))
          return false;

        if (IsNoTXAnnounced(socketPeer))
          return true;

        message = $"{NAME_CASE}: the node announces the spend back to the peer that delivered it.";
        return false;
      }
      finally
      {
        socketPeer.Dispose();
        socketOther.Dispose();
      }
    }

    static bool TestFeeFilter(Wallet wallet, ref string message)
    {
      Console.WriteLine("  26. Fee Filter: Announce no TX below the peer's fee filter.");

      const string NAME_CASE = "Fee filter";
      const double FEE_FILTER = 100;

      if (!BlockchainNetwork.TryCreateBlocks(wallet, [t => t.Fee = 100_000, t => t.Nonce += 1], out List<Block> blocks, out List<BlockchainBToken.TXBToken> spends, ref message))
        return false;

      NetworkTestbench network = StartNodeWithTwoPeers(blocks, out SocketFake socketPeer, out SocketFake socketOther);

      try
      {
        if (!TryHandshakeTwoPeers(NAME_CASE, network, socketPeer, socketOther, ref message))
          return false;

        byte[] payloadFeeFilter = BitConverter.GetBytes(FEE_FILTER);
        socketOther.SendMessage(COMMAND_FEEFILTER, payloadFeeFilter.Length, payloadFeeFilter).Wait();

        if (!TrySynchronize(NAME_CASE, socketOther, ref message))
          return false;

        foreach (BlockchainBToken.TXBToken spend in spends)
          socketPeer.SendMessage(COMMAND_TX, spend.TXRaw.Length, spend.TXRaw).Wait();

        if (!TryReceiveInventories(NAME_CASE, socketOther, COMMAND_INV, MSG_TX, out List<byte[]> hashes, ref message))
          return false;

        if (hashes.Count == 1 && hashes[0].IsAllBytesEqual(spends[0].Hash))
          return true;

        message = $"{NAME_CASE}: the node announces the TXs {string.Join(", ", hashes.Select(h => h.ToHexString()))}, " +
          $"expected only {spends[0].Hash.ToHexString()} above the fee filter.";
        return false;
      }
      finally
      {
        socketPeer.Dispose();
        socketOther.Dispose();
      }
    }

    static bool TestTXThrottle(Wallet wallet, ref string message)
    {
      Console.WriteLine("  27. TX Throttle: Hold back a TX beyond the announcement throttle until drained.");

      const string NAME_CASE = "TX throttle";
      const int COUNT_OUTPUTS_FILLER = 600;
      const int COUNT_BYTES_MISSING = 10_000;
      const int SECONDS_TIMEOUT_DRAIN = 15;

      if (!BlockchainNetwork.TryCreateBlocks(
        wallet,
        [t => t.TXOutputs.AddRange(Enumerable.Range(0, COUNT_OUTPUTS_FILLER).Select(i => new TXOutputP2PKH(0, ((TXOutputP2PKH)t.TXOutputs[0]).IDAccount)))],
        out List<Block> blocks,
        out List<BlockchainBToken.TXBToken> spends,
        ref message))
        return false;

      BlockchainBToken.TXBToken spend = spends[0];

      NetworkTestbench network = StartNodeWithTwoPeers(blocks, out SocketFake socketPeer, out SocketFake socketOther);

      try
      {
        if (!TryHandshakeTwoPeers(NAME_CASE, network, socketPeer, socketOther, ref message))
          return false;

        network.FillThrottleTXAnnounced(IP_PEER_OTHER, spend.TXRaw.Length - COUNT_BYTES_MISSING);

        socketPeer.SendMessage(COMMAND_TX, spend.TXRaw.Length, spend.TXRaw).Wait();

        if (!IsNoTXAnnounced(socketOther))
        {
          message = $"{NAME_CASE}: the node announces the spend before ThrottleTXAnnouncedToPeer drained enough room.";
          return false;
        }

        return IsTXAnnounced(NAME_CASE, socketOther, spend.Hash, ref message, SECONDS_TIMEOUT_DRAIN);
      }
      finally
      {
        socketPeer.Dispose();
        socketOther.Dispose();
      }
    }

    static bool TestOwnTX(Wallet wallet, ref string message)
    {
      Console.WriteLine("  28. Own TX: Announce an own unconfirmed TX once to each peer.");

      const string NAME_CASE = "Own TX";

      if (!BlockchainNetwork.TryCreateBlocks(wallet, [t => { }], out List<Block> blocks, out List<BlockchainBToken.TXBToken> spends, ref message))
        return false;

      BlockchainBToken.TXBToken spend = spends[0];

      NetworkTestbench network = StartNodeWithTwoPeers(blocks, out SocketFake socketPeer, out SocketFake socketOther);

      try
      {
        if (!TryHandshakeTwoPeers(NAME_CASE, network, socketPeer, socketOther, ref message))
          return false;

        network.Blockchain.RunLocked(() => ((BlockchainBToken)network.Blockchain).SubmitTX(spend.TXRaw)).Wait();

        foreach (SocketFake socket in new[] { socketPeer, socketOther })
          if (!IsTXAnnounced(NAME_CASE, socket, spend.Hash, ref message))
            return false;

        if (IsNoTXAnnounced(socketPeer) && IsNoTXAnnounced(socketOther))
          return true;

        message = $"{NAME_CASE}: the node announces the own TX a second time.";
        return false;
      }
      finally
      {
        socketPeer.Dispose();
        socketOther.Dispose();
      }
    }

    static bool IsNoTXAnnounced(SocketFake socketPeer)
    {
      const int SECONDS_HOLD = 3;

      string messageHold = "";

      return !TryReceiveInventories("", socketPeer, COMMAND_INV, MSG_TX, out _, ref messageHold, SECONDS_HOLD);
    }

    static NetworkTestbench StartNodeWithTwoPeers(List<Block> blocks, out SocketFake socketPeer, out SocketFake socketOther)
    {
      NetworkTestbench network = StartNodeWithInboundPeer(out socketPeer, out CommunicationFake communication, b => b.AnchorAndDeliver(blocks));
      socketOther = communication.ConnectInbound(IP_PEER_OTHER);

      return network;
    }

    static bool TryHandshakeTwoPeers(string nameCase, NetworkTestbench network, SocketFake socketPeer, SocketFake socketOther, ref string message)
    {
      return TryHandshakeInbound(nameCase, socketPeer, ref message)
        && TryHandshakeInbound(nameCase, socketOther, ref message)
        && IsPeerListed(nameCase, network, IP_PEER, ref message)
        && IsPeerListed(nameCase, network, IP_PEER_OTHER, ref message);
    }

    static bool IsTXAnnounced(string nameCase, SocketFake socketPeer, byte[] hash, ref string message, int secondsTimeout = SECONDS_TIMEOUT_REQUEST)
    {
      if (!TryReceiveInventories(nameCase, socketPeer, COMMAND_INV, MSG_TX, out List<byte[]> hashes, ref message, secondsTimeout))
        return false;

      if (hashes.Any(h => h.IsAllBytesEqual(hash)))
        return true;

      message = $"{nameCase}: the node announces the TXs {string.Join(", ", hashes.Select(h => h.ToHexString()))}, expected {hash.ToHexString()}.";
      return false;
    }

    static bool TestTXRequest(Wallet wallet, ref string message)
    {
      Console.WriteLine("  22. TX Request: Request only the TXs missing from the TX pool.");

      const string NAME_CASE = "TX request";

      if (!BlockchainNetwork.TryCreateBlocks(wallet, [t => { }, t => t.Nonce += 1], out List<Block> blocks, out List<BlockchainBToken.TXBToken> spends, ref message))
        return false;

      StartNodeWithInboundPeer(out SocketFake socketPeer, out _, b => b.AnchorAndDeliver(blocks));

      try
      {
        if (!TryHandshakeInbound(NAME_CASE, socketPeer, ref message)
          || !IsOnlyTXRequested(NAME_CASE, socketPeer, [spends[0].Hash], spends[0].Hash, ref message))
          return false;

        socketPeer.SendMessage(COMMAND_TX, spends[0].TXRaw.Length, spends[0].TXRaw).Wait();

        return IsOnlyTXRequested(NAME_CASE, socketPeer, [spends[0].Hash, spends[1].Hash], spends[1].Hash, ref message);
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool IsOnlyTXRequested(string nameCase, SocketFake socketPeer, byte[][] hashesAnnounced, byte[] hashExpected, ref string message)
    {
      SendInventory(socketPeer, MSG_TX, COMMAND_INV, hashesAnnounced);

      if (!TryReceiveInventories(nameCase, socketPeer, COMMAND_GETDATA, MSG_TX, out List<byte[]> hashes, ref message))
        return false;

      if (hashes.Count == 1 && hashes[0].IsAllBytesEqual(hashExpected))
        return true;

      message = $"{nameCase}: the node requests the TXs {string.Join(", ", hashes.Select(h => h.ToHexString()))}, expected only {hashExpected.ToHexString()}.";
      return false;
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

    static bool TestAddressPort(ref string message)
    {
      Console.WriteLine("  9. Address Port: Store only learned addresses with the network's port.");

      const string NAME_CASE = "Address port";
      const string IP_PORT_NETWORK = "10.0.2.1";
      const string IP_PORT_OTHER = "10.0.2.2";
      const int PORT_OTHER = 1234;

      NetworkTestbench network = StartNodeWithSeedPeer(1, out CommunicationFake communication, out SocketFake socketSeed);

      try
      {
        if (!TryHandshakeOutbound(NAME_CASE, socketSeed, ref message))
          return false;

        SendAddr(socketSeed, [(IP_PORT_NETWORK, network.ConfigurationNetwork.Port), (IP_PORT_OTHER, PORT_OTHER)]);

        if (!TrySynchronize(NAME_CASE, socketSeed, ref message)
          || !TryReadAddressList(NAME_CASE, communication, out List<string> iPs, ref message))
          return false;

        if (iPs.Contains(IP_PORT_NETWORK) && !iPs.Contains(IP_PORT_OTHER))
          return true;

        message = $"{NAME_CASE}: the address list {string.Join(", ", iPs)} should contain {IP_PORT_NETWORK} but not {IP_PORT_OTHER}.";
        return false;
      }
      finally
      {
        socketSeed.Dispose();
      }
    }

    static bool TestAddressDOS(ref string message)
    {
      Console.WriteLine("  10. Address DOS: Ignore addr messages beyond the address DOSMonitor.");

      const string NAME_CASE = "Address DOS";
      const string IP_ACCEPTED = "10.0.4.1";
      const string IP_BEYOND_LIMIT = "10.0.4.2";
      const string IP_UNASKED_1 = "10.0.4.3";
      const string IP_UNASKED_2 = "10.0.4.4";
      const int COUNT_ADDRESSES_MAX = 1000;
      const int PORT_OTHER = 1234;

      NetworkTestbench network = StartNodeWithSeedPeer(1, out CommunicationFake communication, out SocketFake socketSeed);
      SocketFake socketPeer = null;
      int port = network.ConfigurationNetwork.Port;

      try
      {
        if (!TryHandshakeOutbound(NAME_CASE, socketSeed, ref message))
          return false;

        SendAddr(socketSeed, [
          (IP_ACCEPTED, port),
          .. Enumerable.Range(0, COUNT_ADDRESSES_MAX - 1).Select(i => ($"10.1.{i / 256}.{i % 256}", PORT_OTHER))]);

        SendAddr(socketSeed, [(IP_BEYOND_LIMIT, port)]);

        socketPeer = communication.ConnectInbound(IP_PEER);

        if (!TryHandshakeInbound(NAME_CASE, socketPeer, ref message))
          return false;

        SendAddr(socketPeer, [(IP_UNASKED_1, port), (IP_UNASKED_2, port)]);

        if (!TrySynchronize(NAME_CASE, socketSeed, ref message)
          || !TrySynchronize(NAME_CASE, socketPeer, ref message)
          || !TryReadAddressList(NAME_CASE, communication, out List<string> iPs, ref message))
          return false;

        if (iPs.Contains(IP_ACCEPTED) && !iPs.Intersect([IP_BEYOND_LIMIT, IP_UNASKED_1, IP_UNASKED_2]).Any())
          return true;

        message = $"{NAME_CASE}: the address list {string.Join(", ", iPs)} should contain {IP_ACCEPTED} " +
          $"but none of {IP_BEYOND_LIMIT}, {IP_UNASKED_1} and {IP_UNASKED_2}.";
        return false;
      }
      finally
      {
        socketSeed.Dispose();
        socketPeer?.Dispose();
      }
    }

    static bool TestBlockDownload(Wallet wallet, ref string message)
    {
      Console.WriteLine("  11. Block Download: Download missing blocks, the newest only after its announcement.");

      const string NAME_CASE = "Block download";
      const int SECONDS_HOLD = 3;

      if (!BlockchainNetwork.TryCreateBlocks(wallet, [], out List<Block> blocks, out _, ref message))
        return false;

      Block blockNewest = blocks[^1];

      NetworkTestbench network = StartNodeWithInboundPeer(out SocketFake socketPeer, out _, b => b.ParentFake.MineAnchors(blocks));

      try
      {
        if (!TryHandshakeInbound(NAME_CASE, socketPeer, ref message)
          || !TryServeBlocksRequested(NAME_CASE, socketPeer, blocks.Take(blocks.Count - 1).ToList(), ref message))
          return false;

        string messageHold = "";

        if (TryReceiveRequest(NAME_CASE, socketPeer, MSG_BLOCK, out _, ref messageHold, SECONDS_HOLD))
        {
          message = $"{NAME_CASE}: the node requests the newest block before any peer announced it.";
          return false;
        }

        SendHeaders(socketPeer, blockNewest.Header);

        return TryServeBlocksRequested(NAME_CASE, socketPeer, [blockNewest], ref message)
          && IsAtHeight(NAME_CASE, network, blockNewest.Header.Height, ref message);
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool TestBlockRefusal(Wallet wallet, ref string message)
    {
      Console.WriteLine("  12. Block Refusal: Retry a refused block only after the refusal delay.");

      const string NAME_CASE = "Block refusal";
      const int SECONDS_DELAY_REFUSED = 10;
      const int SECONDS_TIMEOUT_RETRY = 20;

      if (!BlockchainNetwork.TryCreateBlocks(wallet, [], out List<Block> blocks, out _, ref message))
        return false;

      Block blockRefused = blocks[0];

      NetworkTestbench network = StartNodeWithInboundPeer(out SocketFake socketPeer, out _, b => b.ParentFake.MineAnchors(blocks));

      try
      {
        if (!TryHandshakeInbound(NAME_CASE, socketPeer, ref message)
          || !TryReceiveRequest(NAME_CASE, socketPeer, MSG_BLOCK, out byte[] hash, ref message))
          return false;

        if (!hash.IsAllBytesEqual(blockRefused.Header.Hash))
        {
          message = $"{NAME_CASE}: the node first requests block {hash.ToHexString()}, expected the block at height {blockRefused.Header.Height}.";
          return false;
        }

        SendInventory(socketPeer, MSG_BLOCK, COMMAND_NOTFOUND, hash);
        Stopwatch stopwatch = Stopwatch.StartNew();

        if (!TryServeBlocksRequested(NAME_CASE, socketPeer, blocks.Skip(1).Take(blocks.Count - 2).ToList(), ref message)
          || !TryReceiveRequest(NAME_CASE, socketPeer, MSG_BLOCK, out hash, ref message, SECONDS_TIMEOUT_RETRY))
          return false;

        if (!hash.IsAllBytesEqual(blockRefused.Header.Hash) || stopwatch.Elapsed < TimeSpan.FromSeconds(SECONDS_DELAY_REFUSED - 1))
        {
          message = $"{NAME_CASE}: after {stopwatch.Elapsed.TotalSeconds:F0} seconds the node requests block {hash.ToHexString()}, " +
            $"expected the refused block after {SECONDS_DELAY_REFUSED} seconds.";
          return false;
        }

        SendBlock(socketPeer, blockRefused);

        return IsAtHeight(NAME_CASE, network, blocks[^2].Header.Height, ref message);
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool TestUnrequestedBlock(ref string message)
    {
      Console.WriteLine("  13. Unrequested Block: Disconnect a peer that sends a block unasked.");

      return IsDisconnectedOnUnasked("Unrequested block", COMMAND_BLOCK, ref message);
    }

    static bool TestUnrequestedSegment(ref string message)
    {
      Console.WriteLine("  19. Unrequested Segment: Disconnect a peer that sends a segment unasked.");

      return IsDisconnectedOnUnasked("Unrequested segment", COMMAND_SEGMENT, ref message);
    }

    static bool TestSegmentRequest(List<Block> blocksWindow, ref string message)
    {
      Console.WriteLine("  20. Segment Request: Request segments without accounts, accept a matching one.");

      const string NAME_CASE = "Segment request";
      const int COUNT_SEGMENTS_RECORDED = 2;

      List<(byte[] hash, byte[] segment)> segments = null;

      StartNodeWithInboundPeer(out SocketFake socketPeer, out _, b => segments = b.AnchorAndDeliverWithoutAccounts(blocksWindow, COUNT_SEGMENTS_RECORDED));

      try
      {
        if (!TryHandshakeInbound(NAME_CASE, socketPeer, ref message)
          || !IsSegmentRequested(NAME_CASE, socketPeer, segments[0].hash, ref message))
          return false;

        socketPeer.SendMessage(COMMAND_SEGMENT, segments[0].segment.Length, segments[0].segment).Wait();

        return IsSegmentRequested(NAME_CASE, socketPeer, segments[1].hash, ref message);
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool TestSegmentRefusal(List<Block> blocksWindow, ref string message)
    {
      Console.WriteLine("  21. Segment Refusal: Never ask a peer again for a segment it refused.");

      const string NAME_CASE = "Segment refusal";
      const int COUNT_REFUSALS = 4;

      StartNodeWithInboundPeer(out SocketFake socketPeer, out _, b => b.AnchorAndDeliverWithoutAccounts(blocksWindow, 0));

      try
      {
        if (!TryHandshakeInbound(NAME_CASE, socketPeer, ref message))
          return false;

        HashSet<byte[]> hashesRefused = new(new EqualityComparerByteArray());

        for (int i = 0; i < COUNT_REFUSALS; i++)
        {
          if (!TryReceiveRequest(NAME_CASE, socketPeer, MSG_SEGMENT_SWEEP, out byte[] hash, ref message))
            return false;

          if (!hashesRefused.Add(hash))
          {
            message = $"{NAME_CASE}: the node requests the refused segment of block {hash.ToHexString()} again.";
            return false;
          }

          SendInventory(socketPeer, MSG_SEGMENT_SWEEP, COMMAND_NOTFOUND, hash);
        }

        return true;
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool IsSegmentRequested(string nameCase, SocketFake socketPeer, byte[] hashExpected, ref string message)
    {
      if (!TryReceiveRequest(nameCase, socketPeer, MSG_SEGMENT_SWEEP, out byte[] hash, ref message))
        return false;

      if (hash.IsAllBytesEqual(hashExpected))
        return true;

      message = $"{nameCase}: the node requests the segment of block {hash.ToHexString()}, expected {hashExpected.ToHexString()}.";
      return false;
    }

    static bool IsDisconnectedOnUnasked(string nameCase, string command, ref string message)
    {
      StartNodeWithInboundPeer(out SocketFake socketPeer, out _);

      try
      {
        if (!TryHandshakeInbound(nameCase, socketPeer, ref message))
          return false;

        socketPeer.SendMessage(command, 1, [0x00]).Wait();

        return socketPeer.IsDisconnected(nameCase, ref message);
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool TestRequestTimeout(Wallet wallet, ref string message)
    {
      Console.WriteLine("  14. Request Timeout: Disconnect a peer that does not deliver a requested block.");

      const string NAME_CASE = "Request timeout";

      if (!BlockchainNetwork.TryCreateBlocks(wallet, [], out List<Block> blocks, out _, ref message))
        return false;

      NetworkTestbench network = StartNodeWithInboundPeer(out SocketFake socketPeer, out _, b => b.ParentFake.MineAnchors(blocks));

      try
      {
        if (!TryHandshakeInbound(NAME_CASE, socketPeer, ref message)
          || !IsPeerListed(NAME_CASE, network, IP_PEER, ref message)
          || !TryReceiveRequest(NAME_CASE, socketPeer, MSG_BLOCK, out _, ref message))
          return false;

        network.AgeBlockRequest(IP_PEER, Network.TIMEOUT_REQUEST_SECONDS);

        return socketPeer.IsDisconnected(NAME_CASE, ref message);
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool TestBlockServing(Wallet wallet, ref string message)
    {
      Console.WriteLine("  15. Block Serving: Serve a stored block, answer an unknown one with notfound.");

      const string NAME_CASE = "Block serving";

      if (!BlockchainNetwork.TryCreateBlocks(wallet, [], out List<Block> blocks, out _, ref message))
        return false;

      StartNodeWithInboundPeer(out SocketFake socketPeer, out _, b => b.AnchorAndDeliver(blocks));

      try
      {
        return TryHandshakeInbound(NAME_CASE, socketPeer, ref message)
          && TryReceiveBlockServed(NAME_CASE, socketPeer, blocks[2].Header.Hash, ref message)
          && IsAnsweredNotFound(NAME_CASE, socketPeer, MSG_BLOCK, ref message);
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool TestSegmentServing(Wallet wallet, ref string message)
    {
      Console.WriteLine("  18. Segment Serving: Serve the segment of a block, answer an unknown one with notfound.");

      const string NAME_CASE = "Segment serving";

      if (!BlockchainNetwork.TryCreateBlocks(wallet, [], out List<Block> blocks, out _, ref message))
        return false;

      HeaderBToken header = (HeaderBToken)blocks[2].Header;

      StartNodeWithInboundPeer(out SocketFake socketPeer, out _, b => b.AnchorAndDeliver(blocks));

      try
      {
        if (!TryHandshakeInbound(NAME_CASE, socketPeer, ref message))
          return false;

        SendInventory(socketPeer, MSG_SEGMENT_SWEEP, COMMAND_GETDATA, header.Hash);

        if (!socketPeer.TryReceive(NAME_CASE, COMMAND_SEGMENT, out byte[] segment, ref message))
          return false;

        if (!SHA256.HashData(segment).IsAllBytesEqual(header.HashAccounts))
        {
          message = $"{NAME_CASE}: the segment served for the block at height {header.Height} does not hash to its HashAccounts.";
          return false;
        }

        return IsAnsweredNotFound(NAME_CASE, socketPeer, MSG_SEGMENT_SWEEP, ref message);
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool IsAnsweredNotFound(string nameCase, SocketFake socketPeer, Network.Inventory.InventoryType type, ref string message)
    {
      byte[] hashUnknown = Enumerable.Repeat((byte)0xAB, LENGTH_INVENTORY - LENGTH_INVENTORY_TYPE).ToArray();

      SendInventory(socketPeer, type, COMMAND_GETDATA, hashUnknown);

      if (!socketPeer.TryReceive(nameCase, COMMAND_NOTFOUND, out byte[] payload, ref message))
        return false;

      byte[] payloadExpected = [.. VarInt.GetBytes(1), .. BitConverter.GetBytes((uint)type), .. hashUnknown];

      if (payload.IsAllBytesEqual(payloadExpected))
        return true;

      message = $"{nameCase}: the node answers an unknown {type} with notfound {payload.ToHexString()}, expected {payloadExpected.ToHexString()}.";
      return false;
    }

    static bool TestServingDOS(Wallet wallet, ref string message)
    {
      Console.WriteLine("  16. Serving DOS: Disconnect a peer that re-requests an old block too often.");

      const string NAME_CASE = "Serving DOS";
      const int MAX_LEVEL_DOS_GETDATA = 5;

      if (!BlockchainNetwork.TryCreateBlocks(wallet, [], out List<Block> blocks, out _, ref message))
        return false;

      byte[] hashOld = blocks[0].Header.Hash;

      StartNodeWithInboundPeer(out SocketFake socketPeer, out _, b => b.AnchorAndDeliver(blocks));

      try
      {
        if (!TryHandshakeInbound(NAME_CASE, socketPeer, ref message))
          return false;

        foreach (Block block in blocks)
          if (!TryReceiveBlockServed($"{NAME_CASE}, chain served forward", socketPeer, block.Header.Hash, ref message))
            return false;

        for (int i = 0; i < MAX_LEVEL_DOS_GETDATA; i++)
          if (!TryReceiveBlockServed($"{NAME_CASE}, old block requested again", socketPeer, hashOld, ref message))
            return false;

        SendInventory(socketPeer, MSG_BLOCK, COMMAND_GETDATA, hashOld);

        return socketPeer.IsDisconnected(NAME_CASE, ref message);
      }
      finally
      {
        socketPeer.Dispose();
      }
    }

    static bool TestBlockAnnouncement(Wallet wallet, ref string message)
    {
      Console.WriteLine("  17. Block Announcement: Announce the newest inserted block to every peer.");

      const string NAME_CASE = "Block announcement";

      if (!BlockchainNetwork.TryCreateBlocks(wallet, [], out List<Block> blocks, out _, ref message))
        return false;

      Block blockNewest = blocks[^1];

      NetworkTestbench network = StartNodeWithInboundPeer(out SocketFake socketPeer, out CommunicationFake communication, b => b.ParentFake.MineAnchors(blocks));
      SocketFake socketOther = null;

      try
      {
        if (!TryHandshakeInbound(NAME_CASE, socketPeer, ref message)
          || !TryServeBlocksRequested(NAME_CASE, socketPeer, blocks.Take(blocks.Count - 1).ToList(), ref message)
          || !IsAtHeight(NAME_CASE, network, blocks[^2].Header.Height, ref message))
          return false;

        socketOther = communication.ConnectInbound(IP_PEER_OTHER);

        if (!TryHandshakeInbound(NAME_CASE, socketOther, ref message)
          || !IsPeerListed(NAME_CASE, network, IP_PEER_OTHER, ref message))
          return false;

        SendHeaders(socketPeer, blockNewest.Header);

        return TryServeBlocksRequested(NAME_CASE, socketPeer, [blockNewest], ref message)
          && IsAnnounced(NAME_CASE, socketPeer, blockNewest.Header, ref message)
          && IsAnnounced(NAME_CASE, socketOther, blockNewest.Header, ref message);
      }
      finally
      {
        socketPeer.Dispose();
        socketOther?.Dispose();
      }
    }

    static bool IsAnnounced(string nameCase, SocketFake socketPeer, Header header, ref string message)
    {
      if (!socketPeer.TryReceive(nameCase, COMMAND_HEADERS, out byte[] payload, ref message))
        return false;

      byte[] payloadExpected = [.. VarInt.GetBytes(1), .. header.Serialize(), 0x00];

      if (payload.IsAllBytesEqual(payloadExpected))
        return true;

      message = $"{nameCase}: the node announces {payload.ToHexString()}, expected the newest header {payloadExpected.ToHexString()}.";
      return false;
    }

    static NetworkTestbench StartNodeWithInboundPeer(out SocketFake socketPeer, out CommunicationFake communication, Action<BlockchainNetwork> prepare = null)
    {
      communication = new();
      NetworkTestbench network = BlockchainNetwork.StartNode(communication, prepare: prepare);
      socketPeer = communication.ConnectInbound(IP_PEER);

      return network;
    }

    static bool TryReceiveBlockServed(string nameCase, SocketFake socketPeer, byte[] hash, ref string message)
    {
      SendInventory(socketPeer, MSG_BLOCK, COMMAND_GETDATA, hash);

      if (!socketPeer.TryReceive(nameCase, COMMAND_BLOCK, out byte[] payload, ref message))
        return false;

      byte[] hashServed = Crypto.ComputeHash256(payload.AsSpan(0, HeaderBToken.COUNT_HEADER_BYTES));

      if (hashServed.IsAllBytesEqual(hash))
        return true;

      message = $"{nameCase}: the node serves block {hashServed.ToHexString()}, expected {hash.ToHexString()}.";
      return false;
    }

    static void SendInventory(SocketFake socketPeer, Network.Inventory.InventoryType type, string command, params byte[][] hashes)
    {
      byte[] payload = [.. VarInt.GetBytes(hashes.Length), .. hashes.SelectMany(h => (byte[])[.. BitConverter.GetBytes((uint)type), .. h])];

      socketPeer.SendMessage(command, payload.Length, payload).Wait();
    }

    static bool TryServeBlocksRequested(string nameCase, SocketFake socketPeer, List<Block> blocksServed, ref string message)
    {
      for (int i = 0; i < blocksServed.Count; i++)
      {
        if (!TryReceiveRequest(nameCase, socketPeer, MSG_BLOCK, out byte[] hash, ref message))
          return false;

        Block block = blocksServed.Find(b => b.Header.Hash.IsAllBytesEqual(hash));

        if (block == null)
        {
          message = $"{nameCase}: the node requests block {hash.ToHexString()}, expected one of the {blocksServed.Count} blocks served.";
          return false;
        }

        SendBlock(socketPeer, block);
      }

      return true;
    }

    static void SendBlock(SocketFake socketPeer, Block block)
    {
      socketPeer.SendMessage(COMMAND_BLOCK, block.LengthDataPayload, block.Buffer).Wait();
    }

    static bool TryReceiveRequest(string nameCase, SocketFake socketPeer, Network.Inventory.InventoryType type, out byte[] hash, ref string message, int secondsTimeout = SECONDS_TIMEOUT_REQUEST)
    {
      bool isReceived = TryReceiveInventories(nameCase, socketPeer, COMMAND_GETDATA, type, out List<byte[]> hashes, ref message, secondsTimeout);

      hash = hashes?[0];
      return isReceived;
    }

    static bool TryReceiveInventories(string nameCase, SocketFake socketPeer, string command, Network.Inventory.InventoryType type, out List<byte[]> hashes, ref string message, int secondsTimeout = SECONDS_TIMEOUT_REQUEST)
    {
      while (socketPeer.TryReceive(nameCase, command, out byte[] payload, ref message, secondsTimeout))
      {
        int startIndex = 0;
        int countInventories = VarInt.GetInt(payload, ref startIndex);

        hashes = [];

        for (int i = 0; i < countInventories; i++, startIndex += LENGTH_INVENTORY)
          if (BitConverter.ToUInt32(payload, startIndex) == (uint)type)
            hashes.Add(payload[(startIndex + LENGTH_INVENTORY_TYPE)..(startIndex + LENGTH_INVENTORY)]);

        if (hashes.Count > 0)
          return true;
      }

      hashes = null;
      return false;
    }

    static void SendHeaders(SocketFake socketPeer, params Header[] headers)
    {
      byte[] payload = [.. VarInt.GetBytes(headers.Length), .. headers.SelectMany(h => (byte[])[.. h.Serialize(), 0x00])];

      socketPeer.SendMessage(COMMAND_HEADERS, payload.Length, payload).Wait();
    }

    static bool IsAtHeight(string nameCase, NetworkTestbench network, int height, ref string message, int secondsTimeout = SECONDS_TIMEOUT_REQUEST)
    {
      if (SpinWait.SpinUntil(
        () => network.Blockchain.RunLocked(network.Blockchain.GetHeight).Result == height,
        TimeSpan.FromSeconds(secondsTimeout)))
        return true;

      message = $"{nameCase}: the node does not reach height {height}.";
      return false;
    }

    static bool TrySynchronize(string nameCase, SocketFake socketPeer, ref string message)
    {
      socketPeer.SendMessage(COMMAND_PING, LENGTH_NONCE_PING, new byte[LENGTH_NONCE_PING]).Wait();

      return socketPeer.TryReceive(nameCase, COMMAND_PONG, out _, ref message);
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

      internal void AgeBlockRequest(string iP, int seconds)
      {
        lock (LOCK_Peers)
          ((BlockMessage)Peers.Single(p => p.GetIP() == iP).MessagesByCommand[BlockMessage.Command]).TimeRequest -= TimeSpan.FromSeconds(seconds);
      }

      internal void FillThrottleTXAnnounced(string iP, int countBytesRoomLeft)
      {
        lock (LOCK_Peers)
          ((PeerBToken)Peers.Single(p => p.GetIP() == iP)).ThrottleTXAnnouncedToPeer.Increment(TXMessage.MAX_LEVEL_ANNOUNCED - countBytesRoomLeft);
      }

      internal void AgeTXRequests(string iP, int seconds)
      {
        lock (LOCK_Peers)
        {
          Dictionary<byte[], DateTime> hashesTXRequested = ((PeerBToken)Peers.Single(p => p.GetIP() == iP)).HashesTXRequested;

          lock (hashesTXRequested)
            foreach (byte[] hash in hashesTXRequested.Keys.ToList())
              hashesTXRequested[hash] -= TimeSpan.FromSeconds(seconds);
        }
      }
    }

    class BlockchainNetwork : BlockchainTestbench
    {
      const string NAME_TOKEN = "TestNetwork";

      internal BlockchainParentFake ParentFake;


      BlockchainNetwork(LiteDatabase liteDatabase, BlockchainParentFake blockchainParent)
        : base(NAME_TOKEN, liteDatabase, blockchainParent)
      {
        ParentFake = blockchainParent;
      }

      internal static bool TryCreateBlocks(Wallet wallet, List<Action<TXBToken>> altersSpend, out List<Block> blocks, out List<TXBToken> spends, ref string message, int heightTip = 0)
      {
        blocks = null;
        spends = null;

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message)
          || !TryInsertCoinbasesUpTo(ledger, wallet, CreateIDAccount(1000, 1), heightTip, ref message))
          return false;

        blocks = ledger.GetBlocksInserted();
        spends = altersSpend.Select(a => ledger.CreateTXSpend(wallet, CreateIDAccount(1001, 2), VALUE_SPEND, a)).ToList();
        return true;
      }

      internal void AnchorAndDeliver(List<Block> blocks)
      {
        ParentFake.MineAnchors(blocks);
        DeliverBlocks(blocks);
      }

      internal List<(byte[] hash, byte[] segment)> AnchorAndDeliverWithoutAccounts(List<Block> blocks, int countSegmentsRecorded)
      {
        AnchorAndDeliver(blocks);

        List<(byte[] hash, byte[] segment)> segments = [];

        for (
          Header header = ChainRoot.HeaderTipBlockchain.GetHeaderAncestor(GetHeight() - BITCOIN_REORG_MARGIN);
          segments.Count < countSegmentsRecorded;
          header = header.HeaderPrevious)
          segments.Add((header.Hash, GetSegmentSweptByBlock(header.Hash)));

        Synchronizer.Drop();

        return segments;
      }

      internal static NetworkTestbench StartNode(
        CommunicationFake communication,
        int countMaxOutbound = 1,
        int countMaxInbound = 32,
        Action<BlockchainNetwork> prepare = null,
        Action<BlockchainParentFake> prepareParent = null)
      {
        CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);
        CreateWalletFile(NAME_TOKEN);

        BlockchainParentFake blockchainParent = new(CreateLiteDatabase());
        prepareParent?.Invoke(blockchainParent);

        BlockchainNetwork blockchain = new(CreateLiteDatabase(), blockchainParent.Restart());
        blockchain.Load();
        prepare?.Invoke(blockchain);

        NetworkTestbench network = new(blockchain, communication);
        network.ConfigurationNetwork.CountMaxOutboundConnections = countMaxOutbound;
        network.ConfigurationNetwork.CountMaxInboundConnections = countMaxInbound;
        network.Start();

        return network;
      }

      internal bool IsAccountsSynchronized(string nameCase, int secondsTimeout, ref string message)
      {
        if (!SpinWait.SpinUntil(() => RunLocked(Synchronizer.IsAccountsAtTip).Result, TimeSpan.FromSeconds(secondsTimeout)))
        {
          message = $"{nameCase}: the accounts do not reach the tip within {secondsTimeout} seconds.";
          return false;
        }

        string messageSegments = "";

        if (RunLocked(() => AreSegmentsMatchingHashAccounts(nameCase, ref messageSegments)).Result)
          return true;

        message = messageSegments;
        return false;
      }

      internal static SocketFake StartNodeBitcoin(int countBlocks, out List<Header> headers)
      {
        CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);

        BlockchainParentFake blockchain = new(CreateLiteDatabase());
        blockchain.LoadBlockchain();

        headers = Enumerable.Range(0, countBlocks).Select(i => blockchain.MineBlock([])).ToList();

        CommunicationFake communication = new();
        SocketFake socketSeed = communication.QueueOutbound();

        NetworkBitcoin network = new(blockchain, communication);
        network.ConfigurationNetwork.SeedAddresses = [IP_SEED_BITCOIN];
        network.ConfigurationNetwork.CountMaxOutboundConnections = 1;
        network.Start();

        return socketSeed;
      }
    }
  }
}
