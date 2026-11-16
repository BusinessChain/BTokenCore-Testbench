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
        && BlockchainAnchoring.TestRestartResync(Testbench.Wallet, ref message)
        && BlockchainAnchoring.TestCompactStore(Testbench.Wallet, ref message)
        && BlockchainAnchoring.TestCompactLoad(Testbench.Wallet, ref message)
        && BlockchainAnchoring.TestCompactServing(Testbench.Wallet, ref message)
        && BlockchainAnchoring.TestCompactInsertion(Testbench.Wallet, ref message);
    }

    class BlockchainAnchoring : BlockchainTestbench
    {
      const string NAME_TOKEN = "TestAnchoring";
      const int COUNT_MAX_BLOCKS_STORED = COUNT_SEGMENTS + BITCOIN_REORG_MARGIN;

      BlockchainAnchoring(LiteDatabase liteDatabase, BlockchainParentFake blockchainParent)
        : base(NAME_TOKEN, liteDatabase, blockchainParent)
      { }

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

          blockchain.ParentFake.MineAnchors([blockLedger]);

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

          blockchain.ParentFake.MineAnchors([blockLedger]);

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
        blockchain.ParentFake.MineAnchors([blocksLedger[0]]);

        string nameCaseKept = $"Anchor expiry, block at height 1 anchored {BITCOIN_REORG_MARGIN} Bitcoin blocks after its LockTime";

        if (!blockchain.IsSynchronized(ledger, nameCaseKept, 1, ref message)
          || !blockchain.IsRequestingNoBlock(nameCaseKept, ref message))
          return false;

        lockTime = blockchain.ParentFake.GetHeight();
        blockchain.StoreBlockAwaitingAnchor(blocksLedger[1], lockTime);
        blockchain.MineBlocksUpTo(lockTime + BITCOIN_REORG_MARGIN);
        blockchain.ParentFake.MineAnchors([blocksLedger[1]]);

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

      // Counter-tested 2026-11-14 03:01
      internal static bool TestParentRollback(Wallet wallet, ref string message)
      {
        Console.WriteLine("  4. Parent Rollback: Roll back with Bitcoin, re-anchor rolled-back blocks from the store.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        BlockchainAnchoring blockchain = StartNode();

        blockchain.ParentFake.MineAnchors(blocksLedger);
        blockchain.DeliverBlocks(blocksLedger);

        if (!blockchain.IsSynchronized(ledger, "Parent rollback, Ledger blocks anchored before the rollback", 6, ref message))
          return false;

        blockchain.ParentFake.RollBackTo(blockchain.ParentFake.GetHeight() - 3);

        if (!blockchain.IsSynchronized(ledger, "Parent rollback, Bitcoin chain rolled back by three blocks", 3, ref message))
          return false;

        blockchain.ParentFake.MineBlock([]);

        if (!blockchain.IsAtHeight("Parent rollback, replacing Bitcoin block without anchor", 3, ref message))
          return false;

        blockchain.ParentFake.MineAnchors(blocksLedger.Where(b => b.Header.Height > 3).ToList());

        string nameCaseReanchored = "Parent rollback, rolled-back blocks anchored again by replacing Bitcoin blocks";

        return blockchain.IsSynchronized(ledger, nameCaseReanchored, 6, ref message)
          && blockchain.IsRequestingNoBlock(nameCaseReanchored, ref message);
      }

      // Counter-tested 2026-11-14 03:01
      internal static bool TestRestartAnchored(Wallet wallet, ref string message)
      {
        Console.WriteLine("  5. Restart Anchored: Re-anchor stored blocks through the reloaded Bitcoin chain.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        BlockchainAnchoring blockchain = StartNode();

        blockchain.ParentFake.MineAnchors(blocksLedger);
        blockchain.DeliverBlocks(blocksLedger);

        BlockchainAnchoring blockchainRestarted = StartNode(blockchain.LiteDatabase, blockchain.ParentFake.Restart());

        string nameCase = "Restart anchored, restart with every stored block anchored";

        return blockchainRestarted.IsSynchronized(ledger, nameCase, 6, ref message)
          && blockchainRestarted.IsRequestingNoBlock(nameCase, ref message);
      }

      internal static bool TestRestartUnanchored(Wallet wallet, ref string message)
      {
        Console.WriteLine("  6. Restart Unanchored: Roll back unanchored tip blocks, keep unanchored blocks below anchored.");

        const int HEIGHT_BELOW_WINDOW = HEIGHT_TIP_BEYOND_WINDOW - COUNT_MAX_BLOCKS_STORED;
        const int HEIGHT_ANCHOR_LOST_LAST = HEIGHT_BELOW_WINDOW + 3;

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        BlockchainAnchoring blockchain = StartNode();

        blockchain.ParentFake.MineAnchors(blocksLedger);
        blockchain.DeliverBlocks(blocksLedger);

        int heightParent = blockchain.ParentFake.GetHeight();
        blockchain.ParentFake.DeleteBlocksStored(heightParent - 2, heightParent);

        BlockchainAnchoring blockchainRestarted = StartNode(blockchain.LiteDatabase, blockchain.ParentFake.Restart());

        if (!blockchainRestarted.IsSynchronized(ledger, "Restart unanchored, restart after the three latest Bitcoin blocks were lost", 3, ref message))
          return false;

        blockchainRestarted.ParentFake.MineAnchors(blocksLedger.Where(b => b.Header.Height > 3).ToList());

        string nameCaseReanchored = "Restart unanchored, rolled-back blocks anchored again after the restart";

        if (!blockchainRestarted.IsSynchronized(ledger, nameCaseReanchored, 6, ref message)
          || !blockchainRestarted.IsRequestingNoBlock(nameCaseReanchored, ref message))
          return false;

        BlockchainAnchoring blockchainGenesis = StartNode();

        blockchainGenesis.ParentFake.MineAnchors(blocksLedger);
        blockchainGenesis.DeliverBlocks(blocksLedger);
        blockchainGenesis.ParentFake.DeleteBlocksStored(0, 2);

        BlockchainAnchoring blockchainGenesisRestarted = StartNode(blockchainGenesis.LiteDatabase, blockchainGenesis.ParentFake.Restart());

        if (!blockchainGenesisRestarted.IsSynchronized(ledger, "Restart unanchored, restart under the genesis root after the Bitcoin blocks up to height 2 were lost", 6, ref message)
          || !TryInsertCoinbasesUpTo(ledger, wallet, CreateIDAccount(1000, 1), HEIGHT_TIP_BEYOND_WINDOW, ref message))
          return false;

        blocksLedger = ledger.GetBlocksInserted();
        BlockchainAnchoring blockchainServing = StartNode();
        blockchainServing.SyncBlockByBlock(blocksLedger);

        BlockchainAnchoring blockchainBeyondWindow = StartNode();

        blockchainBeyondWindow.ParentFake.MineAnchors(blocksLedger);
        blockchainBeyondWindow.FetchBlocksCompact(blockchainServing);
        blockchainBeyondWindow.DeliverBlocks(blocksLedger);

        string nameCaseBeyondWindow = $"Restart unanchored, compact blocks fetched up to height {HEIGHT_BELOW_WINDOW}, window delivered";

        if (!blockchainBeyondWindow.IsWithoutAccounts(nameCaseBeyondWindow, HEIGHT_TIP_BEYOND_WINDOW, ref message)
          || !blockchainBeyondWindow.IsBlockStoredOldestAt(nameCaseBeyondWindow, HEIGHT_BELOW_WINDOW + 1, ref message))
          return false;

        blockchainBeyondWindow.ParentFake.DeleteBlocksStored(0, HEIGHT_ANCHOR_LOST_LAST);

        BlockchainAnchoring blockchainBeyondWindowRestarted = StartNode(blockchainBeyondWindow.LiteDatabase, blockchainBeyondWindow.ParentFake.Restart());

        string nameCaseLostBelow = $"Restart unanchored, restart after the Bitcoin blocks up to height {HEIGHT_ANCHOR_LOST_LAST} were lost";

        if (!blockchainBeyondWindowRestarted.IsWithoutAccounts(nameCaseLostBelow, HEIGHT_TIP_BEYOND_WINDOW, ref message)
          || !blockchainBeyondWindowRestarted.IsBlockStoredOldestAt(nameCaseLostBelow, HEIGHT_BELOW_WINDOW + 1, ref message))
          return false;

        blockchainBeyondWindowRestarted.ParentFake.DeleteBlocksStored(0, int.MaxValue);

        BlockchainAnchoring blockchainParentLost = StartNode(blockchainBeyondWindowRestarted.LiteDatabase, blockchainBeyondWindowRestarted.ParentFake.Restart());

        string nameCaseLostAll = "Restart unanchored, restart of the node beyond the window after all Bitcoin blocks were lost";

        return blockchainParentLost.IsWithoutAccounts(nameCaseLostAll, HEIGHT_BELOW_WINDOW, ref message)
          && IsFieldEqual(nameCaseLostAll, "count of full blocks", 0, blockchainParentLost.BlocksStored.Count(), ref message)
          && blockchainParentLost.AreBlocksCompactMatching(nameCaseLostAll, blocksLedger.Where(b => b.Header.Height <= HEIGHT_BELOW_WINDOW).ToList(), ref message);
      }

      internal static bool TestWindowResync(Wallet wallet, ref string message)
      {
        Console.WriteLine("  7. Window Resync: Drop accounts, fetch compact blocks when a fork outruns the window.");

        const int HEIGHT_FORK = 3;
        const int HEIGHT_TIP = 6;
        const int HEIGHT_RESYNC = HEIGHT_TIP + COUNT_MAX_BLOCKS_STORED + 1;

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksMain = ledger.GetBlocksInserted();
        BlockchainAnchoring blockchain = StartNode();

        blockchain.ParentFake.MineAnchors(blocksMain);
        blockchain.DeliverBlocks(blocksMain);

        if (!blockchain.IsSynchronized(ledger, "Window resync, main chain anchored and delivered", HEIGHT_TIP, ref message)
          || !TryForkLedger(ledger, wallet, HEIGHT_FORK, HEIGHT_RESYNC + 1, out List<Block> blocksFork, ref message))
          return false;

        blockchain.ParentFake.MineAnchors(blocksFork.Where(b => b.Header.Height < HEIGHT_RESYNC).ToList());

        string nameCaseInside = $"Window resync, fork anchored up to {COUNT_MAX_BLOCKS_STORED} blocks above the tip";

        if (!blockchain.IsAtHeight(nameCaseInside, HEIGHT_TIP, ref message)
          || !blockchain.IsBlockStoredOldestAt(nameCaseInside, 1, ref message))
          return false;

        foreach (Block blockFork in blocksFork.Where(b => b.Header.Height >= HEIGHT_RESYNC))
        {
          string nameCase = $"Window resync, fork anchored up to height {blockFork.Header.Height}";

          blockchain.ParentFake.MineAnchors([blockFork]);

          if (!blockchain.IsBehindWindowAtFork(nameCase, HEIGHT_FORK, ref message))
            return false;
        }

        BlockchainAnchoring blockchainServing = StartNode();
        blockchainServing.SyncBlockByBlock(ledger.GetBlocksInserted());

        blockchain.FetchBlocksCompact(blockchainServing);
        blockchain.DeliverBlocks(blocksFork);

        const string NAME_CASE_DELIVERED = "Window resync, compact blocks fetched and fork blocks delivered after the resync";

        return blockchain.IsWithoutAccounts(NAME_CASE_DELIVERED, HEIGHT_RESYNC + 1, ref message)
          && blockchain.IsBlockStoredOldestAt(NAME_CASE_DELIVERED, HEIGHT_RESYNC + 1 - COUNT_MAX_BLOCKS_STORED + 1, ref message);
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

        blockchain.ParentFake.MineAnchors(blocksMain);
        blockchain.DeliverBlocks(blocksMain);

        if (!blockchain.IsSynchronized(ledger, "Restart resync, main chain anchored and delivered", HEIGHT_TIP, ref message)
          || !TryForkLedger(ledger, wallet, HEIGHT_FORK, HEIGHT_TIP_FORK, out List<Block> blocksFork, ref message))
          return false;

        BlockchainAnchoring blockchainServing = StartNode();
        blockchainServing.SyncBlockByBlock(ledger.GetBlocksInserted());

        BlockchainAnchoring blockchainRestarted = StartNode(blockchain.LiteDatabase, blockchainServing.ParentFake.Restart());

        if (!blockchainRestarted.IsBehindWindowAtFork("Restart resync, restart on a parent chain anchoring a fork below the tip", HEIGHT_FORK, ref message))
          return false;

        blockchainRestarted.FetchBlocksCompact(blockchainServing);
        blockchainRestarted.DeliverBlocks(blocksFork);

        const string NAME_CASE_DELIVERED = "Restart resync, compact blocks fetched and fork blocks delivered after the resync";

        return blockchainRestarted.IsWithoutAccounts(NAME_CASE_DELIVERED, HEIGHT_TIP_FORK, ref message)
          && blockchainRestarted.IsBlockStoredOldestAt(NAME_CASE_DELIVERED, HEIGHT_TIP_FORK - COUNT_MAX_BLOCKS_STORED + 1, ref message);
      }

      bool IsBehindWindowAtFork(string nameCase, int heightFork, ref string message)
      {
        if (!IsWithoutAccounts(nameCase, heightFork, ref message)
          || !IsFieldEqual(nameCase, "count of full blocks", 0, BlocksStored.Count(), ref message))
          return false;

        if (FetchHeaderBlockCompactNext(h => true) is Header headerRequested && headerRequested.Height == heightFork)
          return true;

        message = $"{nameCase}: the node does not request the compact blocks above the fork at height {heightFork}.";
        return false;
      }

      internal static bool TestCompactStore(Wallet wallet, ref string message)
      {
        Console.WriteLine("  9. Compact Store: Keep compact blocks below pruned window, drop rolled-back ones.");

        const int COUNT_ROLLED_BACK = 3;
        const string NAME_CASE = "Compact store, chain synchronized beyond the stored window";

        if (!TrySyncBeyondWindow(wallet, NAME_CASE, out Ledger ledger, out BlockchainAnchoring blockchain, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();

        if (!blockchain.IsBlockStoredOldestAt(NAME_CASE, HEIGHT_TIP_BEYOND_WINDOW - COUNT_MAX_BLOCKS_STORED + 1, ref message)
          || !blockchain.AreBlocksCompactMatching(NAME_CASE, blocksLedger, ref message))
          return false;

        blockchain.ParentFake.RollBackTo(blockchain.ParentFake.GetHeight() - COUNT_ROLLED_BACK);

        const string NAME_CASE_ROLLED_BACK = "Compact store, Bitcoin chain rolled back by three blocks";

        return blockchain.IsAtHeight(NAME_CASE_ROLLED_BACK, HEIGHT_TIP_BEYOND_WINDOW - COUNT_ROLLED_BACK, ref message)
          && IsFieldEqual(NAME_CASE_ROLLED_BACK, "compact blocks above the tip", 0, blockchain.BlocksCompact.Count(Query.GT(NAME_FIELD_ID, HEIGHT_TIP_BEYOND_WINDOW - COUNT_ROLLED_BACK)), ref message)
          && blockchain.AreBlocksCompactMatching(NAME_CASE_ROLLED_BACK, blocksLedger.Where(b => b.Header.Height <= HEIGHT_TIP_BEYOND_WINDOW - COUNT_ROLLED_BACK).ToList(), ref message);
      }

      internal static bool TestCompactLoad(Wallet wallet, ref string message)
      {
        Console.WriteLine("  10. Compact Load: Restart keeps whole history, announces it in order, redraws winners.");

        const string NAME_CASE = "Compact load, chain synchronized beyond the stored window";
        const string NAME_CASE_RESTARTED = "Compact load, restart of the synchronized chain";

        if (!TrySyncBeyondWindow(wallet, NAME_CASE, out Ledger ledger, out BlockchainAnchoring blockchain, ref message))
          return false;

        Dictionary<int, Header> headersBefore = Enumerable.Range(1, HEIGHT_TIP_BEYOND_WINDOW)
          .ToDictionary(h => h, h => blockchain.ChainRoot.HeaderTipBlockchain.GetHeaderAncestor(h));

        if (!IsFieldEqual(NAME_CASE, $"anchor winners at height {HEIGHT_ANCHORS_BEYOND_WINDOW}", 1, headersBefore[HEIGHT_ANCHORS_BEYOND_WINDOW].AnchorsWinner.Count, ref message))
          return false;

        BlockchainAnchoring blockchainRestarted = new(blockchain.LiteDatabase, blockchain.ParentFake.Restart());
        List<Block> blocksAnnounced = new();
        blockchainRestarted.OnBlockInserted += blocksAnnounced.Add;
        blockchainRestarted.Load();

        if (!blockchainRestarted.IsSynchronized(ledger, NAME_CASE_RESTARTED, HEIGHT_TIP_BEYOND_WINDOW, ref message)
          || !IsFieldEqual(NAME_CASE_RESTARTED, "height of the root header", 0, blockchainRestarted.ChainRoot.HeaderRoot.Height, ref message))
          return false;

        if (!blocksAnnounced.Select(b => b.Header.Height).SequenceEqual(Enumerable.Range(1, HEIGHT_TIP_BEYOND_WINDOW)))
        {
          message = $"{NAME_CASE_RESTARTED}: the loaded blocks were announced at heights {string.Join(",", blocksAnnounced.Select(b => b.Header.Height).Take(10))}..., expected 1 to {HEIGHT_TIP_BEYOND_WINDOW} in ascending order.";
          return false;
        }

        foreach (Block blockAnnounced in blocksAnnounced)
        {
          Header headerBefore = headersBefore[blockAnnounced.Header.Height];
          string nameBlock = $"{NAME_CASE_RESTARTED}, block announced at height {blockAnnounced.Header.Height}";

          if (!IsFieldEqual(nameBlock, "hash", headerBefore.Hash, blockAnnounced.Header.Hash, ref message)
            || !IsFieldEqual(nameBlock, "AnchorsWinner.Count", headerBefore.AnchorsWinner.Count, blockAnnounced.Header.AnchorsWinner.Count, ref message))
            return false;

          foreach (TXOutputTokenAnchor anchor in headerBefore.AnchorsWinner.Values)
            if (!blockAnnounced.Header.AnchorsWinner.TryGetValue(anchor.IDToken, out TXOutputTokenAnchor anchorReloaded)
              || !IsFieldEqual(nameBlock, $"HashBlockReferenced of IDToken {anchor.IDToken.ToHexString()}", anchor.HashBlockReferenced, anchorReloaded.HashBlockReferenced, ref message))
            {
              if (message == "")
                message = $"{nameBlock}: no anchor winner of IDToken {anchor.IDToken.ToHexString()}.";

              return false;
            }
        }

        const string NAME_CASE_UNLINKED = "Compact load, header one level short of Bitcoin";

        HeaderBToken headerUnlinked = new() { Hash = headersBefore[HEIGHT_ANCHORS_BEYOND_WINDOW].Hash };
        ParserToken.DrawAnchorsWinner(headerUnlinked, headersBefore[HEIGHT_ANCHORS_BEYOND_WINDOW].AnchorsWinner.Values, blockchain.CountLevelsBelowRoot);

        return IsFieldEqual(NAME_CASE_UNLINKED, "AnchorsWinner.Count", 0, headerUnlinked.AnchorsWinner.Count, ref message);
      }

      internal static bool TestCompactServing(Wallet wallet, ref string message)
      {
        Console.WriteLine("  11. Compact Serving: Serve compact blocks above a hash, ascending, within limits.");

        const int COUNT_BELOW_TIP = 50;
        const int COUNT_SIZE_LIMITED = 3;
        const string NAME_CASE = "Compact serving, chain synchronized beyond the stored window";

        if (!TrySyncBeyondWindow(wallet, NAME_CASE, out _, out BlockchainAnchoring blockchain, ref message))
          return false;

        Header headerTip = blockchain.ChainRoot.HeaderTipBlockchain;
        Header headerRoot = headerTip.GetHeaderAncestor(0);
        Header headerBelowTip = headerTip.GetHeaderAncestor(headerTip.Height - COUNT_BELOW_TIP);

        int sizeLimited = VarInt.GetBytes(COUNT_MAX_BLOCKS_COMPACT).Length
          + Enumerable.Range(1, COUNT_SIZE_LIMITED).Sum(h => blockchain.GetBytesBlockCompact(h).Length);

        return blockchain.IsBatchServed("Compact serving, batch above the root", headerRoot.Hash, COUNT_MAX_BLOCKS_COMPACT, SIZE_MAX_BLOCKS_COMPACT, 1, COUNT_MAX_BLOCKS_COMPACT, ref message)
          && blockchain.IsBatchServed($"Compact serving, batch above {COUNT_BELOW_TIP} blocks below the tip", headerBelowTip.Hash, COUNT_MAX_BLOCKS_COMPACT, SIZE_MAX_BLOCKS_COMPACT, headerBelowTip.Height + 1, COUNT_BELOW_TIP, ref message)
          && blockchain.IsBatchServed("Compact serving, batch limited by size", headerRoot.Hash, COUNT_MAX_BLOCKS_COMPACT, sizeLimited, 1, COUNT_SIZE_LIMITED, ref message)
          && blockchain.IsBatchRefused("Compact serving, unknown hash", new byte[32], COUNT_MAX_BLOCKS_COMPACT, SIZE_MAX_BLOCKS_COMPACT, ref message)
          && blockchain.IsBatchRefused("Compact serving, tip", headerTip.Hash, COUNT_MAX_BLOCKS_COMPACT, SIZE_MAX_BLOCKS_COMPACT, ref message);
      }

      internal static bool TestCompactInsertion(Wallet wallet, ref string message)
      {
        Console.WriteLine("  12. Compact Insertion: Append checked compact blocks behind the window, then download blocks.");

        const int HEIGHT_TARGET = HEIGHT_TIP_BEYOND_WINDOW - COUNT_MAX_BLOCKS_STORED;
        const string NAME_CASE = "Compact insertion, serving chain synchronized beyond the stored window";
        const string NAME_CASE_BEHIND = "Compact insertion, fresh node with every anchor mined";

        if (!TrySyncBeyondWindow(wallet, NAME_CASE, out Ledger ledger, out BlockchainAnchoring blockchainServing, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        BlockchainAnchoring blockchain = StartNode();
        List<Block> blocksAnnounced = new();
        blockchain.OnBlockInserted += blocksAnnounced.Add;

        blockchain.ParentFake.MineAnchors(blocksLedger);

        Header headerRoot = blockchain.ChainRoot.HeaderTipBlockchain;

        if (!IsFieldEqual(NAME_CASE_BEHIND, "height of the tip", 0, headerRoot.Height, ref message))
          return false;

        if (blockchain.FetchHeaderBlockMissingNext(h => true) != null || blockchain.FetchHeaderBlockCompactNext(h => true) != headerRoot)
        {
          message = $"{NAME_CASE_BEHIND}: the node requests a block or no compact blocks above its tip.";
          return false;
        }

        List<(Header header, List<TXOutputTokenAnchor> anchors)> blocksCompact = blockchain.ParseBlocksCompact(
          blockchainServing.GetBlocksCompactSerialized(headerRoot.Hash, COUNT_MAX_BLOCKS_COMPACT, SIZE_MAX_BLOCKS_COMPACT));

        List<(Header header, List<TXOutputTokenAnchor> anchors)> blocksCompactAnchorsWrong = blocksCompact
          .Take(HEIGHT_ANCHORS_BEYOND_WINDOW)
          .Select(b => b.header.Height == HEIGHT_ANCHORS_BEYOND_WINDOW ? (b.header, new List<TXOutputTokenAnchor>()) : b)
          .ToList();

        if (!IsRejected("Compact insertion, first compact block of another height", () => blockchain.InsertBlocksCompact(headerRoot.Hash, blocksCompact.Skip(1).ToList()), ref message)
          || !blockchain.IsAtHeight("Compact insertion, after the compact block of another height", 0, ref message)
          || !IsRejected($"Compact insertion, anchors left out at height {HEIGHT_ANCHORS_BEYOND_WINDOW}", () => blockchain.InsertBlocksCompact(headerRoot.Hash, blocksCompactAnchorsWrong), ref message)
          || !blockchain.IsAtHeight("Compact insertion, after the compact block with wrong anchors", HEIGHT_ANCHORS_BEYOND_WINDOW - 1, ref message))
          return false;

        blockchain.InsertBlocksCompact(headerRoot.Hash, blocksCompact);

        if (!blockchain.IsAtHeight("Compact insertion, answer to an outdated tip", HEIGHT_ANCHORS_BEYOND_WINDOW - 1, ref message))
          return false;

        Header headerTip = blockchain.ChainRoot.HeaderTipBlockchain;
        blockchain.InsertBlocksCompact(headerTip.Hash, blockchain.ParseBlocksCompact(blockchainServing.GetBlocksCompactSerialized(headerTip.Hash, COUNT_MAX_BLOCKS_COMPACT, SIZE_MAX_BLOCKS_COMPACT)));

        const string NAME_CASE_TARGET = "Compact insertion, compact blocks up to the tallest tip minus the stored window";

        if (!blockchain.IsAtHeight(NAME_CASE_TARGET, HEIGHT_TARGET, ref message)
          || !blockchain.AreBlocksCompactMatching(NAME_CASE_TARGET, blocksLedger.Where(b => b.Header.Height <= HEIGHT_TARGET).ToList(), ref message))
          return false;

        if (!blocksAnnounced.Select(b => b.Header.Height).SequenceEqual(Enumerable.Range(1, HEIGHT_TARGET)))
        {
          message = $"{NAME_CASE_TARGET}: the compact blocks were announced at heights {string.Join(",", blocksAnnounced.Select(b => b.Header.Height))}, expected 1 to {HEIGHT_TARGET}.";
          return false;
        }

        Header headerAnchors = blockchain.ChainRoot.HeaderTipBlockchain.GetHeaderAncestor(HEIGHT_ANCHORS_BEYOND_WINDOW);
        Header headerAnchorsServing = blockchainServing.ChainRoot.HeaderTipBlockchain.GetHeaderAncestor(HEIGHT_ANCHORS_BEYOND_WINDOW);

        if (!IsFieldEqual(NAME_CASE_TARGET, $"AnchorsWinner.Count at height {HEIGHT_ANCHORS_BEYOND_WINDOW}", headerAnchorsServing.AnchorsWinner.Count, headerAnchors.AnchorsWinner.Count, ref message)
          || !IsFieldEqual(NAME_CASE_TARGET, $"HashBlockReferenced of the anchor winner at height {HEIGHT_ANCHORS_BEYOND_WINDOW}", headerAnchorsServing.AnchorsWinner[ID_TOKEN].HashBlockReferenced, headerAnchors.AnchorsWinner[ID_TOKEN].HashBlockReferenced, ref message))
          return false;

        if (blockchain.FetchHeaderBlockCompactNext(h => true) != null)
        {
          message = $"{NAME_CASE_TARGET}: the node still requests compact blocks.";
          return false;
        }

        blockchain.DeliverBlocks(blocksLedger);

        return blockchain.IsWithoutAccounts("Compact insertion, window blocks delivered after the compact blocks", HEIGHT_TIP_BEYOND_WINDOW, ref message);
      }

      List<(Header header, List<TXOutputTokenAnchor> anchors)> ParseBlocksCompact(byte[] payload)
      {
        int index = 0;
        int countBlocksCompact = VarInt.GetInt(payload, ref index);

        List<(Header header, List<TXOutputTokenAnchor> anchors)> blocksCompact = new();

        for (int i = 0; i < countBlocksCompact; i++)
        {
          Header header = Parser.ParseHeader(payload, ref index);
          blocksCompact.Add((header, ParserToken.ParseAnchors(payload, ref index)));
        }

        return blocksCompact;
      }

      byte[] GetBytesBlockCompact(int height)
      {
        return BlocksCompact.FindById(height)[NAME_FIELD_BLOCK_BYTES].AsBinary;
      }

      bool IsBatchServed(string nameCase, byte[] hash, int countMax, int sizeMax, int heightFirst, int countExpected, ref string message)
      {
        if (GetBlocksCompactSerialized(hash, countMax, sizeMax) is not byte[] payload)
        {
          message = $"{nameCase}: no batch served.";
          return false;
        }

        byte[] payloadExpected = [
          .. VarInt.GetBytes(countExpected),
          .. Enumerable.Range(heightFirst, countExpected).SelectMany(GetBytesBlockCompact)];

        return IsFieldEqual(nameCase, "payload", payloadExpected, payload, ref message);
      }

      bool IsBatchRefused(string nameCase, byte[] hash, int countMax, int sizeMax, ref string message)
      {
        if (GetBlocksCompactSerialized(hash, countMax, sizeMax) == null)
          return true;

        message = $"{nameCase}: a batch was served, expected none.";
        return false;
      }

      const int COUNT_MAX_BLOCKS_COMPACT = 2000;
      const int SIZE_MAX_BLOCKS_COMPACT = 4_000_000;
      const int HEIGHT_ANCHORS_BEYOND_WINDOW = 7;
      const int HEIGHT_TIP_BEYOND_WINDOW = COUNT_MAX_BLOCKS_STORED + 7;

      static bool TrySyncBeyondWindow(Wallet wallet, string nameCase, out Ledger ledger, out BlockchainAnchoring blockchain, ref string message)
      {
        blockchain = null;

        if (!TryCreateLedger(wallet, out ledger, ref message)
          || !ledger.TryInsertBlock("spend with an eligible anchor", [ledger.CreateCoinbase(wallet, CreateIDAccount(1000, 1), FEE), ledger.CreateTXSpend(wallet, CreateIDAccount(1001, 2), VALUE_SPEND, t => AddAnchor(t, HEIGHT_ANCHORS_BEYOND_WINDOW - 1))], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledger, wallet, CreateIDAccount(1000, 1), HEIGHT_TIP_BEYOND_WINDOW, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        blockchain = StartNode();
        blockchain.SyncBlockByBlock(blocksLedger);

        return blockchain.IsSynchronized(ledger, nameCase, HEIGHT_TIP_BEYOND_WINDOW, ref message)
          && IsFieldEqual(nameCase, $"eligible anchors at height {HEIGHT_ANCHORS_BEYOND_WINDOW}", 1, ParserToken.EnumerateAnchorsEligible(blocksLedger.Single(b => b.Header.Height == HEIGHT_ANCHORS_BEYOND_WINDOW).TXs, HEIGHT_ANCHORS_BEYOND_WINDOW).Count(), ref message);
      }

      void SyncBlockByBlock(List<Block> blocksLedger)
      {
        foreach (Block blockLedger in blocksLedger)
        {
          ParentFake.MineAnchors([blockLedger]);
          DeliverBlocks(blocksLedger);
        }
      }

      void FetchBlocksCompact(BlockchainAnchoring blockchainServing)
      {
        while (FetchHeaderBlockCompactNext(h => true) is Header header)
          InsertBlocksCompact(header.Hash, ParseBlocksCompact(blockchainServing.GetBlocksCompactSerialized(header.Hash, COUNT_MAX_BLOCKS_COMPACT, SIZE_MAX_BLOCKS_COMPACT)));
      }

      bool AreBlocksCompactMatching(string nameCase, List<Block> blocks, ref string message)
      {
        foreach (Block block in blocks)
        {
          string nameBlock = $"{nameCase}, compact block at height {block.Header.Height}";

          if (BlocksCompact.FindById(block.Header.Height) is not BsonDocument documentBlock)
          {
            message = $"{nameBlock}: missing.";
            return false;
          }

          byte[] bytesBlock = documentBlock[NAME_FIELD_BLOCK_BYTES].AsBinary;
          int index = 0;

          HeaderBToken header = (HeaderBToken)Parser.ParseHeader(bytesBlock, ref index);
          byte[] hashAnchors = ParserToken.ComputeHashAnchors(ParserToken.ParseAnchors(bytesBlock, ref index));

          if (!IsFieldEqual(nameBlock, "header hash", block.Header.Hash, header.Hash, ref message)
            || !IsFieldEqual(nameBlock, "hash of the stored anchors", ParserToken.ComputeHashAnchors(ParserToken.EnumerateAnchorsEligible(block.TXs, block.Header.Height)), hashAnchors, ref message)
            || !IsFieldEqual(nameBlock, "HashAnchors", header.HashAnchors, hashAnchors, ref message))
            return false;
        }

        return true;
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

      void MineBlocksUpTo(int height)
      {
        while (ParentFake.GetHeight() < height)
          ParentFake.MineBlock([]);
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
    }
  }
}
