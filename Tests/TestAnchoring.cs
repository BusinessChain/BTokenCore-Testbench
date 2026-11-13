using System.Security.Cryptography;

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
        && BlockchainAnchoring.TestAnchorExpiry(Testbench.Wallet, ref message)
        && BlockchainAnchoring.TestParentRollback(Testbench.Wallet, ref message)
        && BlockchainAnchoring.TestRestartAnchored(Testbench.Wallet, ref message)
        && BlockchainAnchoring.TestRestartUnanchored(Testbench.Wallet, ref message)
        && BlockchainAnchoring.TestWindowResync(Testbench.Wallet, ref message);
    }

    class BlockchainBitcoinTestbench : BlockchainBitcoin
    {
      const uint NBITS_EASY = 0x207fffff;
      const uint NBITS_DIFFICULTY_1 = 0x1d00ffff;
      const int COUNT_HEADERS_RETARGET = 2016;
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
          merkleRootHash: RandomNumberGenerator.GetBytes(32),
          unixTimeSeconds: headerTip.UnixTimeSeconds + SECONDS_BLOCK,
          nBits: (headerTip.Height + 1) % COUNT_HEADERS_RETARGET == 0 ? NBITS_DIFFICULTY_1 : headerTip.NBits,
          nonce: 0);

        header.ComputeHash();

        if (header.NBits == NBITS_EASY)
          while (new UInt256(header.Hash).IsGreaterThan(UInt256.ParseFromCompact(header.NBits)))
          {
            header.Nonce += 1;
            header.ComputeHash();
          }

        foreach (TXOutputTokenAnchor anchor in anchors)
          header.AnchorsWinner[anchor.IDToken] = anchor;

        ChainRoot.AppendHeader(header);
        ChainRoot.BlocksQueued.Add(header.Height, new Block(0) { Header = header });

        UpdateTipBlockchain(ChainRoot);
      }

      internal BlockchainBitcoinTestbench Restart()
      {
        return new BlockchainBitcoinTestbench(LiteDatabase);
      }

      internal void DeleteBlocksStoredAbove(int height)
      {
        BlocksStored.DeleteMany(Query.GT(NAME_FIELD_ID, height));
      }

      internal void RollBackTo(int height)
      {
        Header headerCutOff = ChainRoot.HeaderTipBlockchain.GetHeaderAncestor(height + 1);

        RollBack(height);
        ChainRoot.CutOff(headerCutOff);

        UpdateTipBlockchain(ChainRoot);
      }
    }

    class BlockchainAnchoring : BlockchainTestbench
    {
      const string NAME_TOKEN = "TestAnchoring";
      const int COUNT_MAX_BLOCKS_STORED = COUNT_SEGMENTS + BITCOIN_REORG_MARGIN;      static readonly byte[] ID_TOKEN_BTOKEN = [(byte)'B', (byte)'T', (byte)'K', 0];

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

      internal static bool TestParentRollback(Wallet wallet, ref string message)
      {
        Console.WriteLine("  4. Parent Rollback: Roll back with Bitcoin, re-anchor rolled-back blocks from the store.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        BlockchainAnchoring blockchain = StartNode();

        blockchain.MineAnchors(blocksLedger);
        blockchain.DeliverBlocks(blocksLedger);

        if (!blockchain.IsSynchronized(ledger, "Parent rollback, Ledger blocks anchored before the rollback", 6, ref message))
          return false;

        blockchain.BlockchainParentBitcoin.RollBackTo(blockchain.BlockchainParentBitcoin.GetHeight() - 3);

        if (!blockchain.IsSynchronized(ledger, "Parent rollback, Bitcoin chain rolled back by three blocks", 3, ref message))
          return false;

        blockchain.BlockchainParentBitcoin.MineBlock();

        if (!blockchain.IsAtHeight("Parent rollback, replacing Bitcoin block without anchor", 3, ref message))
          return false;

        blockchain.MineAnchors(blocksLedger.Where(b => b.Header.Height > 3).ToList());

        string nameCaseReanchored = "Parent rollback, rolled-back blocks anchored again by replacing Bitcoin blocks";

        return blockchain.IsSynchronized(ledger, nameCaseReanchored, 6, ref message)
          && blockchain.IsRequestingNoBlock(nameCaseReanchored, ref message);
      }

      internal static bool TestRestartAnchored(Wallet wallet, ref string message)
      {
        Console.WriteLine("  5. Restart Anchored: Re-anchor stored blocks through the reloaded Bitcoin chain.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        BlockchainAnchoring blockchain = StartNode();

        blockchain.MineAnchors(blocksLedger);
        blockchain.DeliverBlocks(blocksLedger);

        BlockchainAnchoring blockchainRestarted = StartNode(blockchain.LiteDatabase, blockchain.BlockchainParentBitcoin.Restart());

        string nameCase = "Restart anchored, restart with every stored block anchored";

        return blockchainRestarted.IsSynchronized(ledger, nameCase, 6, ref message)
          && blockchainRestarted.IsRequestingNoBlock(nameCase, ref message);
      }

      internal static bool TestRestartUnanchored(Wallet wallet, ref string message)
      {
        Console.WriteLine("  6. Restart Unanchored: Roll back blocks whose Bitcoin block was lost, re-anchor later.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        BlockchainAnchoring blockchain = StartNode();

        blockchain.MineAnchors(blocksLedger);
        blockchain.DeliverBlocks(blocksLedger);

        blockchain.BlockchainParentBitcoin.DeleteBlocksStoredAbove(blockchain.BlockchainParentBitcoin.GetHeight() - 3);

        BlockchainAnchoring blockchainRestarted = StartNode(blockchain.LiteDatabase, blockchain.BlockchainParentBitcoin.Restart());

        if (!blockchainRestarted.IsSynchronized(ledger, "Restart unanchored, restart after the three latest Bitcoin blocks were lost", 3, ref message))
          return false;

        blockchainRestarted.MineAnchors(blocksLedger.Where(b => b.Header.Height > 3).ToList());

        string nameCaseReanchored = "Restart unanchored, rolled-back blocks anchored again after the restart";

        return blockchainRestarted.IsSynchronized(ledger, nameCaseReanchored, 6, ref message)
          && blockchainRestarted.IsRequestingNoBlock(nameCaseReanchored, ref message);
      }

      // Counter-tested 2026-11-13 18:36
      internal static bool TestWindowResync(Wallet wallet, ref string message)
      {
        Console.WriteLine("  7. Window Resync: Drop accounts, re-root when a fork outruns the stored window.");

        const int HEIGHT_FORK = 3;
        const int HEIGHT_TIP = 6;
        const int HEIGHT_RESYNC = HEIGHT_TIP + COUNT_MAX_BLOCKS_STORED + 1;

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksMain = ledger.GetBlocksInserted();
        BlockchainAnchoring blockchain = StartNode();

        blockchain.MineAnchors(blocksMain);
        blockchain.DeliverBlocks(blocksMain);

        if (!blockchain.IsSynchronized(ledger, "Window resync, main chain anchored and delivered", HEIGHT_TIP, ref message))
          return false;

        while (ledger.GetHeightTip() > HEIGHT_FORK)
          if (!ledger.TryRollBack("Window resync, fork below the tip", out _, ref message))
            return false;

        if (!TryInsertCoinbasesUpTo(ledger, wallet, CreateIDAccount(1002, 3), HEIGHT_RESYNC + 1, ref message))
          return false;

        List<Block> blocksFork = ledger.GetBlocksInserted().Where(b => b.Header.Height > HEIGHT_FORK).ToList();

        blockchain.MineAnchors(blocksFork.Where(b => b.Header.Height < HEIGHT_RESYNC).ToList());

        string nameCaseInside = $"Window resync, fork anchored up to {COUNT_MAX_BLOCKS_STORED} blocks above the tip";

        if (!blockchain.IsAtHeight(nameCaseInside, HEIGHT_TIP, ref message)
          || !blockchain.IsBlockStoredOldestAt(nameCaseInside, 1, ref message))
          return false;

        foreach (Block blockFork in blocksFork.Where(b => b.Header.Height >= HEIGHT_RESYNC))
        {
          int heightRoot = blockFork.Header.Height - COUNT_MAX_BLOCKS_STORED;
          string nameCase = $"Window resync, fork anchored up to height {blockFork.Header.Height}";

          blockchain.MineAnchors([blockFork]);

          if (!blockchain.IsWithoutAccounts(nameCase, heightRoot, ref message)
            || !blockchain.IsBlockStoredOldestAt(nameCase, heightRoot, ref message))
            return false;
        }

        blockchain.DeliverBlocks(blocksFork);

        return blockchain.IsWithoutAccounts("Window resync, fork blocks delivered after the resync", HEIGHT_RESYNC + 1, ref message);
      }

      bool IsBlockStoredOldestAt(string nameCase, int heightExpected, ref string message)
      {
        if (GetHeightBlockStoredOldest() == heightExpected)
          return true;

        message = $"{nameCase}: the oldest stored block lies at height {GetHeightBlockStoredOldest()}, expected {heightExpected}.";
        return false;
      }

      bool IsRequestingNoBlock(string nameCase, ref string message)
      {
        if (FetchHeaderBlockMissingNext(h => true) is not Header headerRequested)
          return true;

        message = $"{nameCase}: requested the block at height {headerRequested.Height} although the node holds it.";
        return false;
      }

      void MineAnchors(List<Block> blocksLedger)
      {
        foreach (Block blockLedger in blocksLedger)
          BlockchainParentBitcoin.MineBlock(CreateAnchor(blockLedger));
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

        return StartNode(CreateLiteDatabase(), new BlockchainBitcoinTestbench(CreateLiteDatabase()));
      }

      static BlockchainAnchoring StartNode(LiteDatabase liteDatabase, BlockchainBitcoinTestbench blockchainParent)
      {
        BlockchainAnchoring blockchain = new(liteDatabase, blockchainParent);
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
