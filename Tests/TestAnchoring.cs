using BTokenCore;

using LiteDB;

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  class TestAnchoring : Test_Testbench
  {
    internal TestAnchoring(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      return BlockchainAnchoring.TestAnchoredFollow(Testbench.Wallet, ref message)
        && BlockchainAnchoring.TestAwaitingAnchor(Testbench.Wallet, ref message);
    }

    class BlockchainBitcoinTestbench : BlockchainBitcoin
    {
      const uint NBITS_EASY = 0x207fffff;
      const uint SECONDS_BLOCK = 600;

      internal BlockchainBitcoinTestbench(LiteDatabase liteDatabase)
        : base(liteDatabase, new SemaphoreSlim(1))
      { }

      protected override Header CreateHeaderRoot()
      {
        HeaderBitcoin header = (HeaderBitcoin)base.CreateHeaderRoot();
        header.NBits = NBITS_EASY;

        return header;
      }

      internal void MineBlock(TXOutputTokenAnchor anchor)
      {
        HeaderBitcoin headerTip = (HeaderBitcoin)ChainRoot.HeaderTip;

        HeaderBitcoin header = new(
          headerHash: null,
          version: 0x20000000,
          hashPrevious: headerTip.Hash,
          merkleRootHash: new byte[32],
          unixTimeSeconds: headerTip.UnixTimeSeconds + SECONDS_BLOCK,
          nBits: headerTip.NBits,
          nonce: 0);

        for (header.ComputeHash(); new UInt256(header.Hash).IsGreaterThan(UInt256.ParseFromCompact(header.NBits)); header.ComputeHash())
          header.Nonce += 1;

        header.AnchorsWinner[anchor.IDToken] = anchor;

        ChainRoot.AppendHeader(header);
        ChainRoot.BlocksQueued.Add(header.Height, new Block(0) { Header = header });

        UpdateTipBlockchain(ChainRoot);
      }
    }

    class BlockchainAnchoring : BlockchainTestbench
    {
      const string NAME_TOKEN = "TestAnchoring";
      static readonly byte[] ID_TOKEN_BTOKEN = [(byte)'B', (byte)'T', (byte)'K', 0];

      BlockchainBitcoinTestbench BlockchainParentBitcoin;

      BlockchainAnchoring(LiteDatabase liteDatabase, BlockchainBitcoinTestbench blockchainParent)
        : base(NAME_TOKEN, liteDatabase, blockchainParent)
      {
        BlockchainParentBitcoin = blockchainParent;
      }

      internal static bool TestAnchoredFollow(Wallet wallet, ref string message)
      {
        Console.WriteLine("  1. Anchored Follow: Insert each Ledger block once its anchor arrives.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        BlockchainAnchoring blockchain = StartNode();

        foreach (Block blockLedger in blocksLedger)
        {
          int height = blockLedger.Header.Height;

          blockchain.ReceiveBlock(blockLedger);

          if (!blockchain.IsAtHeight($"Anchored follow, block at height {height} received before its anchor", height - 1, ref message))
            return false;

          blockchain.BlockchainParentBitcoin.MineBlock(CreateAnchor(blockLedger));

          blockchain.DeliverBlocks(blocksLedger);

          if (!blockchain.IsSynchronized(ledger, $"Anchored follow, block at height {height} delivered after its anchor", height, ref message))
            return false;
        }

        return true;
      }

      internal static bool TestAwaitingAnchor(Wallet wallet, ref string message)
      {
        Console.WriteLine("  2. Awaiting Anchor: Insert a stored block at once when its anchor arrives.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        BlockchainAnchoring blockchain = StartNode();

        foreach (Block blockLedger in ledger.GetBlocksInserted())
        {
          int height = blockLedger.Header.Height;
          string nameCase = $"Awaiting anchor, block at height {height}";

          blockchain.StoreBlockAwaitingAnchor(blockLedger, blockchain.BlockchainParentBitcoin.GetHeight());

          if (!blockchain.IsAtHeight($"{nameCase} stored before its anchor", height - 1, ref message))
            return false;

          blockchain.BlockchainParentBitcoin.MineBlock(CreateAnchor(blockLedger));

          if (!blockchain.IsSynchronized(ledger, $"{nameCase} stored when its anchor arrived", height, ref message))
            return false;

          if (blockchain.FetchHeaderBlockMissingNext(h => true) is Header headerRequested)
          {
            message = $"{nameCase}: requested the block at height {headerRequested.Height} although it was stored awaiting its anchor.";
            return false;
          }
        }

        return true;
      }

      static TXOutputTokenAnchor CreateAnchor(Block blockLedger)
      {
        return new TXOutputTokenAnchor
        {
          IDToken = ID_TOKEN_BTOKEN,
          HashBlockReferenced = blockLedger.Header.Hash,
          HashBlockPreviousReferenced = blockLedger.Header.HashPrevious
        };
      }

      static BlockchainAnchoring StartNode()
      {
        CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);
        CreateWalletFile(NAME_TOKEN);

        BlockchainAnchoring blockchain = new(CreateLiteDatabase(), new BlockchainBitcoinTestbench(CreateLiteDatabase()));
        blockchain.Load();

        return blockchain;
      }

      void DeliverBlocks(List<Block> blocksLedger)
      {
        while (FetchHeaderBlockMissingNext(h => true) is Header header)
          ReceiveBlock(blocksLedger.Single(b => b.Header.Hash.IsAllBytesEqual(header.Hash)));
      }
    }
  }
}
