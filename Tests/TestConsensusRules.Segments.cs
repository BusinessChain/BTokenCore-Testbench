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
        Console.WriteLine("  17. Serve the segment of every block within the retained undo records as it was right after that block.");

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
        Console.WriteLine("  18. Serve no segment above the tip, below the retained undo records or below the installed height.");

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
        Console.WriteLine("  19. Accept well-formed segments and reject segments with wrong length, count, segment index or order of IDs.");

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

      partial class Ledger
      {
        internal bool AreSegmentsServedCorrect(string nameCase, int heightFirst, ref string message)
        {
          foreach ((Header header, _, _) in BlocksInserted.Where(b => b.header.Height >= heightFirst))
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
