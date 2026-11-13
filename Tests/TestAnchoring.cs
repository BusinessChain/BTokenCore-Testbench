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
        && BlockchainAnchoring.TestWindowResync(Testbench.Wallet, ref message)
        && BlockchainAnchoring.TestRestartResync(Testbench.Wallet, ref message);
    }

    class BlockchainParentFake : BlockchainBitcoin
    {
      internal BlockchainParentFake(LiteDatabase liteDatabase)
        : base(liteDatabase, new SemaphoreSlim(1))
      {
        Parser = new ParserBToken();
      }

      protected override Header CreateHeaderRoot()
      {
        return new HeaderBToken { Hash = new byte[32], Height = 0 };
      }

      internal void MineBlock(params TXOutputTokenAnchor[] anchors)
      {
        HeaderBToken header = new(
          headerHash: null,
          hashPrevious: ChainRoot.HeaderTip.Hash,
          merkleRootHash: RandomNumberGenerator.GetBytes(32),
          hashAccounts: new byte[32],
          nonce: 0);

        header.ComputeHash();

        foreach (TXOutputTokenAnchor anchor in anchors)
          header.AnchorsWinner[anchor.IDToken] = anchor;

        ChainRoot.AppendHeader(header);
        ChainRoot.BlocksQueued.Add(header.Height, new Block(0) { Header = header });

        UpdateTipBlockchain(ChainRoot);
      }

      internal BlockchainParentFake Restart()
      {
        return new BlockchainParentFake(LiteDatabase);
      }

      internal void DeleteBlocksStored(int heightFirst, int heightLast)
      {
        BlocksStored.DeleteMany(Query.Between(NAME_FIELD_ID, heightFirst, heightLast));
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
      const int COUNT_MAX_BLOCKS_STORED = COUNT_SEGMENTS + BITCOIN_REORG_MARGIN;
      static readonly byte[] ID_TOKEN_BTOKEN = [(byte)'B', (byte)'T', (byte)'K', 0];

      BlockchainParentFake ParentFake;

      BlockchainAnchoring(LiteDatabase liteDatabase, BlockchainParentFake blockchainParent)
        : base(NAME_TOKEN, liteDatabase, blockchainParent)
      {
        ParentFake = blockchainParent;
      }

      // Counter-tested 2026-11-13 19:05
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

          blockchain.ParentFake.MineBlock(CreateAnchor(blockLedger));

          blockchain.DeliverBlocks(blocksLedger);

          if (!blockchain.IsSynchronized(ledger, $"Anchored follow, block at height {height} delivered after its anchor", height, ref message))
            return false;
        }

        return true;
      }

      // Counter-tested 2026-11-13 19:05
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

          blockchain.StoreBlockAwaitingAnchor(blockLedger, blockchain.ParentFake.GetHeight());

          if (!blockchain.IsAtHeight($"{nameCase} stored before its anchor", height - 1, ref message))
            return false;

          blockchain.ParentFake.MineBlock(CreateAnchor(blockLedger));

          if (!blockchain.IsSynchronized(ledger, $"{nameCase} stored when its anchor arrived", height, ref message)
            || !blockchain.IsRequestingNoBlock(nameCase, ref message))
            return false;
        }

        return true;
      }

      // Counter-tested 2026-11-13 19:05
      internal static bool TestAnchorExpiry(Wallet wallet, ref string message)
      {
        Console.WriteLine("  3. Anchor Expiry: Drop a stored block six Bitcoin blocks after its LockTime.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        BlockchainAnchoring blockchain = StartNode();

        int lockTime = blockchain.ParentFake.GetHeight();
        blockchain.StoreBlockAwaitingAnchor(blocksLedger[0], lockTime);
        blockchain.MineBlocksUpTo(lockTime + BITCOIN_REORG_MARGIN - 1);
        blockchain.ParentFake.MineBlock(CreateAnchor(blocksLedger[0]));

        string nameCaseKept = $"Anchor expiry, block at height 1 anchored {BITCOIN_REORG_MARGIN} Bitcoin blocks after its LockTime";

        if (!blockchain.IsSynchronized(ledger, nameCaseKept, 1, ref message)
          || !blockchain.IsRequestingNoBlock(nameCaseKept, ref message))
          return false;

        lockTime = blockchain.ParentFake.GetHeight();
        blockchain.StoreBlockAwaitingAnchor(blocksLedger[1], lockTime);
        blockchain.MineBlocksUpTo(lockTime + BITCOIN_REORG_MARGIN);
        blockchain.ParentFake.MineBlock(CreateAnchor(blocksLedger[1]));

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

      // Counter-tested 2026-11-13 19:05
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

        blockchain.ParentFake.RollBackTo(blockchain.ParentFake.GetHeight() - 3);

        if (!blockchain.IsSynchronized(ledger, "Parent rollback, Bitcoin chain rolled back by three blocks", 3, ref message))
          return false;

        blockchain.ParentFake.MineBlock();

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

        BlockchainAnchoring blockchainRestarted = StartNode(blockchain.LiteDatabase, blockchain.ParentFake.Restart());

        string nameCase = "Restart anchored, restart with every stored block anchored";

        return blockchainRestarted.IsSynchronized(ledger, nameCase, 6, ref message)
          && blockchainRestarted.IsRequestingNoBlock(nameCase, ref message);
      }

      internal static bool TestRestartUnanchored(Wallet wallet, ref string message)
      {
        Console.WriteLine("  6. Restart Unanchored: Roll back unanchored tip blocks, keep unanchored blocks below anchored.");

        const int HEIGHT_TIP_REROOTED = COUNT_MAX_BLOCKS_STORED + 7;
        const int HEIGHT_ROOT_REROOTED = HEIGHT_TIP_REROOTED - COUNT_MAX_BLOCKS_STORED;
        const int HEIGHT_ANCHOR_LOST_LAST = HEIGHT_ROOT_REROOTED + 3;

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        BlockchainAnchoring blockchain = StartNode();

        blockchain.MineAnchors(blocksLedger);
        blockchain.DeliverBlocks(blocksLedger);

        int heightParent = blockchain.ParentFake.GetHeight();
        blockchain.ParentFake.DeleteBlocksStored(heightParent - 2, heightParent);

        BlockchainAnchoring blockchainRestarted = StartNode(blockchain.LiteDatabase, blockchain.ParentFake.Restart());

        if (!blockchainRestarted.IsSynchronized(ledger, "Restart unanchored, restart after the three latest Bitcoin blocks were lost", 3, ref message))
          return false;

        blockchainRestarted.MineAnchors(blocksLedger.Where(b => b.Header.Height > 3).ToList());

        string nameCaseReanchored = "Restart unanchored, rolled-back blocks anchored again after the restart";

        if (!blockchainRestarted.IsSynchronized(ledger, nameCaseReanchored, 6, ref message)
          || !blockchainRestarted.IsRequestingNoBlock(nameCaseReanchored, ref message)
          || !TryInsertCoinbasesUpTo(ledger, wallet, CreateIDAccount(1000, 1), HEIGHT_TIP_REROOTED, ref message))
          return false;

        blocksLedger = ledger.GetBlocksInserted();
        BlockchainAnchoring blockchainRerooted = StartNode();

        blockchainRerooted.MineAnchors(blocksLedger);
        blockchainRerooted.DeliverBlocks(blocksLedger);

        string nameCaseRerooted = $"Restart unanchored, chain re-rooted at height {HEIGHT_ROOT_REROOTED}";

        if (!blockchainRerooted.IsWithoutAccounts(nameCaseRerooted, HEIGHT_TIP_REROOTED, ref message)
          || !blockchainRerooted.IsBlockStoredOldestAt(nameCaseRerooted, HEIGHT_ROOT_REROOTED + 1, ref message))
          return false;

        blockchainRerooted.ParentFake.DeleteBlocksStored(0, HEIGHT_ANCHOR_LOST_LAST);

        BlockchainAnchoring blockchainRerootedRestarted = StartNode(blockchainRerooted.LiteDatabase, blockchainRerooted.ParentFake.Restart());

        string nameCaseLostBelow = $"Restart unanchored, restart after the Bitcoin blocks up to height {HEIGHT_ANCHOR_LOST_LAST} were lost";

        return blockchainRerootedRestarted.IsWithoutAccounts(nameCaseLostBelow, HEIGHT_TIP_REROOTED, ref message)
          && blockchainRerootedRestarted.IsBlockStoredOldestAt(nameCaseLostBelow, HEIGHT_ROOT_REROOTED + 1, ref message);
      }

      // Counter-tested 2026-11-13 19:05
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

        if (!blockchain.IsSynchronized(ledger, "Window resync, main chain anchored and delivered", HEIGHT_TIP, ref message)
          || !TryForkLedger(ledger, wallet, HEIGHT_FORK, HEIGHT_RESYNC + 1, out List<Block> blocksFork, ref message))
          return false;

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

      internal static bool TestRestartResync(Wallet wallet, ref string message)
      {
        Console.WriteLine("  8. Restart Resync: Resync at restart when RollBackUnanchored drops below the window.");

        const int HEIGHT_FORK = 3;
        const int HEIGHT_TIP = 6;
        const int HEIGHT_TIP_FORK = HEIGHT_TIP + COUNT_MAX_BLOCKS_STORED;

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksMain = ledger.GetBlocksInserted();
        BlockchainAnchoring blockchain = StartNode();

        blockchain.MineAnchors(blocksMain);
        blockchain.DeliverBlocks(blocksMain);

        if (!blockchain.IsSynchronized(ledger, "Restart resync, main chain anchored and delivered", HEIGHT_TIP, ref message)
          || !TryForkLedger(ledger, wallet, HEIGHT_FORK, HEIGHT_TIP_FORK, out List<Block> blocksFork, ref message))
          return false;

        BlockchainAnchoring blockchainAnchoringFork = StartNode();
        blockchainAnchoringFork.MineAnchors([.. blocksMain.Where(b => b.Header.Height <= HEIGHT_FORK), .. blocksFork]);

        BlockchainAnchoring blockchainRestarted = StartNode(blockchain.LiteDatabase, blockchainAnchoringFork.ParentFake.Restart());

        string nameCase = "Restart resync, restart on a parent chain anchoring a fork below the tip";

        if (!blockchainRestarted.IsWithoutAccounts(nameCase, HEIGHT_TIP_FORK - COUNT_MAX_BLOCKS_STORED, ref message)
          || !blockchainRestarted.IsBlockStoredOldestAt(nameCase, HEIGHT_TIP_FORK - COUNT_MAX_BLOCKS_STORED, ref message))
          return false;

        blockchainRestarted.DeliverBlocks(blocksFork);

        return blockchainRestarted.IsWithoutAccounts("Restart resync, fork blocks delivered after the resync", HEIGHT_TIP_FORK, ref message);
      }

      static bool TryForkLedger(Ledger ledger, Wallet wallet, int heightFork, int heightTip, out List<Block> blocksFork, ref string message)
      {
        blocksFork = null;

        while (ledger.GetHeightTip() > heightFork)
          if (!ledger.TryRollBack($"Ledger rolled back to the fork at height {heightFork}", out _, ref message))
            return false;

        if (!TryInsertCoinbasesUpTo(ledger, wallet, CreateIDAccount(1002, 3), heightTip, ref message))
          return false;

        blocksFork = ledger.GetBlocksInserted().Where(b => b.Header.Height > heightFork).ToList();

        return true;
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
          ParentFake.MineBlock(CreateAnchor(blockLedger));
      }

      void MineBlocksUpTo(int height)
      {
        while (ParentFake.GetHeight() < height)
          ParentFake.MineBlock();
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

        return StartNode(CreateLiteDatabase(), new BlockchainParentFake(CreateLiteDatabase()));
      }

      static BlockchainAnchoring StartNode(LiteDatabase liteDatabase, BlockchainParentFake blockchainParent)
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
