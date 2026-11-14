using System.Security.Cryptography;

using BTokenCore;

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  partial class TestConsensusRules
  {
    abstract partial class BlockchainAccounts
    {
      // Counter-tested 2026-11-14 03:01
      internal static bool TestServedSegments(Wallet wallet, ref string message)
      {
        Console.WriteLine("  17. Served Segments: Serve each block's segment as it was after that block.");

        const int COUNT_MAX_BLOCKS_STORED = COUNT_SEGMENTS + BITCOIN_REORG_MARGIN;

        Ledger ledger = new(heightRoot: 0);

        int indexSegmentAccounts = GetIndexSegment(wallet.Hash160PKeyPublic) == 3 ? 4 : 3;
        byte[] iDMiner = CreateIDAccount(1001, 1);
        byte[] iDReceiver = CreateIDAccount(1002, 2);
        byte[] iDTouchedTwice = CreateIDAccount(indexSegmentAccounts, 3);
        byte[] iDSweptAndRecreated = CreateIDAccount(indexSegmentAccounts, 4);
        byte[] iDUntouched = CreateIDAccount(indexSegmentAccounts, 5);
        byte[] iDCreatedLater = CreateIDAccount(indexSegmentAccounts, 6);

        TXBToken tXCoinbaseAccounts = ledger.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, 0,
          new TXOutputP2PKH(100, iDTouchedTwice),
          new TXOutputP2PKH(50, iDSweptAndRecreated),
          new TXOutputP2PKH(1000, iDUntouched));

        if (!ledger.TryInsertBlock("coinbase creating the wallet account and accounts in a segment swept twice", [tXCoinbaseAccounts], isValidExpected: true, null, ref message)
          || !ledger.TryInsertBlock("spend from the wallet account", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => { })], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledger, wallet, iDMiner, indexSegmentAccounts, ref message)
          || !ledger.TryInsertBlock("first touch of an account and creation of another after the sweep", [ledger.CreateCoinbase(wallet, iDMiner, 0, new TXOutputP2PKH(10, iDTouchedTwice), new TXOutputP2PKH(20, iDCreatedLater))], isValidExpected: true, null, ref message)
          || !ledger.TryInsertBlock("second touch of an account after the sweep", [ledger.CreateCoinbase(wallet, iDMiner, 0, new TXOutputP2PKH(5, iDTouchedTwice))], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledger, wallet, iDMiner, indexSegmentAccounts + COUNT_SEGMENTS - 1, ref message)
          || !ledger.TryInsertBlock("second sweep recreating a swept account", [ledger.CreateCoinbase(wallet, iDMiner, 0, new TXOutputP2PKH(30, iDSweptAndRecreated))], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledger, wallet, iDMiner, COUNT_MAX_BLOCKS_STORED, ref message)
          || !ledger.TryInsertBlock("spend of the whole Balance", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, ledger.GetBalanceNext(wallet.Hash160PKeyPublic) - FEE, t => { })], isValidExpected: true, null, ref message))
          return false;

        return ledger.AreSegmentsServedCorrect("served segments", heightFirst: 1, ref message)
          && TestHeightGuards(ledger, wallet, iDMiner, ref message);
      }

      // Counter-tested 2026-11-14 03:01
      static bool TestHeightGuards(Ledger ledgerServing, Wallet wallet, byte[] iDMiner, ref string message)
      {
        Console.WriteLine("  18. Height Guards: Serve nothing above tip, below undo records or installation.");

        Ledger ledgerInstalled = new(heightRoot: 10);

        return ledgerServing.IsNoSegmentServed("height above the tip", ledgerServing.GetHeightTip() + 1, ref message)
          && ledgerServing.IsNoSegmentServed("height below the retained undo records", 0, ref message)
          && TryInsertCoinbasesUpTo(ledgerInstalled, wallet, iDMiner, 13, ref message)
          && ledgerInstalled.IsNoSegmentServed("height below the installed height", 9, ref message)
          && ledgerInstalled.AreSegmentsServedCorrect("heights above the installed height", heightFirst: 11, ref message);
      }

      const int INDEX_SEGMENT = 5;

      // Counter-tested 2026-11-14 03:35
      internal static bool TestMalformedSegments(ref string message)
      {
        Console.WriteLine("  19. Malformed Segments: Reject segments with wrong length, count, segment or order.");

        byte[] iDFirst = CreateIDAccount(INDEX_SEGMENT, 1);
        byte[] iDSecond = CreateIDAccount(INDEX_SEGMENT, 2);
        byte[] iDThird = CreateIDAccount(INDEX_SEGMENT, 3);
        byte[] segmentValid = SerializeSegment(3, iDFirst, iDSecond, iDThird);

        return IsSegmentValidExpected("segment without accounts", SerializeSegment(0), isValidExpected: true, ref message)
          && IsSegmentValidExpected("segment with three accounts in ascending order", segmentValid, isValidExpected: true, ref message)
          && IsSegmentValidExpected("segment ending with the highest ID of its segment", SerializeSegment(2, iDFirst, CreateIDAccountHighest(INDEX_SEGMENT)), isValidExpected: true, ref message)
          && IsSegmentValidExpected("empty buffer", [], isValidExpected: false, ref message)
          && IsSegmentValidExpected("segment one byte shorter than its accounts", segmentValid[..^1], isValidExpected: false, ref message)
          && IsSegmentValidExpected("segment one byte longer than its accounts", [.. segmentValid, 0x00], isValidExpected: false, ref message)
          && IsSegmentValidExpected("count one higher than the accounts", SerializeSegment(4, iDFirst, iDSecond, iDThird), isValidExpected: false, ref message)
          && IsSegmentValidExpected("count above int.MaxValue", [.. VarInt.GetBytes((ulong)int.MaxValue + 1)], isValidExpected: false, ref message)
          && IsSegmentValidExpected("account with the lowest ID of the next segment", SerializeSegment(2, iDFirst, CreateIDAccount(INDEX_SEGMENT + 1, 0)), isValidExpected: false, ref message)
          && IsSegmentValidExpected("account with the highest ID of the previous segment", SerializeSegment(2, CreateIDAccountHighest(INDEX_SEGMENT - 1), iDFirst), isValidExpected: false, ref message)
          && IsSegmentValidExpected("two accounts in descending order", SerializeSegment(2, iDSecond, iDFirst), isValidExpected: false, ref message)
          && IsSegmentValidExpected("three accounts with the last two in descending order", SerializeSegment(3, iDFirst, iDThird, iDSecond), isValidExpected: false, ref message)
          && IsSegmentValidExpected("same ID twice", SerializeSegment(2, iDFirst, iDFirst), isValidExpected: false, ref message);
      }

      static bool IsSegmentValidExpected(string nameCase, byte[] segment, bool isValidExpected, ref string message)
      {
        bool isValid;

        try
        {
          isValid = DatabaseAccounts.IsSegmentValid(segment, INDEX_SEGMENT);
        }
        catch (Exception exception)
        {
          message = $"Malformed segments, {nameCase}: threw {exception.GetType().Name}: {exception.Message}";
          return false;
        }

        if (isValid == isValidExpected)
          return true;

        message = $"Malformed segments, {nameCase}: {(isValid ? "accepted the malformed" : "rejected the valid")} segment.";
        return false;
      }

      static byte[] SerializeSegment(int countAccounts, params byte[][] iDsAccount)
      {
        List<byte> bytes = [.. VarInt.GetBytes(countAccounts)];

        foreach (byte[] iDAccount in iDsAccount)
        {
          bytes.AddRange(iDAccount);
          bytes.AddRange(BitConverter.GetBytes(1));
          bytes.AddRange(BitConverter.GetBytes(0));
          bytes.AddRange(BitConverter.GetBytes(100L));
        }

        return [.. bytes];
      }

      static byte[] CreateIDAccountHighest(int indexSegment)
      {
        byte[] iDAccount = Enumerable.Repeat((byte)0xFF, 20).ToArray();
        int prefix = indexSegment << SHIFT_INDEX_SEGMENT | (1 << SHIFT_INDEX_SEGMENT) - 1;

        iDAccount[0] = (byte)(prefix >> 8);
        iDAccount[1] = (byte)prefix;

        return iDAccount;
      }

      // Counter-tested 2026-11-14 04:59
      internal static bool TestInstalledAccounts(Wallet wallet, ref string message)
      {
        Console.WriteLine("  20. Installed Accounts: Install served segments, compare all accounts and account counts.");

        const int HEIGHT_INSTALLED = COUNT_SEGMENTS + BITCOIN_REORG_MARGIN;
        const int HEIGHT_SWEEP_OLDEST = HEIGHT_INSTALLED - COUNT_SEGMENTS + 1;

        Ledger ledgerServing = new(heightRoot: 0);

        byte[] iDMiner = CreateIDAccount(1001, 1);
        byte[] iDReceiver = CreateIDAccount(1002, 2);
        byte[] iDPaidAroundSnapshot = CreateIDAccount(1000, 3);
        byte[] iDPaidBeforeSnapshot = CreateIDAccount(3, 4);
        byte[] iDCreatedAfterSnapshot = CreateIDAccount(3, 5);
        byte[] iDPaidByCarriedBlockOldest = CreateIDAccount(HEIGHT_SWEEP_OLDEST % COUNT_SEGMENTS, 6);
        byte[] iDInSegmentFirst = CreateIDAccount(0, 7);
        byte[] iDInSegmentLast = CreateIDAccount(COUNT_SEGMENTS - 1, 8);
        Wallet walletSpendingAfterInstallation = CreateWallet();

        if (!ledgerServing.TryInsertBlock("coinbase creating the wallet accounts and four accounts", [ledgerServing.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, 0, new TXOutputP2PKH(100, iDPaidAroundSnapshot), new TXOutputP2PKH(100, iDPaidBeforeSnapshot), new TXOutputP2PKH(100, iDInSegmentFirst), new TXOutputP2PKH(100, iDInSegmentLast), new TXOutputP2PKH(VALUE_FUNDING, walletSpendingAfterInstallation.Hash160PKeyPublic))], isValidExpected: true, null, ref message)
          || !ledgerServing.TryInsertBlock("spend from the wallet account", [ledgerServing.CreateCoinbase(wallet, iDMiner, FEE), ledgerServing.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => { })], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledgerServing, wallet, iDMiner, HEIGHT_SWEEP_OLDEST, ref message)
          || !ledgerServing.TryInsertBlock("output in the oldest carried block to the segment of the oldest sweep", [ledgerServing.CreateCoinbase(wallet, iDMiner, 0, new TXOutputP2PKH(40, iDPaidByCarriedBlockOldest))], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledgerServing, wallet, iDMiner, 499, ref message)
          || !ledgerServing.TryInsertBlock("output before the snapshot of its segment", [ledgerServing.CreateCoinbase(wallet, iDMiner, 0, new TXOutputP2PKH(10, iDPaidAroundSnapshot))], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledgerServing, wallet, iDMiner, 1499, ref message)
          || !ledgerServing.TryInsertBlock("output after and spend before the snapshots of their segments", [ledgerServing.CreateCoinbase(wallet, iDMiner, FEE, new TXOutputP2PKH(20, iDPaidAroundSnapshot)), ledgerServing.CreateTXSpend(wallet, iDPaidBeforeSnapshot, VALUE_SPEND, t => { })], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledgerServing, wallet, iDMiner, 2051, ref message)
          || !ledgerServing.TryInsertBlock("account created after its snapshot and spend of the whole Balance", [ledgerServing.CreateCoinbase(wallet, iDMiner, FEE, new TXOutputP2PKH(30, iDCreatedAfterSnapshot)), ledgerServing.CreateTXSpend(wallet, iDReceiver, ledgerServing.GetBalanceNext(wallet.Hash160PKeyPublic) - FEE, t => { })], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledgerServing, wallet, iDMiner, HEIGHT_INSTALLED + 2, ref message)
          || !ledgerServing.TryInsertBlock("spend after the installation to the segment swept first after it", [ledgerServing.CreateCoinbase(wallet, iDMiner, FEE), ledgerServing.CreateTXSpend(walletSpendingAfterInstallation, iDPaidByCarriedBlockOldest, VALUE_SPEND, t => { })], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledgerServing, wallet, iDMiner, HEIGHT_INSTALLED + BITCOIN_REORG_MARGIN, ref message))
          return false;

        Ledger ledgerInstalled = new(ledgerServing, HEIGHT_INSTALLED);

        return ledgerInstalled.AreAccountsInstalledCorrect(ref message)
          && TestContinuedBlocks(ledgerServing, ledgerInstalled, ref message);
      }

      // Counter-tested 2026-11-14 04:59
      static bool TestContinuedBlocks(Ledger ledgerServing, Ledger ledgerInstalled, ref string message)
      {
        Console.WriteLine("  21. Continued Blocks: Insert later blocks into installed database, comparing accounts and segments.");

        int heightInstalled = ledgerInstalled.GetHeightTip();

        return ledgerInstalled.TryInsertBlocksAbove(ledgerServing, ref message)
          && ledgerInstalled.AreSegmentsServedCorrect("segments of the installed database", heightInstalled, ref message, ledgerServing)
          && ledgerInstalled.AreAccountsInstalledCorrect(ref message);
      }
    }
  }
}
