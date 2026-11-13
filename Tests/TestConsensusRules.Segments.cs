using System.Security.Cryptography;

using BTokenCore;

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  partial class TestConsensusRules
  {
    abstract partial class BlockchainAccounts
    {
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

      internal static bool TestInstalledAccounts(Wallet wallet, ref string message)
      {
        Console.WriteLine("  20. Installed Accounts: Install served segments, compare all accounts and account counts.");

        const int HEIGHT_INSTALLED = COUNT_SEGMENTS + BITCOIN_REORG_MARGIN;

        Ledger ledgerServing = new(heightRoot: 0);

        byte[] iDMiner = CreateIDAccount(1001, 1);
        byte[] iDReceiver = CreateIDAccount(1002, 2);
        byte[] iDPaidAroundSnapshot = CreateIDAccount(1000, 3);
        byte[] iDPaidBeforeSnapshot = CreateIDAccount(3, 4);
        byte[] iDCreatedAfterSnapshot = CreateIDAccount(3, 5);

        if (!ledgerServing.TryInsertBlock("coinbase creating the wallet account and two accounts", [ledgerServing.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, 0, new TXOutputP2PKH(100, iDPaidAroundSnapshot), new TXOutputP2PKH(100, iDPaidBeforeSnapshot))], isValidExpected: true, null, ref message)
          || !ledgerServing.TryInsertBlock("spend from the wallet account", [ledgerServing.CreateCoinbase(wallet, iDMiner, FEE), ledgerServing.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => { })], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledgerServing, wallet, iDMiner, 499, ref message)
          || !ledgerServing.TryInsertBlock("output before the snapshot of its segment", [ledgerServing.CreateCoinbase(wallet, iDMiner, 0, new TXOutputP2PKH(10, iDPaidAroundSnapshot))], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledgerServing, wallet, iDMiner, 1499, ref message)
          || !ledgerServing.TryInsertBlock("output after and spend before the snapshots of their segments", [ledgerServing.CreateCoinbase(wallet, iDMiner, FEE, new TXOutputP2PKH(20, iDPaidAroundSnapshot)), ledgerServing.CreateTXSpend(wallet, iDPaidBeforeSnapshot, VALUE_SPEND, t => { })], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledgerServing, wallet, iDMiner, 2051, ref message)
          || !ledgerServing.TryInsertBlock("account created after its snapshot and spend of the whole Balance", [ledgerServing.CreateCoinbase(wallet, iDMiner, FEE, new TXOutputP2PKH(30, iDCreatedAfterSnapshot)), ledgerServing.CreateTXSpend(wallet, iDReceiver, ledgerServing.GetBalanceNext(wallet.Hash160PKeyPublic) - FEE, t => { })], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledgerServing, wallet, iDMiner, HEIGHT_INSTALLED + BITCOIN_REORG_MARGIN, ref message))
          return false;

        Ledger ledgerInstalled = new(ledgerServing, HEIGHT_INSTALLED);

        return ledgerInstalled.AreAccountsInstalledCorrect(ref message)
          && TestContinuedBlocks(ledgerServing, ledgerInstalled, ref message);
      }

      static bool TestContinuedBlocks(Ledger ledgerServing, Ledger ledgerInstalled, ref string message)
      {
        Console.WriteLine("  21. Continued Blocks: Insert later blocks into installed database, comparing accounts.");

        return ledgerInstalled.TryInsertBlocksAbove(ledgerServing, ref message);
      }

      partial class Ledger
      {
        internal Ledger(Ledger ledgerServing, int heightInstalled)
        {
          Dictionary<Header, Block> blocksServed = ledgerServing.BlocksInserted.ToDictionary(b => b.block.Header, b => b.block);
          (byte[] segment, Header header)[] sweeps = new (byte[], Header)[COUNT_SEGMENTS];

          foreach (Header header in blocksServed.Keys.Where(h => h.Height > heightInstalled - COUNT_SEGMENTS && h.Height <= heightInstalled))
            sweeps[header.Height % COUNT_SEGMENTS] = (ledgerServing.Accounts.SerializeSegmentAtHeight(header.Height), header);

          Header headerInstalled = sweeps[heightInstalled % COUNT_SEGMENTS].header;

          Accounts.Load(LiteDatabase);
          LiteDatabase.RunTransaction(() => Accounts.Install(sweeps, headerInstalled, h => blocksServed[h], out _));

          AccountsReference = ledgerServing.BlocksInserted.FirstOrDefault(b => b.block.Header.Height == heightInstalled + 1).accountsBefore ?? ledgerServing.AccountsReference;
          IDsAccountSeen = new(ledgerServing.IDsAccountSeen, new EqualityComparerByteArray());
          HeightTip = heightInstalled;
          HashTip = headerInstalled.Hash;
        }

        internal bool TryInsertBlocksAbove(Ledger ledgerServing, ref string message)
        {
          foreach ((Block block, List<TXBToken> tXs, _) in ledgerServing.BlocksInserted.Where(b => b.block.Header.Height > HeightTip).ToList())
            if (!TryInsertBlock($"block of the serving ledger at height {block.Header.Height} inserted after the installation", tXs, isValidExpected: true, null, ref message))
              return false;

          return true;
        }

        internal bool AreAccountsInstalledCorrect(ref string message)
        {
          if (!AreAccountsStoredCorrect($"installed accounts at height {HeightTip}", AccountsReference, ref message))
            return false;

          int[] countsAccountExpected = new int[COUNT_SEGMENTS];

          foreach (byte[] iDAccount in ApplyBlock([], HeightTip + 1, out _).Keys)
            countsAccountExpected[GetIndexSegment(iDAccount)] += 1;

          (int[] countsAccount, _) = Accounts.LoadCountsAccountsPerSegmentNext();

          for (int i = 0; i < COUNT_SEGMENTS; i++)
            if (countsAccount[i] != countsAccountExpected[i])
            {
              message = $"Installed accounts at height {HeightTip}: segment {i} counts {countsAccount[i]} accounts, expected {countsAccountExpected[i]}.";
              return false;
            }

          return true;
        }

        internal bool AreSegmentsServedCorrect(string nameCase, int heightFirst, ref string message)
        {
          foreach (Header header in BlocksInserted.Select(b => b.block.Header).Where(h => h.Height >= heightFirst))
          {
            byte[] segment = Accounts.SerializeSegmentAtHeight(header.Height);

            if (segment == null)
            {
              message = $"Served segments, {nameCase}: no segment {header.Height % COUNT_SEGMENTS} served for height {header.Height}.";
              return false;
            }

            if (!SHA256.HashData(segment).IsAllBytesEqual(((HeaderBToken)header).HashAccounts))
            {
              message = $"Served segments, {nameCase}: segment {header.Height % COUNT_SEGMENTS} served for height {header.Height} doesn't match HashAccounts of its block.";
              return false;
            }
          }

          return true;
        }

        internal bool IsNoSegmentServed(string nameCase, int height, ref string message)
        {
          if (Accounts.SerializeSegmentAtHeight(height) == null)
            return true;

          message = $"Served segments, {nameCase}: served segment {height % COUNT_SEGMENTS} for height {height}.";
          return false;
        }
      }
    }
  }
}
