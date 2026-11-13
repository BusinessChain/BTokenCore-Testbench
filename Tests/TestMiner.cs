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

      return BlockchainMining.TestMineCoinbase(Testbench.Wallet, ref message);
    }

    class BlockchainMining : BlockchainTestbench
    {
      const string NAME_TOKEN = "TestMiner";

      BlockchainParentFake ParentFake;

      BlockchainMining(LiteDatabase liteDatabase, BlockchainParentFake blockchainParent)
        : base(NAME_TOKEN, liteDatabase, blockchainParent)
      {
        ParentFake = blockchainParent;
      }

      internal static bool TestMineCoinbase(Wallet wallet, ref string message)
      {
        Console.WriteLine("  1. Mine Coinbase: Insert a coinbase-only mined block after its anchor.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        BlockchainMining blockchain = StartNode(ledger);

        if (!blockchain.IsSynchronized(ledger, "Mine coinbase, Ledger blocks anchored and delivered", ledger.GetHeightTip(), ref message))
          return false;

        int height = ledger.GetHeightTip() + 1;
        long valueExpected = GetBlockReward(height) + ledger.GetRentNext();

        if (!blockchain.TryMineBlock("Mine coinbase", out Block block, ref message))
          return false;

        if (block.TXs.Count != 1)
        {
          message = $"Mine coinbase: the mined block holds {block.TXs.Count} TXs, expected only the coinbase.";
          return false;
        }

        TXBToken tXCoinbase = (TXBToken)block.TXs[0];

        if (tXCoinbase.TXOutputs.Count != 1 || tXCoinbase.TXOutputs[0] is not TXOutputP2PKH outputCoinbase)
        {
          message = $"Mine coinbase: the coinbase holds {tXCoinbase.TXOutputs.Count} outputs, expected one TXOutputP2PKH.";
          return false;
        }

        if (outputCoinbase.Value != valueExpected)
        {
          message = $"Mine coinbase: the coinbase pays {outputCoinbase.Value}, expected block reward plus rent {valueExpected}.";
          return false;
        }

        if (!outputCoinbase.IDAccount.IsAllBytesEqual(blockchain.Wallet.Hash160PKeyPublic))
        {
          message = $"Mine coinbase: the coinbase pays to {outputCoinbase.IDAccount.ToHexString()}, expected the receiving key {blockchain.Wallet.Hash160PKeyPublic.ToHexString()}.";
          return false;
        }

        return true;
      }

      bool TryMineBlock(string nameCase, out Block block, ref string message)
      {
        block = null;
        int height = GetHeight() + 1;

        ParentFake.AnchorCreatedLast = null;

        MinerBlockchain.MineBlock();

        if (ParentFake.AnchorCreatedLast is not TXOutputTokenAnchor anchor)
        {
          message = $"{nameCase}: MineBlock created no anchor.";
          return false;
        }

        ParentFake.MineBlock(anchor);

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

      static BlockchainMining StartNode(Ledger ledger)
      {
        CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);
        CreateWalletFile(NAME_TOKEN);

        BlockchainMining blockchain = new(CreateLiteDatabase(), new BlockchainParentFake(CreateLiteDatabase()));
        blockchain.Load();

        List<Block> blocksLedger = ledger.GetBlocksInserted();

        blockchain.ParentFake.MineAnchors(blocksLedger);
        blockchain.DeliverBlocks(blocksLedger);

        return blockchain;
      }
    }
  }
}
