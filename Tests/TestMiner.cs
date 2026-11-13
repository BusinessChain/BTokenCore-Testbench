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
        && BlockchainMining.TestFillBlock(Testbench.Wallet, ref message);
    }

    class BlockchainMining : BlockchainTestbench
    {
      const string NAME_TOKEN = "TestMiner";
      const long VALUE_FUNDING = 100_000;

      BlockchainParentFake ParentFake;

      BlockchainMining(LiteDatabase liteDatabase, BlockchainParentFake blockchainParent)
        : base(NAME_TOKEN, liteDatabase, blockchainParent)
      {
        ParentFake = blockchainParent;
      }

      internal static bool TestMineCoinbase(Wallet wallet, ref string message)
      {
        Console.WriteLine("  1. Mine Coinbase: Insert a coinbase-only mined block after its anchor.");

        const string NAME_CASE = "Mine coinbase";

        if (!TryStartNode(wallet, NAME_CASE, out BlockchainMining blockchain, out Ledger ledger, ref message))
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

        if (!TryStartNode(wallet, NAME_CASE, out BlockchainMining blockchain, out Ledger ledger, ref message))
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

        if (!TryStartNode(wallet, NAME_CASE, out BlockchainMining blockchain, out Ledger ledger, ref message)
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

          if (!IsAccepted($"{nameCase}, spend {i} added to the pool", () => AddTX(tX), ref message))
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
        int height = GetHeight() + 1;

        if (!IsAccepted($"{nameCase}, MineBlock", MinerBlockchain.MineBlock, ref message))
          return false;

        List<byte[]> hashesAnchor = ParentFake.GetHashesTXOwnedUnconfirmed();

        if (hashesAnchor.Count != 1 || ParentFake.GetTX(hashesAnchor[0]) is not BlockchainBitcoin.TXBitcoin tXAnchor)
        {
          message = $"{nameCase}: MineBlock created {hashesAnchor.Count} anchors at the Bitcoin tip, expected one.";
          return false;
        }

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

      static bool TryStartNode(Wallet wallet, string nameCase, out BlockchainMining blockchain, out Ledger ledger, ref string message)
      {
        blockchain = null;

        if (!TryCreateLedger(wallet, out ledger, ref message))
          return false;

        CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);
        CreateWalletFile(NAME_TOKEN);

        blockchain = new(CreateLiteDatabase(), new BlockchainParentFake(CreateLiteDatabase()));
        blockchain.Load();
        blockchain.ParentFake.Fund(VALUE_FUNDING);

        List<Block> blocksLedger = ledger.GetBlocksInserted();

        blockchain.ParentFake.MineAnchors(blocksLedger);
        blockchain.DeliverBlocks(blocksLedger);

        return blockchain.IsSynchronized(ledger, $"{nameCase}, Ledger blocks anchored and delivered", ledger.GetHeightTip(), ref message);
      }
    }
  }
}
