using BTokenCore;

using LiteDB;

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  class TestMiner : Test_Testbench
  {
    internal TestMiner(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      return BlockchainMining.TestMineCoinbase(Testbench.Wallet, ref message)
        && BlockchainMining.TestMinePoolTXs(Testbench.Wallet, ref message)
        && BlockchainMining.TestFillBlock(Testbench.Wallet, ref message)
        && BlockchainMining.TestAnchorFunding(Testbench.Wallet, ref message)
        && BlockchainMining.TestMissedReplacement(Testbench.Wallet, ref message)
        && BlockchainMining.TestCandidateExpiry(Testbench.Wallet, ref message)
        && BlockchainMining.TestInsertedRemoval(Testbench.Wallet, ref message);
    }

    class BlockchainMining : BlockchainTestbench
    {
      const string NAME_TOKEN = "TestMiner";
      const long VALUE_FUNDING = 100_000;
      const long FEE_ANCHOR = 1000;
      const int LENGTH_ANCHOR = 10 + 148 + 9 + TXOutputTokenAnchor.LENGTH_SCRIPT_ANCHOR_TOKEN + 34;

      BlockchainParentFake ParentFake;
      BlockchainBitcoin.TXBitcoin TXFunding;

      BlockchainMining(LiteDatabase liteDatabase, BlockchainParentFake blockchainParent)
        : base(NAME_TOKEN, liteDatabase, blockchainParent)
      {
        ParentFake = blockchainParent;
      }

      internal static bool TestMineCoinbase(Wallet wallet, ref string message)
      {
        Console.WriteLine("  1. Mine Coinbase: Insert a coinbase-only mined block after its anchor.");

        const string NAME_CASE = "Mine coinbase";

        if (!TryStartNode(wallet, NAME_CASE, VALUE_FUNDING, out BlockchainMining blockchain, out Ledger ledger, ref message))
          return false;

        long valueExpected = GetBlockReward(ledger.GetHeightTip() + 1) + ledger.GetRentNext();

        return blockchain.TryMineBlock(NAME_CASE, out Block block, ref message)
          && blockchain.IsBlockMinedCorrect(NAME_CASE, block, [], valueExpected, ref message);
      }

      internal static bool TestMinePoolTXs(Wallet wallet, ref string message)
      {
        Console.WriteLine("  2. Mine PoolTXs: Include ranked pool TXs, pay their fees, empty the pool.");

        const string NAME_CASE = "Mine pool TXs";
        const int COUNT_TXS = 3;

        if (!TryStartNode(wallet, NAME_CASE, VALUE_FUNDING, out BlockchainMining blockchain, out Ledger ledger, ref message))
          return false;

        if (!blockchain.TryAddSpends(NAME_CASE, ledger, wallet, COUNT_TXS, 0, out List<TXBToken> tXsRanked, ref message))
          return false;

        long valueExpected = GetBlockReward(ledger.GetHeightTip() + 1) + ledger.GetRentNext() + tXsRanked.Sum(t => t.Fee);

        if (!blockchain.TryMineBlock(NAME_CASE, out Block block, ref message)
          || !blockchain.IsBlockMinedCorrect(NAME_CASE, block, tXsRanked, valueExpected, ref message))
          return false;

        if (blockchain.GetHashesTXNotInPool(tXsRanked.Select(t => t.Hash).ToList()).Count == COUNT_TXS)
          return true;

        message = $"{NAME_CASE}: the pool still holds mined TXs after the block's insertion.";
        return false;
      }

      internal static bool TestFillBlock(Wallet wallet, ref string message)
      {
        Console.WriteLine("  3. Fill Block: Mine a pool larger than SizeBlockMax into one block.");

        const string NAME_CASE = "Fill block";
        const int COUNT_TXS = 25;
        const int COUNT_OUTPUTS_ADDED = 1500;

        if (!TryStartNode(wallet, NAME_CASE, VALUE_FUNDING, out BlockchainMining blockchain, out Ledger ledger, ref message)
          || !blockchain.TryAddSpends(NAME_CASE, ledger, wallet, COUNT_TXS, COUNT_OUTPUTS_ADDED, out List<TXBToken> tXsRanked, ref message))
          return false;

        if (tXsRanked.Sum(t => t.TXRaw.Length) <= blockchain.SizeBlockMax)
        {
          message = $"{NAME_CASE}: the pool holds only {tXsRanked.Sum(t => t.TXRaw.Length)} bytes, not more than SizeBlockMax {blockchain.SizeBlockMax}.";
          return false;
        }

        long rent = ledger.GetRentNext();
        int heightMined = ledger.GetHeightTip() + 1;

        if (!blockchain.TryMineBlock(NAME_CASE, out Block block, ref message))
          return false;

        if (block.Buffer.Length > blockchain.SizeBlockMax)
        {
          message = $"{NAME_CASE}: the mined block at height {heightMined} takes {block.Buffer.Length} bytes, more than SizeBlockMax {blockchain.SizeBlockMax}.";
          return false;
        }

        int countTXsMined = block.TXs.Count - 1;

        if (countTXsMined < 1 || countTXsMined >= COUNT_TXS)
        {
          message = $"{NAME_CASE}: the mined block holds {countTXsMined} pool TXs, expected a strict prefix of the {COUNT_TXS} ranked pool TXs.";
          return false;
        }

        List<TXBToken> tXsExpected = tXsRanked.Take(countTXsMined).ToList();

        return blockchain.IsBlockMinedCorrect(NAME_CASE, block, tXsExpected, GetBlockReward(heightMined) + rent + tXsExpected.Sum(t => t.Fee), ref message);
      }

      internal static bool TestAnchorFunding(Wallet wallet, ref string message)
      {
        Console.WriteLine("  4. Anchor Funding: Spend the funded output, confirm, fund the next anchor from change.");

        const string NAME_CASE = "Anchor funding";

        if (!TryStartNode(wallet, NAME_CASE, VALUE_FUNDING, out BlockchainMining blockchain, out _, ref message))
          return false;

        blockchain.SetFeeAnchor();

        if (!blockchain.TryMineAnchor(NAME_CASE, blockchain.MinerBlockchain.MineBlock, out BlockchainBitcoin.TXBitcoin tXAnchor, ref message)
          || !blockchain.IsAnchorSpending($"{NAME_CASE}, first anchor", tXAnchor, blockchain.TXFunding.Hash, 0, FEE_ANCHOR, ref message))
          return false;

        if (tXAnchor.TXOutputs.Count != 2
          || tXAnchor.TXOutputs[1] is not TXOutputP2PKH outputChange
          || outputChange.Value != VALUE_FUNDING - FEE_ANCHOR
          || !outputChange.IDAccount.IsAllBytesEqual(blockchain.ParentFake.Wallet.Hash160PKeyPublic))
        {
          message = $"{NAME_CASE}: the first anchor lacks a change output of {VALUE_FUNDING - FEE_ANCHOR} to the parent's wallet.";
          return false;
        }

        if (!blockchain.TryInsertAnchored(NAME_CASE, tXAnchor, out _, ref message))
          return false;

        if (blockchain.ParentFake.GetTX(tXAnchor.Hash) != null || blockchain.ParentFake.CountAnchorsReplaceable(FEE_ANCHOR) != 0)
        {
          message = $"{NAME_CASE}: the anchor mined into the Bitcoin block is not confirmed.";
          return false;
        }

        return blockchain.TryMineAnchor($"{NAME_CASE}, next anchor", blockchain.MinerBlockchain.MineBlock, out BlockchainBitcoin.TXBitcoin tXAnchorNext, ref message)
          && blockchain.IsAnchorSpending($"{NAME_CASE}, next anchor", tXAnchorNext, tXAnchor.Hash, 1, FEE_ANCHOR, ref message);
      }

      internal static bool TestMissedReplacement(Wallet wallet, ref string message)
      {
        Console.WriteLine("  5. Missed Replacement: Replace a missed anchor with raised fee, unless unaffordable.");

        const string NAME_CASE = "Missed replacement";

        if (!TryStartNode(wallet, NAME_CASE, VALUE_FUNDING, out BlockchainMining blockchain, out _, ref message))
          return false;

        blockchain.SetFeeAnchor();

        if (!blockchain.TryMineAnchor(NAME_CASE, blockchain.MinerBlockchain.MineBlock, out BlockchainBitcoin.TXBitcoin tXMissed, ref message)
          || !blockchain.TryMissAnchor(NAME_CASE, 1, ref message))
          return false;

        long feeReplacement = Math.Max(FEE_ANCHOR, tXMissed.Fee + LENGTH_ANCHOR);
        string nameCaseReplaced = $"{NAME_CASE}, anchor replaced";

        if (!blockchain.TryMineAnchor(nameCaseReplaced, blockchain.MinerBlockchain.ReplaceAnchorsMissed, out BlockchainBitcoin.TXBitcoin tXReplacement, ref message)
          || !blockchain.IsAnchorSpending(nameCaseReplaced, tXReplacement, blockchain.TXFunding.Hash, 0, feeReplacement, ref message))
          return false;

        if (blockchain.ParentFake.GetTX(tXMissed.Hash) != null)
        {
          message = $"{nameCaseReplaced}: the missed anchor is still an own unconfirmed TX.";
          return false;
        }

        if (tXReplacement.TXOutputs.OfType<TXOutputTokenAnchor>().Single().HashBlockReferenced.IsAllBytesEqual(tXMissed.TXOutputs.OfType<TXOutputTokenAnchor>().Single().HashBlockReferenced))
        {
          message = $"{nameCaseReplaced}: the replacement references the block of the missed anchor, expected a newly mined block.";
          return false;
        }

        if (!blockchain.TryInsertAnchored(nameCaseReplaced, tXReplacement, out _, ref message))
          return false;

        const string NAME_CASE_UNAFFORDABLE = "Missed replacement, output not covering the raised fee";

        if (!TryStartNode(wallet, NAME_CASE_UNAFFORDABLE, FEE_ANCHOR + LENGTH_ANCHOR - 1, out BlockchainMining blockchainPoor, out _, ref message))
          return false;

        blockchainPoor.SetFeeAnchor();

        if (!blockchainPoor.TryMineAnchor(NAME_CASE_UNAFFORDABLE, blockchainPoor.MinerBlockchain.MineBlock, out _, ref message)
          || !blockchainPoor.TryMissAnchor(NAME_CASE_UNAFFORDABLE, 0, ref message)
          || !IsAccepted($"{NAME_CASE_UNAFFORDABLE}, ReplaceAnchorsMissed", blockchainPoor.MinerBlockchain.ReplaceAnchorsMissed, ref message))
          return false;

        if (blockchainPoor.ParentFake.GetHashesTXOwnedUnconfirmed().Count == 0)
          return true;

        message = $"{NAME_CASE_UNAFFORDABLE}: ReplaceAnchorsMissed created an anchor the output cannot pay.";
        return false;
      }

      internal static bool TestCandidateExpiry(Wallet wallet, ref string message)
      {
        Console.WriteLine("  6. Candidate Expiry: Prune missed and replaced candidates after the reorg margin.");

        const string NAME_CASE = "Candidate expiry";
        const int COUNT_REPLACEMENTS = 2;

        if (!TryStartNode(wallet, NAME_CASE, VALUE_FUNDING, out BlockchainMining blockchain, out _, ref message))
          return false;

        blockchain.SetFeeAnchor();

        if (!blockchain.TryMineAnchor(NAME_CASE, blockchain.MinerBlockchain.MineBlock, out BlockchainBitcoin.TXBitcoin tXAnchor, ref message))
          return false;

        List<int> lockTimes = [tXAnchor.LockTime];

        for (int i = 0; i < COUNT_REPLACEMENTS; i++)
        {
          if (!blockchain.TryMissAnchor(NAME_CASE, 1, ref message)
            || !blockchain.TryMineAnchor($"{NAME_CASE}, replacement {i + 1}", blockchain.MinerBlockchain.ReplaceAnchorsMissed, out tXAnchor, ref message))
            return false;

          lockTimes.Add(tXAnchor.LockTime);
        }

        while (true)
        {
          int heightParent = blockchain.ParentFake.GetHeight();
          int countExpected = lockTimes.Count(l => l > heightParent - BITCOIN_REORG_MARGIN);
          int countStored = blockchain.BlocksStoredAwaitingAnchor.Count();

          if (countStored != countExpected)
          {
            message = $"{NAME_CASE}: at Bitcoin height {heightParent} the store holds {countStored} candidates, expected {countExpected} with LockTimes {string.Join(", ", lockTimes)}.";
            return false;
          }

          if (countExpected == 0)
            return true;

          blockchain.ParentFake.MineBlock([]);
        }
      }

      internal static bool TestInsertedRemoval(Wallet wallet, ref string message)
      {
        Console.WriteLine("  7. Inserted Removal: Remove the inserted candidate at once, its missed rival after the margin.");

        const string NAME_CASE = "Inserted removal";

        if (!TryStartNode(wallet, NAME_CASE, VALUE_FUNDING, out BlockchainMining blockchain, out _, ref message))
          return false;

        blockchain.SetFeeAnchor();

        if (!blockchain.TryMineAnchor(NAME_CASE, blockchain.MinerBlockchain.MineBlock, out BlockchainBitcoin.TXBitcoin tXMissed, ref message)
          || !blockchain.TryMissAnchor(NAME_CASE, 1, ref message)
          || !blockchain.TryMineAnchor($"{NAME_CASE}, replacement", blockchain.MinerBlockchain.ReplaceAnchorsMissed, out BlockchainBitcoin.TXBitcoin tXReplacement, ref message))
          return false;

        byte[] hashMissed = tXMissed.TXOutputs.OfType<TXOutputTokenAnchor>().Single().HashBlockReferenced;
        byte[] hashReplacement = tXReplacement.TXOutputs.OfType<TXOutputTokenAnchor>().Single().HashBlockReferenced;

        if (blockchain.BlocksStoredAwaitingAnchor.Count() != 2)
        {
          message = $"{NAME_CASE}: the store holds {blockchain.BlocksStoredAwaitingAnchor.Count()} candidates before the insertion, expected the missed and the replacing one.";
          return false;
        }

        if (!blockchain.TryInsertAnchored(NAME_CASE, tXReplacement, out _, ref message))
          return false;

        if (blockchain.BlocksStoredAwaitingAnchor.FindById(hashReplacement) != null
          || blockchain.BlocksStoredAwaitingAnchor.FindById(hashMissed) == null)
        {
          message = $"{NAME_CASE}: after the insertion the store must hold the missed candidate but not the inserted one.";
          return false;
        }

        while (blockchain.ParentFake.GetHeight() < tXMissed.LockTime + BITCOIN_REORG_MARGIN)
          blockchain.ParentFake.MineBlock([]);

        if (blockchain.BlocksStoredAwaitingAnchor.Count() == 0)
          return true;

        message = $"{NAME_CASE}: the store still holds {blockchain.BlocksStoredAwaitingAnchor.Count()} candidates {BITCOIN_REORG_MARGIN} Bitcoin blocks after the missed LockTime.";
        return false;
      }

      bool TryMissAnchor(string nameCase, int countReplaceableExpected, ref string message)
      {
        ParentFake.MineBlock([]);

        int countReplaceable = ParentFake.CountAnchorsReplaceable(FEE_ANCHOR);

        if (countReplaceable == countReplaceableExpected)
          return true;

        message = $"{nameCase}: CountAnchorsReplaceable returns {countReplaceable} after a Bitcoin block without the anchor, expected {countReplaceableExpected}.";
        return false;
      }

      void SetFeeAnchor()
      {
        StartMining(1, FEE_ANCHOR);
        StopMining();
      }

      bool IsAnchorSpending(string nameCase, BlockchainBitcoin.TXBitcoin tXAnchor, byte[] tXIDExpected, int indexExpected, long feeExpected, ref string message)
      {
        if (tXAnchor.Inputs.Count != 1
          || !tXAnchor.Inputs[0].TXIDOutput.IsAllBytesEqual(tXIDExpected)
          || tXAnchor.Inputs[0].IndexOutput != indexExpected)
        {
          message = $"{nameCase}: the anchor does not spend output {indexExpected} of TX {tXIDExpected.ToHexString()}.";
          return false;
        }

        if (tXAnchor.LockTime != ParentFake.GetHeight() || tXAnchor.Fee != feeExpected)
        {
          message = $"{nameCase}: the anchor has LockTime {tXAnchor.LockTime} and fee {tXAnchor.Fee}, expected the Bitcoin tip {ParentFake.GetHeight()} and {feeExpected}.";
          return false;
        }

        return true;
      }

      bool TryAddSpends(string nameCase, Ledger ledger, Wallet wallet, int countTXs, int countOutputsAdded, out List<TXBToken> tXsRanked, ref string message)
      {
        tXsRanked = null;

        byte[] iDReceiver = CreateIDAccount(1001, 2);
        HashSet<byte[]> hashesTX = new(new EqualityComparerByteArray());

        for (int i = 0; i < countTXs; i++)
        {
          int offsetNonce = i;

          TXBToken tX = ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t =>
          {
            t.Nonce += offsetNonce;
            t.Fee = FEE * (offsetNonce + 1);
            t.TXOutputs.AddRange(Enumerable.Range(0, countOutputsAdded).Select(_ => new TXOutputP2PKH(1, iDReceiver)));
          });

          if (!IsAccepted($"{nameCase}, spend {i} added to the pool", () => AddTX(ParseTXRaw(tX.TXRaw)), ref message))
            return false;

          hashesTX.Add(tX.Hash);
        }

        tXsRanked = GetTXsRanked(hashesTX);

        return true;
      }

      bool IsBlockMinedCorrect(string nameCase, Block block, List<TXBToken> tXsExpected, long valueCoinbaseExpected, ref string message)
      {
        if (block.TXs.Count != tXsExpected.Count + 1
          || !block.TXs.Skip(1).Select(t => t.Hash).SequenceEqual(tXsExpected.Select(t => t.Hash), new EqualityComparerByteArray()))
        {
          message = $"{nameCase}: the mined block holds {block.TXs.Count - 1} TXs after the coinbase, expected the {tXsExpected.Count} pool TXs in rank order.";
          return false;
        }

        TXBToken tXCoinbase = (TXBToken)block.TXs[0];

        if (tXCoinbase.TXOutputs.Count != 1 || tXCoinbase.TXOutputs[0] is not TXOutputP2PKH outputCoinbase)
        {
          message = $"{nameCase}: the coinbase holds {tXCoinbase.TXOutputs.Count} outputs, expected one TXOutputP2PKH.";
          return false;
        }

        if (outputCoinbase.Value != valueCoinbaseExpected)
        {
          message = $"{nameCase}: the coinbase pays {outputCoinbase.Value}, expected block reward plus rent plus fees {valueCoinbaseExpected}.";
          return false;
        }

        if (!outputCoinbase.IDAccount.IsAllBytesEqual(Wallet.Hash160PKeyPublic))
        {
          message = $"{nameCase}: the coinbase pays to {outputCoinbase.IDAccount.ToHexString()}, expected the receiving key {Wallet.Hash160PKeyPublic.ToHexString()}.";
          return false;
        }

        return true;
      }

      bool TryMineBlock(string nameCase, out Block block, ref string message)
      {
        block = null;

        return TryMineAnchor(nameCase, MinerBlockchain.MineBlock, out BlockchainBitcoin.TXBitcoin tXAnchor, ref message)
          && TryInsertAnchored(nameCase, tXAnchor, out block, ref message);
      }

      bool TryMineAnchor(string nameCase, Action mine, out BlockchainBitcoin.TXBitcoin tXAnchor, ref string message)
      {
        tXAnchor = null;

        if (!IsAccepted($"{nameCase}, mining", mine, ref message))
          return false;

        List<byte[]> hashesAnchor = ParentFake.GetHashesTXOwnedUnconfirmed();

        if (hashesAnchor.Count == 1 && ParentFake.GetTX(hashesAnchor[0]) is BlockchainBitcoin.TXBitcoin tXOwned)
        {
          tXAnchor = tXOwned;
          return true;
        }

        message = $"{nameCase}: mining left {hashesAnchor.Count} anchors at the Bitcoin tip, expected one.";
        return false;
      }

      bool TryInsertAnchored(string nameCase, BlockchainBitcoin.TXBitcoin tXAnchor, out Block block, ref string message)
      {
        block = null;
        int height = GetHeight() + 1;

        TXOutputTokenAnchor anchor = tXAnchor.TXOutputs.OfType<TXOutputTokenAnchor>().Single();

        ParentFake.MineBlock([tXAnchor], anchor);

        if (!IsAtHeight($"{nameCase}, mined block anchored", height, ref message))
          return false;

        Header header = ChainRoot.HeaderTipBlockchain;

        if (!header.Hash.IsAllBytesEqual(anchor.HashBlockReferenced))
        {
          message = $"{nameCase}: the tip {header.Hash.ToHexString()} is not the anchored block {anchor.HashBlockReferenced.ToHexString()}.";
          return false;
        }

        block = LoadBlock(height);

        if (block == null || !TryParseBlockStored(header, block))
        {
          message = $"{nameCase}: the mined block at height {height} is not stored.";
          return false;
        }

        return true;
      }

      static bool TryStartNode(Wallet wallet, string nameCase, long valueFunding, out BlockchainMining blockchain, out Ledger ledger, ref string message)
      {
        blockchain = null;

        if (!TryCreateLedger(wallet, out ledger, ref message))
          return false;

        CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);
        CreateWalletFile(NAME_TOKEN);

        blockchain = new(CreateLiteDatabase(), new BlockchainParentFake(CreateLiteDatabase()));
        blockchain.Load();
        blockchain.TXFunding = blockchain.ParentFake.Fund(valueFunding);

        List<Block> blocksLedger = ledger.GetBlocksInserted();

        blockchain.ParentFake.MineAnchors(blocksLedger);
        blockchain.DeliverBlocks(blocksLedger);

        return blockchain.IsSynchronized(ledger, $"{nameCase}, Ledger blocks anchored and delivered", ledger.GetHeightTip(), ref message);
      }
    }
  }
}
