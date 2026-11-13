using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

using BTokenCore;

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  partial class TestConsensusRules : Test_Testbench
  {
    internal TestConsensusRules(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      return Test_BlockRules(Testbench.Wallet, ref message)
        && Test_TXRules(Testbench.Wallet, ref message)
        && Test_HeaderBitcoinRules(ref message)
        && BlockchainAccounts.TestSpendingRules(Testbench.Wallet, ref message)
        && BlockchainAccounts.TestCoinbaseRules(Testbench.Wallet, ref message)
        && BlockchainAccounts.TestBlockLimits(Testbench.Wallet, ref message)
        && BlockchainChain.TestForkPermutations(ref message)
        && BlockchainChain.TestAlternatingSwitches(ref message)
        && BlockchainChain.TestKnownHeaders(ref message)
        && BlockchainChain.TestHeaderPlaceholders(ref message)
        && BlockchainChain.TestForkPruning(ref message)
        && Test_BlockSize(ref message)
        && BlockchainAccounts.TestSingleRollbacks(Testbench.Wallet, ref message)
        && BlockchainAccounts.TestCountRestoration(Testbench.Wallet, ref message)
        && BlockchainAccounts.TestForkReplacement(Testbench.Wallet, ref message)
        && BlockchainAccounts.TestRetentionWindow(Testbench.Wallet, ref message)
        && BlockchainAccounts.TestServedSegments(Testbench.Wallet, ref message)
        && BlockchainAccounts.TestMalformedSegments(ref message)
        && BlockchainAccounts.TestInstalledAccounts(Testbench.Wallet, ref message);
    }

    static readonly byte[] MAGIC_BYTES = [0xf9, 0xbe, 0xb4, 0xd9];

    // Counter-tested 2026-11-13 14:03
    bool Test_BlockSize(ref string message)
    {
      Console.WriteLine("  12. Block Size: Receive SizeBlockMax payload, reject longer and negative lengths.");

      byte[] payload = new byte[SIZE_BLOCK_MAX + 1];
      Random.Shared.NextBytes(payload);

      byte[] headerNegativeLength = [
        .. MAGIC_BYTES,
        .. Encoding.ASCII.GetBytes("block".PadRight(SocketTCP.SIZE_COMMAND, '\0')),
        .. BitConverter.GetBytes(-1),
        .. new byte[SocketTCP.SIZE_CHECKSUM]];

      string error = ReceiveBlockPayload("payload of SizeBlockMax bytes", (s, c) => s.SendMessage("block", SIZE_BLOCK_MAX, payload), payload[..SIZE_BLOCK_MAX]).GetAwaiter().GetResult();

      if (error == "")
        error = ReceiveBlockPayload("payload of SizeBlockMax plus one bytes", (s, c) => s.SendMessage("block", SIZE_BLOCK_MAX + 1, payload), null).GetAwaiter().GetResult();

      if (error == "")
        error = ReceiveBlockPayload("payload of negative length", (s, c) => c.GetStream().WriteAsync(headerNegativeLength).AsTask(), null).GetAwaiter().GetResult();

      if (error == "")
        return true;

      message = $"Block size, {error}";
      return false;
    }

    static async Task<string> ReceiveBlockPayload(string nameCase, Func<SocketTCP, TcpClient, Task> send, byte[] payloadExpected)
    {
      TcpListener listener = new(IPAddress.Loopback, 0);
      listener.Start();

      TcpClient clientSending = new();
      SocketTCP socketReceiving = null;

      try
      {
        Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
        await clientSending.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);

        socketReceiving = new SocketTCP(await accepting);
        await socketReceiving.Start(MAGIC_BYTES);

        SocketTCP socketSending = new(clientSending);
        await socketSending.Start(MAGIC_BYTES);

        Task sending = send(socketSending, clientSending);

        byte[] buffer = new byte[SIZE_BLOCK_MAX];
        string result;

        try
        {
          string command = await socketReceiving.ReceiveCommandMessageNext();
          int lengthReceived = await socketReceiving.ReceivePayloadNext(buffer);

          if (payloadExpected == null)
            result = $"{nameCase}: accepted a payload of {lengthReceived} bytes.";
          else if (command != "block" || lengthReceived != payloadExpected.Length || !buffer.AsSpan(0, lengthReceived).SequenceEqual(payloadExpected))
            result = $"{nameCase}: received command {command} with {lengthReceived} bytes that differ from the sent payload.";
          else
            result = "";
        }
        catch (ProtocolException exception)
        {
          result = payloadExpected == null ? "" : $"{nameCase}: rejected the valid payload: {exception.Message}";
        }
        catch (Exception exception)
        {
          result = $"{nameCase}: threw {exception.GetType().Name}: {exception.Message}";
        }

        socketReceiving.Dispose();
        clientSending.Dispose();

        try
        {
          await sending;
        }
        catch (Exception)
        { }

        return result;
      }
      finally
      {
        socketReceiving?.Dispose();
        clientSending.Dispose();
        listener.Stop();
      }
    }

    abstract partial class BlockchainAccounts : BlockchainTestbench
    {
      internal static bool TestSpendingRules(Wallet wallet, ref string message)
      {
        Console.WriteLine("  4. Spending Rules: Reject wrong Nonce, low Balance, unknown accounts, replays, wrong HashAccounts.");

        Ledger ledger = new(heightRoot: 0);

        int indexSegmentWallet = GetIndexSegment(wallet.Hash160PKeyPublic);
        byte[] iDMiner = CreateIDAccount((indexSegmentWallet + 100) % COUNT_SEGMENTS, 1);
        byte[] iDReceiver = CreateIDAccount((indexSegmentWallet + 200) % COUNT_SEGMENTS, 2);
        byte[] iDUnknown = CreateIDAccount((indexSegmentWallet + 300) % COUNT_SEGMENTS, 3);

        if (!ledger.TryInsertBlock("coinbase creating the wallet account", [ledger.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, 0)], isValidExpected: true, null, ref message))
          return false;

        long balanceWallet = ledger.GetBalanceNext(wallet.Hash160PKeyPublic);
        TXBToken tXSpend = ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => { });

        if (!ledger.TryInsertBlock("spend with Nonce one higher", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => t.Nonce += 1)], isValidExpected: false, null, ref message)
          || !ledger.TryInsertBlock("spend with HeightAccountCreated one higher", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => t.HeightAccountCreated += 1)], isValidExpected: false, null, ref message)
          || !ledger.TryInsertBlock("spend of one more than the Balance", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, balanceWallet - FEE + 1, t => { })], isValidExpected: false, null, ref message)
          || !ledger.TryInsertBlock("spend from an unknown account", [ledger.CreateCoinbase(wallet, iDMiner, FEE), tXSpend], isValidExpected: false, b => ((TXBToken)b.TXs[1]).IDAccountSource = iDUnknown, ref message)
          || !ledger.TryInsertBlock("block with wrong HashAccounts", [ledger.CreateCoinbase(wallet, iDMiner, FEE), tXSpend], isValidExpected: false, b => ((HeaderBToken)b.Header).HashAccounts = SHA256.HashData(((HeaderBToken)b.Header).HashAccounts), ref message)
          || !ledger.TryInsertBlock("valid spend", [ledger.CreateCoinbase(wallet, iDMiner, FEE), tXSpend], isValidExpected: true, null, ref message)
          || !ledger.TryInsertBlock("TX paying only a Fee", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => t.TXOutputs.Clear())], isValidExpected: true, null, ref message)
          || !ledger.TryInsertBlock("replayed spend", [ledger.CreateCoinbase(wallet, iDMiner, FEE), tXSpend], isValidExpected: false, null, ref message))
          return false;

        TXBToken tXSpendBalance = ledger.CreateTXSpend(wallet, iDReceiver, ledger.GetBalanceNext(wallet.Hash160PKeyPublic) - FEE, t => { });
        TXBToken tXSpendAfterEmptied = ledger.CreateTXSpend(wallet, iDReceiver, 0, t =>
        {
          t.Nonce += 1;
          t.Fee = 0;
        });

        return ledger.TryInsertBlock("spend of nothing from an account spent to zero in the same block", [ledger.CreateCoinbase(wallet, iDMiner, FEE), tXSpendBalance, tXSpendAfterEmptied], isValidExpected: false, null, ref message)
          && ledger.TryInsertBlock("spend of the whole Balance", [ledger.CreateCoinbase(wallet, iDMiner, FEE), tXSpendBalance], isValidExpected: true, null, ref message);
      }

      internal static bool TestCoinbaseRules(Wallet wallet, ref string message)
      {
        Console.WriteLine("  5. Coinbase Rules: Check coinbase against block reward, fees, rent and halving.");

        Ledger ledger = new(heightRoot: 0);

        int indexSegmentWallet = GetIndexSegment(wallet.Hash160PKeyPublic);
        int indexSegmentRent = indexSegmentWallet == 3 ? 4 : 3;
        byte[] iDMiner = CreateIDAccount(1000, 1);
        byte[] iDReceiver = CreateIDAccount(1001, 2);
        byte[] iDRentPaying = CreateIDAccount(indexSegmentRent, 3);
        byte[] iDRentSwept = CreateIDAccount(indexSegmentRent, 4);
        byte[] iDRentSweptExactly = CreateIDAccount(indexSegmentRent, 5);

        TXBToken tXCoinbaseRentAccounts = ledger.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, 0,
          new TXOutputP2PKH(100, iDRentPaying), new TXOutputP2PKH(20, iDRentSwept), new TXOutputP2PKH(RENT_SWEEP, iDRentSweptExactly));

        if (!ledger.TryInsertBlock("coinbase paying one more than the block reward", [ledger.CreateCoinbase(wallet, iDMiner, 1)], isValidExpected: false, null, ref message)
          || !ledger.TryInsertBlock("coinbase paying one less than the block reward", [ledger.CreateCoinbase(wallet, iDMiner, -1)], isValidExpected: false, null, ref message)
          || !ledger.TryInsertBlock("coinbase paying the block reward to three accounts", [tXCoinbaseRentAccounts], isValidExpected: true, null, ref message))
          return false;

        TXBToken tXSpend = ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => { });

        if (!ledger.TryInsertBlock("coinbase paying one more than block reward plus fees", [ledger.CreateCoinbase(wallet, iDMiner, FEE + 1), tXSpend], isValidExpected: false, null, ref message)
          || !ledger.TryInsertBlock("coinbase paying one less than block reward plus fees", [ledger.CreateCoinbase(wallet, iDMiner, FEE - 1), tXSpend], isValidExpected: false, null, ref message)
          || !ledger.TryInsertBlock("coinbase paying block reward plus fees", [ledger.CreateCoinbase(wallet, iDMiner, FEE), tXSpend], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledger, wallet, iDMiner, indexSegmentRent - 1, ref message))
          return false;

        long rent = ledger.GetRentNext();

        if (rent != 92)
        {
          message = $"Account rules, rent of the sweep of segment {indexSegmentRent}: the reference computes {rent}, expected 92.";
          return false;
        }

        if (!ledger.TryInsertBlock("coinbase without the rent", [ledger.CreateCoinbase(wallet, iDMiner, -rent)], isValidExpected: false, null, ref message)
          || !ledger.TryInsertBlock("coinbase with the rent of a sweep", [ledger.CreateCoinbase(wallet, iDMiner, 0)], isValidExpected: true, null, ref message))
          return false;

        Ledger ledgerHalving = new(heightRoot: PERIOD_HALVING - 1);
        Ledger ledgerHalvingLast = new(heightRoot: 64 * PERIOD_HALVING - 1);

        return ledgerHalving.TryInsertBlock("coinbase paying the block reward before the halving", [ledgerHalving.CreateCoinbase(wallet, iDMiner, BLOCK_REWARD_INITIAL / 2)], isValidExpected: false, null, ref message)
          && ledgerHalving.TryInsertBlock("coinbase paying the halved block reward", [ledgerHalving.CreateCoinbase(wallet, iDMiner, 0)], isValidExpected: true, null, ref message)
          && ledgerHalvingLast.TryInsertBlock("coinbase paying the initial block reward after 64 halvings", [ledgerHalvingLast.CreateCoinbase(wallet, iDMiner, BLOCK_REWARD_INITIAL)], isValidExpected: false, null, ref message)
          && ledgerHalvingLast.TryInsertBlock("coinbase paying no block reward after 64 halvings", [ledgerHalvingLast.CreateCoinbase(wallet, iDMiner, 0)], isValidExpected: true, null, ref message);
      }

      internal static bool TestBlockLimits(Wallet wallet, ref string message)
      {
        Console.WriteLine("  6. Block Limits: Reject wrong anchor LockTime and accounts above CountAccountsMax.");

        Ledger ledger = new(heightRoot: 0);

        byte[] iDMiner = CreateIDAccount(1000, 1);
        byte[] iDReceiver = CreateIDAccount(1001, 2);

        if (!ledger.TryInsertBlock("coinbase creating the wallet account", [ledger.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, 0)], isValidExpected: true, null, ref message))
          return false;

        int lockTimeValid = ledger.GetHeightTip();
        int indexSegmentAccounts = GetIndexSegment(wallet.Hash160PKeyPublic) == 1500 ? 1501 : 1500;

        return ledger.TryInsertBlock("anchor TX with LockTime below the height before the block", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => AddAnchor(t, lockTimeValid - 1))], isValidExpected: false, null, ref message)
          && ledger.TryInsertBlock("anchor TX with LockTime of the block height", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => AddAnchor(t, lockTimeValid + 1))], isValidExpected: false, null, ref message)
          && ledger.TryInsertBlock("anchor TX with LockTime of the height before the block", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => AddAnchor(t, lockTimeValid))], isValidExpected: true, null, ref message)
          && ledger.TryInsertBlock("coinbase creating 101 accounts in one segment", [CreateCoinbaseAccounts(ledger, wallet, indexSegmentAccounts, 101)], isValidExpected: false, null, ref message)
          && ledger.TryInsertBlock("coinbase creating 100 accounts in one segment", [CreateCoinbaseAccounts(ledger, wallet, indexSegmentAccounts, 100)], isValidExpected: true, null, ref message);
      }

      internal static bool TestSingleRollbacks(Wallet wallet, ref string message)
      {
        Console.WriteLine("  13. Single Rollbacks: Roll back to root comparing accounts, then reinsert blocks.");

        Ledger ledger = new(heightRoot: 0);

        int indexSegmentWallet = GetIndexSegment(wallet.Hash160PKeyPublic);
        int indexSegmentRent = indexSegmentWallet == 3 ? 4 : 3;
        byte[] iDMiner = CreateIDAccount(1000, 1);
        byte[] iDReceiver = CreateIDAccount(1001, 2);
        byte[] iDRentPaying = CreateIDAccount(indexSegmentRent, 3);
        byte[] iDRentSwept = CreateIDAccount(indexSegmentRent, 4);
        byte[] iDRentSweptExactly = CreateIDAccount(indexSegmentRent, 5);

        TXBToken tXCoinbaseRentAccounts = ledger.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, 0,
          new TXOutputP2PKH(100, iDRentPaying), new TXOutputP2PKH(20, iDRentSwept), new TXOutputP2PKH(RENT_SWEEP, iDRentSweptExactly));

        if (!ledger.TryInsertBlock("coinbase creating the wallet account and three accounts in a rent segment", [tXCoinbaseRentAccounts], isValidExpected: true, null, ref message)
          || !ledger.TryInsertBlock("spend and TX paying only a Fee", [
            ledger.CreateCoinbase(wallet, iDMiner, 2 * FEE),
            ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => { }),
            ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t =>
            {
              t.Nonce += 1;
              t.TXOutputs.Clear();
            })], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledger, wallet, iDMiner, indexSegmentRent - 1, ref message))
          return false;

        if (!ledger.TryInsertBlock("sweep recreating a swept account", [ledger.CreateCoinbase(wallet, iDMiner, 0, new TXOutputP2PKH(50, iDRentSwept))], isValidExpected: true, null, ref message)
          || !ledger.TryInsertBlock("output to and spend from the wallet account", [ledger.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, FEE), ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => { })], isValidExpected: true, null, ref message))
          return false;

        if (!ledger.TryInsertBlock("spend of the whole Balance", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, ledger.GetBalanceNext(wallet.Hash160PKeyPublic) - FEE, t => { })], isValidExpected: true, null, ref message))
          return false;

        return ledger.TryRollBackAndInsertAgain("single rollbacks", heightTarget: 0, ref message);
      }

      internal static bool TestRetentionWindow(Wallet wallet, ref string message)
      {
        Console.WriteLine("  16. Retention Window: Roll back all blocks with undo records, then reinsert.");

        const int COUNT_MAX_BLOCKS_STORED = COUNT_SEGMENTS + BITCOIN_REORG_MARGIN;

        Ledger ledger = new(heightRoot: 0);

        int indexSegmentRent = GetIndexSegment(wallet.Hash160PKeyPublic) == 3 ? 4 : 3;
        byte[] iDMiner = CreateIDAccount(1001, 1);
        byte[] iDReceiver = CreateIDAccount(1002, 2);

        TXBToken tXCoinbaseAccounts = ledger.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, 0,
          new TXOutputP2PKH(100, CreateIDAccount(indexSegmentRent, 3)),
          new TXOutputP2PKH(50, CreateIDAccount(indexSegmentRent, 4)),
          new TXOutputP2PKH(100, CreateIDAccount(1000, 5)));

        if (!ledger.TryInsertBlock("coinbase creating the wallet account and accounts in a segment swept twice", [tXCoinbaseAccounts], isValidExpected: true, null, ref message)
          || !ledger.TryInsertBlock("spend from the wallet account", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => { })], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledger, wallet, iDMiner, COUNT_MAX_BLOCKS_STORED - 1, ref message))
          return false;

        return ledger.TryInsertBlock("spend of the whole Balance", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, ledger.GetBalanceNext(wallet.Hash160PKeyPublic) - FEE, t => { })], isValidExpected: true, null, ref message)
          && ledger.TryRollBackAndInsertAgain("retention window", heightTarget: 0, ref message);
      }

      internal static bool TestCountRestoration(Wallet wallet, ref string message)
      {
        Console.WriteLine("  14. Count Restoration: Restore segment account counts when rolling back creations or sweeps.");

        Ledger ledger = new(heightRoot: 0);

        int indexSegmentAccounts = GetIndexSegment(wallet.Hash160PKeyPublic) == 4 ? 5 : 4;
        byte[] iDMiner = CreateIDAccount(1000, 1);

        if (!ledger.TryInsertBlock("coinbase creating the wallet account", [ledger.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, 0)], isValidExpected: true, null, ref message)
          || !ledger.TryInsertBlock("coinbase creating 100 accounts in one segment", [CreateCoinbaseAccounts(ledger, wallet, indexSegmentAccounts, 100)], isValidExpected: true, null, ref message)
          || !ledger.TryRollBack("count restoration", out _, ref message)
          || !ledger.TryInsertBlock("coinbase creating 101 accounts in one segment after rolling back the creation of 100", [CreateCoinbaseAccounts(ledger, wallet, indexSegmentAccounts, 101)], isValidExpected: false, null, ref message)
          || !ledger.TryInsertBlock("coinbase creating 100 accounts in one segment after rolling back their creation", [CreateCoinbaseAccounts(ledger, wallet, indexSegmentAccounts, 100)], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledger, wallet, iDMiner, indexSegmentAccounts - 1, ref message))
          return false;

        return ledger.TryInsertBlock("sweep deleting 99 accounts", [ledger.CreateCoinbase(wallet, iDMiner, 0)], isValidExpected: true, null, ref message)
          && ledger.TryRollBack("count restoration", out _, ref message)
          && ledger.TryRollBack("count restoration", out _, ref message)
          && ledger.TryInsertBlock("coinbase creating a 101st account in a segment whose sweep was rolled back", [CreateCoinbaseAccounts(ledger, wallet, indexSegmentAccounts, 101)], isValidExpected: false, null, ref message)
          && ledger.TryInsertBlock("coinbase paying the 100 accounts restored by rolling back their sweep", [CreateCoinbaseAccounts(ledger, wallet, indexSegmentAccounts, 100)], isValidExpected: true, null, ref message);
      }

      internal static bool TestForkReplacement(Wallet wallet, ref string message)
      {
        Console.WriteLine("  15. Fork Replacement: Replace two rolled-back blocks, rejecting the old fork's spend.");

        Ledger ledger = new(heightRoot: 0);

        byte[] iDMiner = CreateIDAccount(1000, 1);
        byte[] iDReceiver = CreateIDAccount(1001, 2);
        byte[] iDMinerFork = CreateIDAccount(1002, 3);
        byte[] iDReceiverFork = CreateIDAccount(1003, 4);

        if (!ledger.TryInsertBlock("coinbase creating the wallet account", [ledger.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, 0)], isValidExpected: true, null, ref message)
          || !ledger.TryInsertBlock("first spend of the replaced fork", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => { })], isValidExpected: true, null, ref message)
          || !ledger.TryInsertBlock("second spend of the replaced fork", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, 2 * VALUE_SPEND, t => { })], isValidExpected: true, null, ref message)
          || !ledger.TryRollBack("fork replacement", out List<TXBToken> tXsRolledBack, ref message)
          || !ledger.TryRollBack("fork replacement", out _, ref message))
          return false;

        return ledger.TryInsertBlock("spend with the Nonce of the rolled-back fork", [ledger.CreateCoinbase(wallet, iDMiner, FEE), tXsRolledBack[1]], isValidExpected: false, null, ref message)
          && ledger.TryInsertBlock("first spend of the replacing fork", [ledger.CreateCoinbase(wallet, iDMinerFork, FEE), ledger.CreateTXSpend(wallet, iDReceiverFork, 3 * VALUE_SPEND, t => { })], isValidExpected: true, null, ref message)
          && ledger.TryInsertBlock("second spend of the replacing fork", [
            ledger.CreateCoinbase(wallet, iDMinerFork, 2 * FEE),
            ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => { }),
            ledger.CreateTXSpend(wallet, iDReceiverFork, VALUE_SPEND, t => t.Nonce += 1)], isValidExpected: true, null, ref message);
      }
    }

    const uint UNIX_TIME_START = 1_600_000_000;
    const uint SECONDS_BLOCK = 600;
    const uint SECONDS_TWO_WEEKS = 14 * 24 * 60 * 60;
    const uint NBITS_MAINNET = 0x17096a20;
    const uint NBITS_DIFFICULTY_1 = 0x1d00ffff;
    const int COUNT_HEADERS_RETARGET = 2016;
    const uint SECONDS_HOUR = 60 * 60;
    const uint NBITS_EASY = 0x1f00ffff;
    const int INDEX_UNIX_TIME = 68;
    const int INDEX_NBITS = 72;
    const int INDEX_NONCE = 76;

    // Counter-tested 2026-11-13 14:03
    bool Test_HeaderBitcoinRules(ref string message)
    {
      Console.WriteLine("  3. HeaderBitcoin Rules: Reject bad timestamp, proof of work, HashPrevious or NBits.");

      uint unixTimeNow = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

      byte[] headerRawMeetingTarget = MineHeaderBitcoin(unixTimeNow - SECONDS_HOUR, (hash, target) => hash <= target);
      byte[] headerRawAboveTarget = MineHeaderBitcoin(unixTimeNow - SECONDS_HOUR, (hash, target) => hash > target && hash <= 2 * target);
      byte[] headerRawAheadInsideLimit = MineHeaderBitcoin(unixTimeNow + 2 * SECONDS_HOUR - 60, (hash, target) => hash <= target);
      byte[] headerRawAheadBeyondLimit = MineHeaderBitcoin(unixTimeNow + 2 * SECONDS_HOUR + 60, (hash, target) => hash <= target);

      HeaderBitcoin headerTip;

      try
      {
        headerTip = CreateChainBitcoin(20, i => i == 8 ? UNIX_TIME_START + 30 * SECONDS_BLOCK : UNIX_TIME_START + (uint)i * SECONDS_BLOCK, NBITS_MAINNET);
      }
      catch (ProtocolException exception)
      {
        message = $"Chain of HeaderBitcoin for the median time past: rejected a valid header: {exception.Message}";
        return false;
      }

      uint unixTimeMedian = UNIX_TIME_START + 14 * SECONDS_BLOCK;

      return IsAccepted("HeaderBitcoin with Hash at most its target", () => ParseHeaderBitcoin(headerRawMeetingTarget), ref message)
        && IsRejected("HeaderBitcoin with Hash above its target", () => ParseHeaderBitcoin(headerRawAboveTarget), ref message)
        && IsAccepted("HeaderBitcoin with timestamp one minute inside two hours ahead", () => ParseHeaderBitcoin(headerRawAheadInsideLimit), ref message)
        && IsRejected("HeaderBitcoin with timestamp one minute beyond two hours ahead", () => ParseHeaderBitcoin(headerRawAheadBeyondLimit), ref message)
        && IsAccepted("HeaderBitcoin with timestamp at median time past", () => AppendHeaderBitcoin(headerTip, unixTimeMedian, NBITS_MAINNET), ref message)
        && IsRejected("HeaderBitcoin with wrong HashPrevious", () =>
          CreateHeaderBitcoin(SHA256.HashData(headerTip.Hash), headerTip.UnixTimeSeconds + SECONDS_BLOCK, NBITS_MAINNET).AppendToHeader(headerTip), ref message)
        && IsRejected("HeaderBitcoin with timestamp below median time past", () => AppendHeaderBitcoin(headerTip, unixTimeMedian - 1, NBITS_MAINNET), ref message)
        && IsRejected("HeaderBitcoin with NBits changed outside retarget", () => AppendHeaderBitcoin(headerTip, headerTip.UnixTimeSeconds + SECONDS_BLOCK, NBITS_MAINNET + 1), ref message)
        && IsRetargetCorrect(NBITS_MAINNET, SECONDS_TWO_WEEKS, ref message)
        && IsRetargetCorrect(NBITS_MAINNET, SECONDS_TWO_WEEKS / 2, ref message)
        && IsRetargetCorrect(NBITS_MAINNET, 5 * SECONDS_TWO_WEEKS, ref message)
        && IsRetargetCorrect(NBITS_MAINNET, SECONDS_TWO_WEEKS / 10, ref message)
        && IsRetargetCorrect(NBITS_DIFFICULTY_1, 2 * SECONDS_TWO_WEEKS, ref message);
    }

    static byte[] MineHeaderBitcoin(uint unixTimeSeconds, Func<BigInteger, BigInteger, bool> isHashWanted)
    {
      byte[] headerRaw = Convert.FromHexString(HEADER_BITCOIN_737857);
      BitConverter.GetBytes(unixTimeSeconds).CopyTo(headerRaw, INDEX_UNIX_TIME);
      BitConverter.GetBytes(NBITS_EASY).CopyTo(headerRaw, INDEX_NBITS);

      BigInteger target = DecodeCompact(NBITS_EASY);

      for (uint nonce = 0; ; nonce++)
      {
        BitConverter.GetBytes(nonce).CopyTo(headerRaw, INDEX_NONCE);

        if (isHashWanted(new BigInteger(Crypto.ComputeHash256(headerRaw), isUnsigned: true), target))
          return headerRaw;
      }
    }

    static void ParseHeaderBitcoin(byte[] headerRaw)
    {
      int index = 0;
      _ = new HeaderBitcoin(headerRaw, ref index);
    }

    static HeaderBitcoin CreateHeaderBitcoin(byte[] hashPrevious, uint unixTimeSeconds, uint nBits)
    {
      HeaderBitcoin header = new(
        headerHash: null,
        version: 0x20000000,
        hashPrevious: hashPrevious,
        merkleRootHash: new byte[32],
        unixTimeSeconds: unixTimeSeconds,
        nBits: nBits,
        nonce: 0);

      header.ComputeHash();

      return header;
    }

    static HeaderBitcoin AppendHeaderBitcoin(HeaderBitcoin headerPrevious, uint unixTimeSeconds, uint nBits)
    {
      HeaderBitcoin header = CreateHeaderBitcoin(headerPrevious.Hash, unixTimeSeconds, nBits);
      header.AppendToHeader(headerPrevious);

      return header;
    }

    static HeaderBitcoin CreateChainBitcoin(int countHeaders, Func<int, uint> getUnixTimeSeconds, uint nBits)
    {
      HeaderBitcoin header = CreateHeaderBitcoin(new byte[32], getUnixTimeSeconds(0), nBits);

      for (int i = 1; i < countHeaders; i++)
      {
        if (i % COUNT_HEADERS_RETARGET == 0)
          nBits = ComputeNBitsRetarget(nBits, header.UnixTimeSeconds - ((HeaderBitcoin)header.GetHeaderAncestor(i - COUNT_HEADERS_RETARGET)).UnixTimeSeconds);

        header = AppendHeaderBitcoin(header, getUnixTimeSeconds(i), nBits);
      }

      return header;
    }

    static bool IsRetargetCorrect(uint nBits, uint secondsTimespan, ref string message)
    {
      uint secondsBetweenHeaders = secondsTimespan / (COUNT_HEADERS_RETARGET - 1);
      uint unixTimeIntervalSecond = UNIX_TIME_START + COUNT_HEADERS_RETARGET * SECONDS_BLOCK;

      HeaderBitcoin headerTip;

      try
      {
        headerTip = CreateChainBitcoin(
          2 * COUNT_HEADERS_RETARGET,
          i => i < COUNT_HEADERS_RETARGET ? UNIX_TIME_START + (uint)i * SECONDS_BLOCK
            : i == 2 * COUNT_HEADERS_RETARGET - 1 ? unixTimeIntervalSecond + secondsTimespan
            : unixTimeIntervalSecond + (uint)(i - COUNT_HEADERS_RETARGET) * secondsBetweenHeaders,
          nBits);
      }
      catch (ProtocolException exception)
      {
        message = $"Chain of HeaderBitcoin for a retarget of NBits {nBits:x8} after a timespan of {secondsTimespan} seconds: rejected a valid header: {exception.Message}";
        return false;
      }

      uint nBitsExpected = ComputeNBitsRetarget(headerTip.NBits, secondsTimespan);
      string nameRule = $"Retarget of NBits {nBits:x8} after a timespan of {secondsTimespan} seconds to {nBitsExpected:x8}";

      try
      {
        AppendHeaderBitcoin(headerTip, headerTip.UnixTimeSeconds + SECONDS_BLOCK, nBitsExpected);
      }
      catch (ProtocolException exception)
      {
        message = $"{nameRule}: rejected the expected NBits: {exception.Message}";
        return false;
      }

      return IsRejected(nameRule, () => AppendHeaderBitcoin(headerTip, headerTip.UnixTimeSeconds + SECONDS_BLOCK, nBitsExpected + 1), ref message);
    }

    static uint ComputeNBitsRetarget(uint nBits, uint secondsTimespan)
    {
      uint secondsClamped = Math.Clamp(secondsTimespan, SECONDS_TWO_WEEKS / 4, SECONDS_TWO_WEEKS * 4);

      BigInteger target = DecodeCompact(nBits) * secondsClamped / SECONDS_TWO_WEEKS;

      return EncodeCompact(BigInteger.Min(target, DecodeCompact(NBITS_DIFFICULTY_1)));
    }

    static BigInteger DecodeCompact(uint compact)
    {
      int exponent = (int)(compact >> 24);
      BigInteger mantissa = compact & 0x007fffff;

      return exponent <= 3 ? mantissa >> 8 * (3 - exponent) : mantissa << 8 * (exponent - 3);
    }

    static uint EncodeCompact(BigInteger target)
    {
      int size = target.GetByteCount(isUnsigned: true);
      uint mantissa = (uint)(size <= 3 ? target << 8 * (3 - size) : target >> 8 * (size - 3));

      if ((mantissa & 0x00800000) != 0)
      {
        mantissa >>= 8;
        size += 1;
      }

      return mantissa | (uint)size << 24;
    }

    const long SUPPLY_MAX = 2 * 20_000_000_000 * 52_500;
    const long VALUE_OUTPUT = 50000;

    // Counter-tested 2026-11-13 14:03
    bool Test_TXRules(Wallet wallet, ref string message)
    {
      Console.WriteLine("  2. TX Rules: Reject invalid Fee, Value, script, signature, outputs; check IsLockTimeValid.");

      byte[] tXRawSignatureFlipped = CreateTXRawSigned(wallet, t => { });
      tXRawSignatureFlipped[^1] ^= 0x01;

      byte[] tXRawBodyChanged = CreateTXRawSigned(wallet, t => { });
      tXRawBodyChanged[LENGTH_PUBKEY + 2 * sizeof(int) + 1] ^= 0x01;

      byte[] tXRawCountOutputsTooLarge = [
        .. CreateTXRawSigned(wallet, t => { })[..(LENGTH_PUBKEY + 2 * sizeof(int) + sizeof(long))],
        0xff,
        .. BitConverter.GetBytes((ulong)int.MaxValue + 1)];

      return IsAccepted("TX with Fee of SUPPLY_MAX and no outputs", () => ParseTXRaw(CreateTXRawSigned(wallet, t =>
          {
            t.Fee = SUPPLY_MAX;
            t.TXOutputs.Clear();
          })), ref message)
        && IsAccepted("TX with Fee and Values summing to SUPPLY_MAX", () => ParseTXRaw(CreateTXRawSigned(wallet, t => t.Fee = SUPPLY_MAX - VALUE_OUTPUT)), ref message)
        && IsAccepted("TX with three Values and Fee summing to SUPPLY_MAX", () => ParseTXRaw(CreateTXRawSigned(wallet, t =>
          {
            t.Fee = 0;
            t.TXOutputs[0] = new TXOutputP2PKH(SUPPLY_MAX / 3, wallet.Hash160PKeyPublic);
            t.TXOutputs.Add(new TXOutputP2PKH(SUPPLY_MAX / 3, wallet.Hash160PKeyPublic));
            t.TXOutputs.Add(new TXOutputP2PKH(SUPPLY_MAX / 3, wallet.Hash160PKeyPublic));
          })), ref message)
        && IsRejected("TX with Fee minus one", () => ParseTXRaw(CreateTXRawSigned(wallet, t => t.Fee = -1)), ref message)
        && IsRejected("TX with Fee above SUPPLY_MAX", () => ParseTXRaw(CreateTXRawSigned(wallet, t =>
          {
            t.Fee = SUPPLY_MAX + 1;
            t.TXOutputs.Clear();
          })), ref message)
        && IsRejected("TX with Fee and Values above SUPPLY_MAX", () => ParseTXRaw(CreateTXRawSigned(wallet, t => t.Fee = SUPPLY_MAX - VALUE_OUTPUT + 1)), ref message)
        && IsRejected("TX with Value minus one", () => ParseTXRaw(CreateTXRawSigned(wallet, t => t.TXOutputs[0] = new TXOutputP2PKH(-1, wallet.Hash160PKeyPublic))), ref message)
        && IsRejected("TX with two Values above SUPPLY_MAX", () => ParseTXRaw(CreateTXRawSigned(wallet, t =>
          {
            t.TXOutputs[0] = new TXOutputP2PKH(SUPPLY_MAX / 2 + 1, wallet.Hash160PKeyPublic);
            t.TXOutputs.Add(new TXOutputP2PKH(SUPPLY_MAX / 2 + 1, wallet.Hash160PKeyPublic));
          })), ref message)
        && IsRejected("TX with three Values summing above SUPPLY_MAX", () => ParseTXRaw(CreateTXRawSigned(wallet, t =>
          {
            t.Fee = 0;
            t.TXOutputs[0] = new TXOutputP2PKH(SUPPLY_MAX / 3 + 1, wallet.Hash160PKeyPublic);
            t.TXOutputs.Add(new TXOutputP2PKH(SUPPLY_MAX / 3 + 1, wallet.Hash160PKeyPublic));
            t.TXOutputs.Add(new TXOutputP2PKH(SUPPLY_MAX / 3 + 1, wallet.Hash160PKeyPublic));
          })), ref message)
        && IsRejected("TX with unknown output script", () => ParseTXRaw(CreateTXRawSigned(wallet, t => t.TXOutputs.Add(new TXOutput { Script = [0x6a, 0x01, 0x02] }))), ref message)
        && IsRejected("TX with flipped signature byte", () => ParseTXRaw(tXRawSignatureFlipped), ref message)
        && IsRejected("TX with body changed after signing", () => ParseTXRaw(tXRawBodyChanged), ref message)
        && IsRejected("TX with count of outputs above int.MaxValue", () => ParseTXRaw(tXRawCountOutputsTooLarge), ref message)
        && IsLockTimeValidExpected(wallet, hasAnchor: true, offsetLockTime: -1, isValidExpected: true, ref message)
        && IsLockTimeValidExpected(wallet, hasAnchor: true, offsetLockTime: -2, isValidExpected: false, ref message)
        && IsLockTimeValidExpected(wallet, hasAnchor: true, offsetLockTime: 0, isValidExpected: false, ref message)
        && IsLockTimeValidExpected(wallet, hasAnchor: false, offsetLockTime: -2, isValidExpected: true, ref message)
        && IsLockTimeValidExpected(wallet, hasAnchor: false, offsetLockTime: 5, isValidExpected: true, ref message);
    }

    const int LENGTH_PUBKEY = 33;
    const int HEIGHT_BLOCK = 737858;

    static byte[] CreateTXRawSigned(Wallet wallet, Action<BlockchainBToken.TXBToken> alter)
    {
      BlockchainBToken.TXBToken tX = CreateTXBToken(wallet.KeyPublic, wallet.Hash160PKeyPublic);
      alter(tX);
      tX.Serialize(wallet);

      return tX.TXRaw;
    }

    static bool IsLockTimeValidExpected(Wallet wallet, bool hasAnchor, int offsetLockTime, bool isValidExpected, ref string message)
    {
      BlockchainBToken.TXBToken tX = hasAnchor
        ? CreateTXBToken(wallet.KeyPublic, wallet.Hash160PKeyPublic, CreateTXOutputTokenAnchor(ID_TOKEN, 0))
        : CreateTXBToken(wallet.KeyPublic, wallet.Hash160PKeyPublic);

      tX.LockTime = HEIGHT_BLOCK + offsetLockTime;

      return IsFieldEqual(
        $"IsLockTimeValid of TX {(hasAnchor ? "with" : "without")} anchor at LockTime {tX.LockTime} in block {HEIGHT_BLOCK}",
        "result",
        isValidExpected,
        tX.IsLockTimeValid(HEIGHT_BLOCK),
        ref message);
    }

    // Counter-tested 2026-11-13 14:03
    bool Test_BlockRules(Wallet wallet, ref string message)
    {
      Console.WriteLine("  1. Block Rules: Reject unexpected header, missing or repeated TXs, wrong MerkleRoot.");

      BlockchainBToken.TXBToken tXCoinbase = CreateTXBToken(wallet.KeyPublic, wallet.Hash160PKeyPublic);
      tXCoinbase.Nonce = 0;
      tXCoinbase.SerializeUnsigned();

      BlockchainBToken.TXBToken tXDuplicatingCoinbase = CreateTXBToken(wallet.KeyPublic, wallet.Hash160PKeyPublic);
      tXDuplicatingCoinbase.Nonce = 0;
      tXDuplicatingCoinbase.Serialize(wallet);

      BlockchainBToken.TXBToken tX = CreateTXBToken(wallet.KeyPublic, wallet.Hash160PKeyPublic);
      tX.Serialize(wallet);

      BlockchainBToken.TXBToken tXOther = CreateTXBToken(wallet.KeyPublic, wallet.Hash160PKeyPublic);
      tXOther.Nonce += 1;
      tXOther.Serialize(wallet);

      Block block = CreateBlock([tXCoinbase, tX]);
      block.Serialize();
      byte[] bufferBlock = block.Buffer[..block.LengthDataPayload];

      HeaderBToken headerHashOther = new()
      {
        Hash = SHA256.HashData(block.Header.Hash),
        HashPrevious = block.Header.HashPrevious,
        Height = block.Header.Height
      };

      HeaderBToken headerHashPreviousOther = new()
      {
        Hash = block.Header.Hash,
        HashPrevious = SHA256.HashData(block.Header.HashPrevious),
        Height = block.Header.Height
      };

      HeaderBToken headerExpected = new()
      {
        Hash = block.Header.Hash,
        HashPrevious = block.Header.HashPrevious,
        Height = block.Header.Height
      };

      Block blockMutated = CreateBlock([tXCoinbase, tX, tXOther, tXOther]);

      if (!blockMutated.Header.MerkleRoot.IsAllBytesEqual(CreateBlock([tXCoinbase, tX, tXOther]).Header.MerkleRoot))
      {
        message = "Block rules: the mutated block doesn't keep the merkle root of the block without its repeated TX.";
        return false;
      }

      Block blockMerkleRootWrong = CreateBlock([tXCoinbase, tX]);
      blockMerkleRootWrong.Header.MerkleRoot = SHA256.HashData(blockMerkleRootWrong.Header.MerkleRoot);
      blockMerkleRootWrong.Header.ComputeHash();

      return IsAccepted("Block of coinbase and one TX", () => ParseBlockBuffer(bufferBlock, headerExpected), ref message)
        && IsAccepted("Block of coinbase and two TXs", () => ParseBlockSerialized(CreateBlock([tXCoinbase, tX, tXOther])), ref message)
        && IsRejected("Block with unexpected Hash", () => ParseBlockBuffer(bufferBlock, headerHashOther), ref message)
        && IsRejected("Block with unexpected HashPrevious", () => ParseBlockBuffer(bufferBlock, headerHashPreviousOther), ref message)
        && IsRejected("Block without TXs", () => ParseBlockBuffer([.. block.Header.Serialize(), 0x00], headerExpected), ref message)
        && IsRejected("Block with a TX twice", () => ParseBlockSerialized(CreateBlock([tXCoinbase, tX, tX])), ref message)
        && IsRejected("Block with a TX duplicating the coinbase", () => ParseBlockSerialized(CreateBlock([tXCoinbase, tXDuplicatingCoinbase])), ref message)
        && IsRejected("Mutated block repeating its last TX", () => ParseBlockSerialized(blockMutated), ref message)
        && IsRejected("Block with wrong MerkleRoot", () => ParseBlockSerialized(blockMerkleRootWrong), ref message);
    }
  }
}
