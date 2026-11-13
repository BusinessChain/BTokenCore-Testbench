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
