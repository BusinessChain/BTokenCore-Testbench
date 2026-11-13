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
        && BlockchainAnchoring.TestAwaitingAnchor(Testbench.Wallet, ref message)
        && BlockchainAnchoring.TestAnchorExpiry(Testbench.Wallet, ref message);
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

      internal void MineBlock(params TXOutputTokenAnchor[] anchors)
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

        foreach (TXOutputTokenAnchor anchor in anchors)
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

          if (!blockchain.IsSynchronized(ledger, $"{nameCase} stored when its anchor arrived", height, ref message)
            || !blockchain.IsRequestingNoBlock(nameCase, ref message))
            return false;
        }

        return true;
      }

      internal static bool TestAnchorExpiry(Wallet wallet, ref string message)
      {
        Console.WriteLine("  3. Anchor Expiry: Drop a stored block six Bitcoin blocks after its LockTime.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        BlockchainAnchoring blockchain = StartNode();

        int lockTime = blockchain.BlockchainParentBitcoin.GetHeight();
        blockchain.StoreBlockAwaitingAnchor(blocksLedger[0], lockTime);
        blockchain.MineBlocksUpTo(lockTime + BITCOIN_REORG_MARGIN - 1);
        blockchain.BlockchainParentBitcoin.MineBlock(CreateAnchor(blocksLedger[0]));

        string nameCaseKept = $"Anchor expiry, block at height 1 anchored {BITCOIN_REORG_MARGIN} Bitcoin blocks after its LockTime";

        if (!blockchain.IsSynchronized(ledger, nameCaseKept, 1, ref message)
          || !blockchain.IsRequestingNoBlock(nameCaseKept, ref message))
          return false;

        lockTime = blockchain.BlockchainParentBitcoin.GetHeight();
        blockchain.StoreBlockAwaitingAnchor(blocksLedger[1], lockTime);
        blockchain.MineBlocksUpTo(lockTime + BITCOIN_REORG_MARGIN);
        blockchain.BlockchainParentBitcoin.MineBlock(CreateAnchor(blocksLedger[1]));

        string nameCaseDropped = $"Anchor expiry, block at height 2 anchored {BITCOIN_REORG_MARGIN + 1} Bitcoin blocks after its LockTime";

        if (!blockchain.IsAtHeight(nameCaseDropped, 1, ref message))
          return false;

        if (blockchain.FetchHeaderBlockMissingNext(h => true) is not Header headerRequested || headerRequested.Height != 2)
        {
          message = $"{nameCaseDropped}: the dropped block is not requested.";
          return false;
        }

        blockchain.ReceiveBlock(blocksLedger[1]);

        return blockchain.IsSynchronized(ledger, $"{nameCaseDropped}, received after the request", 2, ref message);
      }

      bool IsRequestingNoBlock(string nameCase, ref string message)
      {
        if (FetchHeaderBlockMissingNext(h => true) is not Header headerRequested)
          return true;

        message = $"{nameCase}: requested the block at height {headerRequested.Height} although it was stored awaiting its anchor.";
        return false;
      }

      void MineBlocksUpTo(int height)
      {
        while (BlockchainParentBitcoin.GetHeight() < height)
          BlockchainParentBitcoin.MineBlock();
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
