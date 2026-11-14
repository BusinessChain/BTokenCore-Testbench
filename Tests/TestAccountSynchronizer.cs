using System.Security.Cryptography;

using BTokenCore;

using LiteDB;

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  class TestAccountSynchronizer : Test_Testbench
  {
    internal TestAccountSynchronizer(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      return BlockchainSynchronizing.TestFreshStart(Testbench.Wallet, ref message)
        && BlockchainSynchronizing.TestRollbackAtTip(Testbench.Wallet, ref message)
        && BlockchainSynchronizing.TestRestartRollback(Testbench.Wallet, ref message)
        && BlockchainSynchronizing.TestReplayFromRoot(Testbench.Wallet, ref message)
        && BlockchainSynchronizing.TestSegmentInstallation(Testbench.Wallet, ref message)
        && BlockchainSynchronizing.TestReplayRejection(Testbench.Wallet, ref message)
        && BlockchainSynchronizing.TestTXPoolGating(Testbench.Wallet, ref message)
        && BlockchainSynchronizing.TestRollbackGating(Testbench.Wallet, ref message)
        && BlockchainSynchronizing.TestConfirmReopen(Testbench.Wallet, ref message)
        && BlockchainSynchronizing.TestPruneConfirmed(Testbench.Wallet, ref message)
        && BlockchainSynchronizing.TestRestartReload(Testbench.Wallet, ref message)
        && BlockchainSynchronizing.TestReplayConfirmation(Testbench.Wallet, ref message)
        && BlockchainSynchronizing.TestRejectionReopen(Testbench.Wallet, ref message)
        && BlockchainSynchronizing.TestConflictReopen(Testbench.Wallet, ref message)
        && BlockchainSynchronizing.TestAnchorCount(Testbench.Wallet, ref message);
    }

    class BlockchainSynchronizing : BlockchainTestbench
    {
      const string NAME_TOKEN = "TestSynchronizer";
      const int HEIGHT_TIP_WINDOW = 2060;
      const int HEIGHT_INSTALLED = HEIGHT_TIP_WINDOW - BITCOIN_REORG_MARGIN;

      int HeightAnchored;

      BlockchainSynchronizing(LiteDatabase liteDatabase, Blockchain blockchainParent, int heightAnchored)
        : base(NAME_TOKEN, liteDatabase, blockchainParent)
      {
        HeightAnchored = heightAnchored;
      }

      // Counter-tested 2026-11-13 17:16
      internal static bool TestFreshStart(Wallet wallet, ref string message)
      {
        Console.WriteLine("  1. Fresh Start: Install empty accounts at height 0, follow inserted blocks.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);

        if (!blockchain.IsSynchronized(ledger, "Account synchronizer, fresh start before any block", 0, ref message))
          return false;

        foreach (Block blockLedger in ledger.GetBlocksInserted())
        {
          blockchain.InsertBlocks([blockLedger]);

          if (!blockchain.IsSynchronized(ledger, $"Account synchronizer, fresh start after the block at height {blockLedger.Header.Height}", blockLedger.Header.Height, ref message))
            return false;
        }

        return true;
      }

      // Counter-tested 2026-11-13 17:16
      internal static bool TestRollbackAtTip(Wallet wallet, ref string message)
      {
        Console.WriteLine("  2. Rollback At Tip: Roll back accounts with blocks, then insert a replacing fork.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);
        blockchain.InsertBlocks(ledger.GetBlocksInserted());
        blockchain.RollBackTo(3);

        if (!blockchain.IsSynchronized(ledger, "Account synchronizer, rollback to height 3", 3, ref message))
          return false;

        while (ledger.GetHeightTip() > 3)
          if (!ledger.TryRollBack("rollback at tip", out _, ref message))
            return false;

        byte[] iDMinerFork = CreateIDAccount(1002, 3);
        byte[] iDReceiverFork = CreateIDAccount(1003, 4);

        if (!ledger.TryInsertBlock("spend of the replacing fork", [ledger.CreateCoinbase(wallet, iDMinerFork, FEE), ledger.CreateTXSpend(wallet, iDReceiverFork, VALUE_SPEND, t => { })], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledger, wallet, iDMinerFork, 6, ref message))
          return false;

        blockchain.InsertBlocks(ledger.GetBlocksInserted().Where(b => b.Header.Height > 3).ToList());

        return blockchain.IsSynchronized(ledger, "Account synchronizer, replacing fork inserted after the rollback to height 3", 6, ref message);
      }

      // Counter-tested 2026-11-14 03:01
      internal static bool TestRestartRollback(Wallet wallet, ref string message)
      {
        Console.WriteLine("  3. Restart Rollback: Restart rolls accounts back with unanchored blocks before replaying.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);
        blockchain.InsertBlocks(ledger.GetBlocksInserted());

        BlockchainSynchronizing blockchainRestarted = StartNode(blockchain.LiteDatabase, 3);

        if (!blockchainRestarted.IsSynchronized(ledger, "Account synchronizer, restart with blocks anchored up to height 3", 3, ref message))
          return false;

        blockchainRestarted.InsertBlocks(ledger.GetBlocksInserted().Where(b => b.Header.Height > 3).ToList());

        return blockchainRestarted.IsSynchronized(ledger, "Account synchronizer, blocks above height 3 inserted again after the restart", 6, ref message);
      }

      // Counter-tested 2026-11-13 17:16
      internal static bool TestReplayFromRoot(Wallet wallet, ref string message)
      {
        Console.WriteLine("  4. Replay From Root: Replay all stored blocks after dropped or unknown accounts.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);
        blockchain.InsertBlocks(ledger.GetBlocksInserted());
        blockchain.Synchronizer.Drop();

        BlockchainSynchronizing blockchainRestarted = StartNode(blockchain.LiteDatabase, int.MaxValue);

        if (!blockchainRestarted.IsSynchronized(ledger, "Account synchronizer, restart after the account database was dropped", 6, ref message))
          return false;

        blockchainRestarted.BlocksStored.Delete(6);

        return StartNode(blockchainRestarted.LiteDatabase, int.MaxValue)
          .IsSynchronized(ledger, "Account synchronizer, restart without the stored block the accounts applied last", 5, ref message);
      }

      // Counter-tested 2026-11-13 17:16
      internal static bool TestReplayRejection(Wallet wallet, ref string message)
      {
        Console.WriteLine("  8. Replay Rejection: Reject a failing stored block during the replay, then follow.");

        const int HEIGHT_FORGED = 4;

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        TXBToken tXSpendInvalid = ledger.CreateTXSpend(wallet, CreateIDAccount(1001, 2), VALUE_SPEND, t => t.Nonce = 99);

        BlockchainSynchronizing blockchain = StartNodeWithoutAccounts(blocksLedger.Where(b => b.Header.Height < HEIGHT_FORGED).ToList());
        blockchain.InsertBlocks(ForgeBlocks(blocksLedger, HEIGHT_FORGED, (tXs, header) => tXs.Add(tXSpendInvalid)));

        if (!blockchain.IsWithoutAccounts("Account synchronizer, forged blocks stored without accounts", 6, ref message))
          return false;

        BlockchainSynchronizing blockchainRestarted = StartNode(blockchain.LiteDatabase, int.MaxValue);

        if (!blockchainRestarted.IsSynchronized(ledger, $"Account synchronizer, replay from the root rejecting the block at height {HEIGHT_FORGED}", HEIGHT_FORGED - 1, ref message))
          return false;

        blockchainRestarted.InsertBlocks(blocksLedger.Where(b => b.Header.Height >= HEIGHT_FORGED).ToList());

        return blockchainRestarted.IsSynchronized(ledger, "Account synchronizer, Ledger blocks inserted after the rejected block", 6, ref message);
      }

      // Counter-tested 2026-11-13 17:16
      internal static bool TestTXPoolGating(Wallet wallet, ref string message)
      {
        Console.WriteLine("  9. TX Pool Gating: Reject TXs without accounts, reload owned TXs after replay.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        TXBToken tXSpend = ledger.CreateTXSpend(wallet, CreateIDAccount(1001, 2), VALUE_SPEND, t => { });

        BlockchainSynchronizing blockchain = StartNodeWithoutAccounts(ledger.GetBlocksInserted());

        if (!IsRejected("Account synchronizer, TX submitted without accounts", () => blockchain.SubmitTX(tXSpend.TXRaw), ref message))
          return false;

        BlockchainSynchronizing blockchainRestarted = StartNode(blockchain.LiteDatabase, int.MaxValue);

        if (!blockchainRestarted.IsSynchronized(ledger, "Account synchronizer, restart after the TX submitted without accounts", 6, ref message)
          || !blockchainRestarted.IsInPool("restart after the TX submitted without accounts", tXSpend.Hash, false, ref message)
          || !IsAccepted("Account synchronizer, TX submitted with accounts at the tip", () => blockchainRestarted.SubmitTX(tXSpend.TXRaw), ref message)
          || !blockchainRestarted.IsInPool("TX submitted with accounts at the tip", tXSpend.Hash, true, ref message))
          return false;

        blockchainRestarted.Synchronizer.Drop();

        if (!blockchainRestarted.IsInPool("account database dropped", tXSpend.Hash, false, ref message))
          return false;

        BlockchainSynchronizing blockchainReloaded = StartNode(blockchainRestarted.LiteDatabase, int.MaxValue);

        return blockchainReloaded.IsSynchronized(ledger, "Account synchronizer, restart after the account database was dropped", 6, ref message)
          && blockchainReloaded.IsInPool("owned TX reloaded after the replay", tXSpend.Hash, true, ref message);
      }

      // Counter-tested 2026-11-14 03:01
      internal static bool TestRollbackGating(Wallet wallet, ref string message)
      {
        Console.WriteLine("  10. Rollback Gating: Return rolled-back TXs to the pool only with accounts.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        byte[] hashSpend = blocksLedger.Single(b => b.Header.Height == 2).TXs[1].Hash;

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);
        blockchain.InsertBlocks(blocksLedger);
        blockchain.RollBackTo(1);

        if (!blockchain.IsSynchronized(ledger, "Account synchronizer, rollback to height 1 with accounts at the tip", 1, ref message)
          || !blockchain.IsInPool("rollback to height 1 with accounts at the tip", hashSpend, true, ref message))
          return false;

        BlockchainSynchronizing blockchainWithoutAccounts = StartNodeWithoutAccounts(blocksLedger);
        blockchainWithoutAccounts.RollBackTo(1);

        if (!blockchainWithoutAccounts.IsWithoutAccounts("Account synchronizer, rollback to height 1 without accounts", 1, ref message)
          || !blockchainWithoutAccounts.IsInPool("rollback to height 1 without accounts", hashSpend, false, ref message))
          return false;

        BlockchainSynchronizing blockchainReplaying = StartNode(CreateLiteDatabase(), int.MaxValue);
        blockchainReplaying.InsertBlocks(blocksLedger);

        BlockchainSynchronizing blockchainRestarted = StartNode(blockchainReplaying.LiteDatabase, 1);

        return blockchainRestarted.IsSynchronized(ledger, "Account synchronizer, restart rolling back unanchored blocks while the replay is pending", 1, ref message)
          && blockchainRestarted.IsInPool("restart rolling back unanchored blocks while the replay is pending", hashSpend, false, ref message);
      }

      // Counter-tested 2026-11-14 04:59
      internal static bool TestConfirmReopen(Wallet wallet, ref string message)
      {
        Console.WriteLine("  11. Confirm Reopen: Mark only confirmed own TX, reopen it on rollback.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        while (ledger.GetHeightTip() > 2)
          if (!ledger.TryRollBack("own TX lifecycle", out _, ref message))
            return false;

        TXBToken tXSpend = (TXBToken)ledger.GetBlocksInserted().Single(b => b.Header.Height == 2).TXs[1];
        TXBToken tXSpendNext = ledger.CreateTXSpend(wallet, CreateIDAccount(1001, 2), VALUE_SPEND, t => t.Nonce = tXSpend.Nonce + 1);
        byte[] iDMiner = CreateIDAccount(1000, 1);
        Wallet walletForeign = CreateWallet();
        Wallet walletOwn = CreateWallet();

        if (!ledger.TryInsertBlock("spend with the next Nonce beside a coinbase creating an own account", [ledger.CreateCoinbase(wallet, iDMiner, FEE, new TXOutputP2PKH(VALUE_FUNDING, walletOwn.Hash160KeyPublicReceiving)), tXSpendNext], isValidExpected: true, null, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        TXBToken tXOwnCreatedEarlier = ledger.CreateTXSpend(walletOwn, CreateIDAccount(1001, 2), VALUE_SPEND, t => { });

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);
        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height == 1).ToList());

        if (!IsAccepted("Own TX lifecycle, spend submitted at height 1", () => blockchain.SubmitTX(tXSpend.TXRaw), ref message)
          || !blockchain.IsTXOwnedAtHeight("spend submitted at height 1", tXSpend.Hash, 0, ref message)
          || !IsAccepted("Own TX lifecycle, spend with the next Nonce submitted at height 1", () => blockchain.SubmitTX(tXSpendNext.TXRaw), ref message))
          return false;

        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height == 2).ToList());

        if (!blockchain.IsSynchronized(ledger, "Own TX lifecycle, block confirming the spend at height 2", 2, ref message)
          || !blockchain.IsTXOwnedAtHeight("block confirming the spend at height 2", tXSpend.Hash, 2, ref message)
          || !blockchain.IsTXOwnedAtHeight("block confirming the spend, leaving the next Nonce open", tXSpendNext.Hash, 0, ref message))
          return false;

        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height == 3).ToList());

        if (!blockchain.IsSynchronized(ledger, "Own TX lifecycle, block confirming the next Nonce at height 3", 3, ref message)
          || !blockchain.IsTXOwnedAtHeight("block confirming the next Nonce at height 3", tXSpendNext.Hash, 3, ref message)
          || !IsAccepted("Own TX lifecycle, spend of the own account created at height 3", () => blockchain.SubmitTX(tXOwnCreatedEarlier.TXRaw), ref message))
          return false;

        blockchain.RollBackTo(2);

        if (!blockchain.IsSynchronized(ledger, "Own TX lifecycle, rollback of the block confirming the next Nonce", 2, ref message)
          || !blockchain.IsTXOwnedAtHeight("rollback of the block confirming the next Nonce", tXSpendNext.Hash, 0, ref message)
          || !blockchain.IsTXOwnedAtHeight("rollback keeping the spend confirmed below", tXSpend.Hash, 2, ref message))
          return false;

        blockchain.RollBackTo(1);

        if (!blockchain.IsSynchronized(ledger, "Own TX lifecycle, rollback of the confirming block", 1, ref message)
          || !blockchain.IsTXOwnedAtHeight("rollback of the confirming block", tXSpend.Hash, 0, ref message))
          return false;

        while (ledger.GetHeightTip() > 1)
          if (!ledger.TryRollBack("own TX lifecycle", out _, ref message))
            return false;

        TXBToken tXCoinbaseFork = ledger.CreateCoinbase(wallet, iDMiner, 0, new TXOutputP2PKH(VALUE_FUNDING, walletForeign.Hash160KeyPublicReceiving), new TXOutputP2PKH(VALUE_FUNDING, walletOwn.Hash160KeyPublicReceiving));
        tXCoinbaseFork.HeightAccountCreated = tXSpend.HeightAccountCreated;
        tXCoinbaseFork.Nonce = tXSpend.Nonce;
        tXCoinbaseFork.SerializeUnsigned();

        if (!ledger.TryInsertBlock("coinbase-only block of the fork with the fields of an own spend creating two accounts", [tXCoinbaseFork], isValidExpected: true, null, ref message))
          return false;

        TXBToken tXForeign = ledger.CreateTXSpend(walletForeign, CreateIDAccount(1001, 2), VALUE_SPEND, t => { });
        TXBToken tXOwnOpen = ledger.CreateTXSpend(walletOwn, CreateIDAccount(1001, 2), VALUE_SPEND, t => { });

        if (!ledger.TryInsertBlock("spend of the fork beside a foreign spend", [ledger.CreateCoinbase(wallet, iDMiner, 2 * FEE), tXSpend, tXForeign], isValidExpected: true, null, ref message)
          || !ledger.TryInsertBlock("spend of the own account recreated in the fork", [ledger.CreateCoinbase(wallet, iDMiner, FEE), tXOwnOpen], isValidExpected: true, null, ref message))
          return false;

        List<Block> blocksFork = ledger.GetBlocksInserted();

        blockchain.InsertBlocks(blocksFork.Where(b => b.Header.Height == 2).ToList());

        if (!blockchain.IsSynchronized(ledger, "Own TX lifecycle, coinbase-only block of the fork at height 2", 2, ref message)
          || !blockchain.IsTXOwnedAtHeight("coinbase-only block of the fork at height 2", tXSpend.Hash, 0, ref message)
          || !IsAccepted("Own TX lifecycle, spend with the Nonce of a foreign spend submitted at height 2", () => blockchain.SubmitTX(tXOwnOpen.TXRaw), ref message))
          return false;

        blockchain.InsertBlocks(blocksFork.Where(b => b.Header.Height == 3).ToList());

        if (!blockchain.IsSynchronized(ledger, "Own TX lifecycle, fork confirming the spend at height 3", 3, ref message)
          || !blockchain.IsTXOwnedAtHeight("fork confirming the spend at height 3", tXSpend.Hash, 3, ref message)
          || !blockchain.IsTXOwnedAtHeight("fork confirming a foreign spend with the Nonce of an open own spend", tXOwnOpen.Hash, 0, ref message))
          return false;

        blockchain.InsertBlocks(blocksFork.Where(b => b.Header.Height == 4).ToList());

        if (!blockchain.IsSynchronized(ledger, "Own TX lifecycle, fork confirming the spend of the recreated own account at height 4", 4, ref message)
          || !blockchain.IsTXOwnedAtHeight("fork confirming the spend of the recreated own account at height 4", tXOwnOpen.Hash, 4, ref message)
          || !blockchain.IsTXOwnedAtHeight("fork confirming a spend with the Nonce of an own spend from an earlier account creation", tXOwnCreatedEarlier.Hash, 0, ref message, isInPoolExpected: false))
          return false;

        blockchain.RollBackTo(2);

        return blockchain.IsSynchronized(ledger, "Own TX lifecycle, rollback of the fork's confirming block", 2, ref message)
          && blockchain.IsTXOwnedAtHeight("rollback of the fork's confirming block", tXSpend.Hash, 0, ref message);
      }

      // Counter-tested 2026-11-14 03:35
      internal static bool TestPruneConfirmed(Wallet wallet, ref string message)
      {
        Console.WriteLine("  12. Prune Confirmed: Delete a confirmed own TX after the stored window.");

        const int COUNT_MAX_BLOCKS_STORED = COUNT_SEGMENTS + BITCOIN_REORG_MARGIN;
        const int HEIGHT_CONFIRMED = 2;
        const int HEIGHT_PRUNE = HEIGHT_CONFIRMED + COUNT_MAX_BLOCKS_STORED;

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message)
          || !TryInsertCoinbasesUpTo(ledger, wallet, CreateIDAccount(1000, 1), HEIGHT_PRUNE, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        TXBToken tXSpend = (TXBToken)blocksLedger.Single(b => b.Header.Height == HEIGHT_CONFIRMED).TXs[1];
        TXBToken tXOpen = ledger.CreateTXSpend(wallet, CreateIDAccount(1001, 2), VALUE_SPEND, t => { });

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);
        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height < HEIGHT_CONFIRMED).ToList());

        if (!IsAccepted("Own TX lifecycle, spend submitted before its confirming block", () => blockchain.SubmitTX(tXSpend.TXRaw), ref message))
          return false;

        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height >= HEIGHT_CONFIRMED && b.Header.Height < HEIGHT_PRUNE).ToList());

        string nameCaseKept = $"last block at height {HEIGHT_PRUNE - 1} before the prune";

        if (!blockchain.IsSynchronized(ledger, $"Own TX lifecycle, {nameCaseKept}", HEIGHT_PRUNE - 1, ref message)
          || !blockchain.IsTXOwnedAtHeight(nameCaseKept, tXSpend.Hash, HEIGHT_CONFIRMED, ref message)
          || !IsAccepted($"Own TX lifecycle, open spend submitted at height {HEIGHT_PRUNE - 1}", () => blockchain.SubmitTX(tXOpen.TXRaw), ref message))
          return false;

        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height == HEIGHT_PRUNE).ToList());

        string nameCasePruned = $"block at height {HEIGHT_PRUNE} pruning the confirmed spend";

        return blockchain.IsSynchronized(ledger, $"Own TX lifecycle, {nameCasePruned}", HEIGHT_PRUNE, ref message)
          && blockchain.IsTXOwnedAtHeight(nameCasePruned, tXSpend.Hash, null, ref message)
          && blockchain.IsTXOwnedAtHeight($"block at height {HEIGHT_PRUNE} keeping the open spend", tXOpen.Hash, 0, ref message);
      }

      // Counter-tested 2026-11-14 03:01
      internal static bool TestRestartReload(Wallet wallet, ref string message)
      {
        Console.WriteLine("  13. Restart Reload: Reload unconfirmed own TX after restart, keep confirmed one.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        TXBToken tXSpend = (TXBToken)blocksLedger.Single(b => b.Header.Height == 2).TXs[1];

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);
        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height == 1).ToList());

        if (!IsAccepted("Own TX lifecycle, spend submitted at height 1", () => blockchain.SubmitTX(tXSpend.TXRaw), ref message))
          return false;

        BlockchainSynchronizing blockchainRestarted = StartNode(blockchain.LiteDatabase, int.MaxValue);

        if (!blockchainRestarted.IsSynchronized(ledger, "Own TX lifecycle, restart with the unconfirmed spend", 1, ref message)
          || !blockchainRestarted.IsTXOwnedAtHeight("restart with the unconfirmed spend", tXSpend.Hash, 0, ref message))
          return false;

        blockchainRestarted.InsertBlocks(blocksLedger.Where(b => b.Header.Height == 2).ToList());

        BlockchainSynchronizing blockchainRestartedAgain = StartNode(blockchainRestarted.LiteDatabase, int.MaxValue);

        return blockchainRestartedAgain.IsSynchronized(ledger, "Own TX lifecycle, restart with the confirmed spend", 2, ref message)
          && blockchainRestartedAgain.IsTXOwnedAtHeight("restart with the confirmed spend", tXSpend.Hash, 2, ref message);
      }

      // Counter-tested 2026-11-14 03:01
      internal static bool TestReplayConfirmation(Wallet wallet, ref string message)
      {
        Console.WriteLine("  14. Replay Confirmation: Mark own TX confirmed by a block replayed after restart.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        TXBToken tXSpend = (TXBToken)blocksLedger.Single(b => b.Header.Height == 2).TXs[1];

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);
        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height == 1).ToList());

        if (!IsAccepted("Own TX lifecycle, spend submitted at height 1", () => blockchain.SubmitTX(tXSpend.TXRaw), ref message))
          return false;

        blockchain.Synchronizer.Drop();
        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height > 1).ToList());

        if (!blockchain.IsWithoutAccounts("Own TX lifecycle, confirming block inserted without accounts", 6, ref message))
          return false;

        BlockchainSynchronizing blockchainRestarted = StartNode(blockchain.LiteDatabase, int.MaxValue);

        return blockchainRestarted.IsSynchronized(ledger, "Own TX lifecycle, replay of the confirming block after the restart", 6, ref message)
          && blockchainRestarted.IsTXOwnedAtHeight("replay of the confirming block after the restart", tXSpend.Hash, 2, ref message);
      }

      // Counter-tested 2026-11-14 03:35
      internal static bool TestRejectionReopen(Wallet wallet, ref string message)
      {
        Console.WriteLine("  16. Rejection Reopen: Reopen own TX confirmed by a block rejected in replay.");

        const int HEIGHT_FORGED = 2;

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        while (ledger.GetHeightTip() > HEIGHT_FORGED)
          if (!ledger.TryRollBack("own TX lifecycle", out _, ref message))
            return false;

        byte[] iDMiner = CreateIDAccount(1000, 1);

        if (!ledger.TryInsertBlock("second spend above the forged block", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, CreateIDAccount(1001, 2), VALUE_SPEND, t => { })], isValidExpected: true, null, ref message)
          || !TryInsertCoinbasesUpTo(ledger, wallet, iDMiner, 6, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        TXBToken tXSpend = (TXBToken)blocksLedger.Single(b => b.Header.Height == HEIGHT_FORGED).TXs[1];
        TXBToken tXSpendAbove = (TXBToken)blocksLedger.Single(b => b.Header.Height == HEIGHT_FORGED + 1).TXs[1];
        TXBToken tXSpendInvalid = ledger.CreateTXSpend(wallet, CreateIDAccount(1001, 2), VALUE_SPEND, t => t.Nonce = 99);

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);
        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height < HEIGHT_FORGED).ToList());

        if (!IsAccepted("Own TX lifecycle, spend submitted at height 1", () => blockchain.SubmitTX(tXSpend.TXRaw), ref message)
          || !IsAccepted("Own TX lifecycle, second spend submitted at height 1", () => blockchain.SubmitTX(tXSpendAbove.TXRaw), ref message))
          return false;

        blockchain.Synchronizer.Drop();
        blockchain.InsertBlocks(ForgeBlocks(blocksLedger, HEIGHT_FORGED, (tXs, header) => tXs.Add(tXSpendInvalid)));

        if (!blockchain.IsWithoutAccounts("Own TX lifecycle, forged blocks stored without accounts", 6, ref message)
          || !blockchain.IsTXOwnedAtHeight("forged block confirming the spend", tXSpend.Hash, HEIGHT_FORGED, ref message)
          || !blockchain.IsTXOwnedAtHeight("block above the forged block confirming the second spend", tXSpendAbove.Hash, HEIGHT_FORGED + 1, ref message))
          return false;

        BlockchainSynchronizing blockchainRestarted = StartNode(blockchain.LiteDatabase, int.MaxValue);

        if (!blockchainRestarted.IsSynchronized(ledger, $"Own TX lifecycle, replay rejecting the forged block at height {HEIGHT_FORGED}", HEIGHT_FORGED - 1, ref message)
          || !blockchainRestarted.IsTXOwnedAtHeight($"replay rejecting the forged block at height {HEIGHT_FORGED}", tXSpend.Hash, 0, ref message)
          || !blockchainRestarted.IsTXOwnedAtHeight($"replay rejecting the forged block below the second spend", tXSpendAbove.Hash, 0, ref message))
          return false;

        blockchainRestarted.InsertBlocks(blocksLedger.Where(b => b.Header.Height >= HEIGHT_FORGED).ToList());

        return blockchainRestarted.IsSynchronized(ledger, "Own TX lifecycle, Ledger blocks inserted after the rejected block", 6, ref message)
          && blockchainRestarted.IsTXOwnedAtHeight("Ledger blocks inserted after the rejected block", tXSpend.Hash, HEIGHT_FORGED, ref message)
          && blockchainRestarted.IsTXOwnedAtHeight("Ledger blocks confirming the second spend again", tXSpendAbove.Hash, HEIGHT_FORGED + 1, ref message);
      }

      // Counter-tested 2026-11-14 03:01
      internal static bool TestConflictReopen(Wallet wallet, ref string message)
      {
        Console.WriteLine("  17. Conflict Reopen: Conflicting TX confirms own TX, rollback reopens it outside pool.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        TXBToken tXSpend = (TXBToken)blocksLedger.Single(b => b.Header.Height == 2).TXs[1];

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);
        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height == 1).ToList());

        if (!IsAccepted("Own TX lifecycle, spend submitted at height 1", () => blockchain.SubmitTX(tXSpend.TXRaw), ref message))
          return false;

        while (ledger.GetHeightTip() > 1)
          if (!ledger.TryRollBack("own TX lifecycle", out _, ref message))
            return false;

        TXBToken tXConflicting = ledger.CreateTXSpend(wallet, CreateIDAccount(1002, 3), VALUE_SPEND, t => { });

        if (!ledger.TryInsertBlock("conflicting spend", [ledger.CreateCoinbase(wallet, CreateIDAccount(1000, 1), FEE), tXConflicting], isValidExpected: true, null, ref message))
          return false;

        blockchain.InsertBlocks(ledger.GetBlocksInserted().Where(b => b.Header.Height == 2).ToList());

        if (!blockchain.IsSynchronized(ledger, "Own TX lifecycle, block with the conflicting spend at height 2", 2, ref message)
          || !blockchain.IsTXOwnedAtHeight("block with the conflicting spend at height 2", tXSpend.Hash, 2, ref message))
          return false;

        blockchain.RollBackTo(1);

        return blockchain.IsSynchronized(ledger, "Own TX lifecycle, rollback of the conflicting spend", 1, ref message)
          && blockchain.IsTXOwnedAtHeight("rollback of the conflicting spend", tXSpend.Hash, 0, ref message, isInPoolExpected: false)
          && blockchain.IsInPool("rollback of the conflicting spend", tXConflicting.Hash, true, ref message);
      }

      // Counter-tested 2026-11-14 03:01
      internal static bool TestAnchorCount(Wallet wallet, ref string message)
      {
        Console.WriteLine("  18. Anchor Count: Never count an open user payment as missed anchor.");

        const int HEIGHT_TIP = 4;

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        TXBToken tXSpend = (TXBToken)ledger.GetBlocksInserted().Single(b => b.Header.Height == 2).TXs[1];

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);
        blockchain.InsertBlocks(ledger.GetBlocksInserted().Where(b => b.Header.Height == 1).ToList());

        if (!IsAccepted("Own TX lifecycle, spend submitted at height 1", () => blockchain.SubmitTX(tXSpend.TXRaw), ref message))
          return false;

        while (ledger.GetHeightTip() > 1)
          if (!ledger.TryRollBack("own TX lifecycle", out _, ref message))
            return false;

        if (!TryInsertCoinbasesUpTo(ledger, wallet, CreateIDAccount(1000, 1), HEIGHT_TIP, ref message))
          return false;

        blockchain.InsertBlocks(ledger.GetBlocksInserted().Where(b => b.Header.Height > 1).ToList());

        string nameCase = $"coinbase-only blocks up to height {HEIGHT_TIP} leaving the spend open";

        if (!blockchain.IsSynchronized(ledger, $"Own TX lifecycle, {nameCase}", HEIGHT_TIP, ref message)
          || !blockchain.IsTXOwnedAtHeight(nameCase, tXSpend.Hash, 0, ref message))
          return false;

        int countAnchorsReplaceable = blockchain.CountAnchorsReplaceable(FEE_ANCHOR);

        if (countAnchorsReplaceable == 0)
          return true;

        message = $"Own TX lifecycle, {nameCase}: CountAnchorsReplaceable counts {countAnchorsReplaceable} missed anchors, expected 0.";
        return false;
      }

      bool IsTXOwnedAtHeight(string nameCase, byte[] hashTX, int? heightConfirmedExpected, ref string message, bool? isInPoolExpected = null)
      {
        string nameCaseFull = $"Own TX lifecycle, {nameCase}";
        int? heightConfirmed = TXsOwnedStored.FindById(hashTX)?[NAME_FIELD_HEIGHT_CONFIRMED].AsInt32;

        if (heightConfirmed != heightConfirmedExpected)
        {
          message = $"{nameCaseFull}: TX {hashTX.ToHexString()} is {(heightConfirmed == null ? "not stored" : $"stored at HeightConfirmed {heightConfirmed}")}, " +
            $"expected {(heightConfirmedExpected == null ? "deleted" : $"HeightConfirmed {heightConfirmedExpected}")}.";
          return false;
        }

        bool isOpenExpected = heightConfirmedExpected == 0;

        if ((GetTX(hashTX) != null) != isOpenExpected)
        {
          message = $"{nameCaseFull}: GetTX {(isOpenExpected ? "doesn't serve" : "serves")} TX {hashTX.ToHexString()}.";
          return false;
        }

        if (GetHashesTXOwnedUnconfirmed().Any(h => h.IsAllBytesEqual(hashTX)) != isOpenExpected)
        {
          message = $"{nameCaseFull}: GetHashesTXOwnedUnconfirmed {(isOpenExpected ? "doesn't list" : "lists")} TX {hashTX.ToHexString()}.";
          return false;
        }

        return IsInPool(nameCase, hashTX, isInPoolExpected ?? isOpenExpected, ref message);
      }

      internal static bool TestSegmentInstallation(Wallet wallet, ref string message)
      {
        return TryCreateServer(wallet, out Ledger ledger, out BlockchainSynchronizing server, ref message)
          && TestSegmentDownload(ledger, server, ref message)
          && TestFaultySegment(ledger, server, ref message)
          && TestFaultyCarriedBlock(wallet, ledger, server, ref message)
          && TestInstallConfirmation(ledger, server, ref message);
      }

      // Counter-tested 2026-11-14 03:01
      static bool TestInstallConfirmation(Ledger ledger, BlockchainSynchronizing server, ref string message)
      {
        Console.WriteLine("  15. Install Confirmation: Mark own TX confirmed by a block carried in installation.");

        const int HEIGHT_CONFIRMED = 2000;

        List<Block> blocksLedger = ledger.GetBlocksInserted();
        TXBToken tXSpend = (TXBToken)blocksLedger.Single(b => b.Header.Height == HEIGHT_CONFIRMED).TXs[1];

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);
        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height < HEIGHT_CONFIRMED).ToList());

        if (!IsAccepted($"Own TX lifecycle, spend submitted at height {HEIGHT_CONFIRMED - 1}", () => blockchain.SubmitTX(tXSpend.TXRaw), ref message))
          return false;

        blockchain.Synchronizer.Drop();
        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height >= HEIGHT_CONFIRMED).ToList());

        if (!blockchain.IsWithoutAccounts("Own TX lifecycle, confirming block inserted without accounts", HEIGHT_TIP_WINDOW, ref message))
          return false;

        blockchain.DownloadSegments(GetSegmentServed(ledger, server));

        return blockchain.IsSynchronized(ledger, "Own TX lifecycle, installation carrying the confirming block", HEIGHT_TIP_WINDOW, ref message)
          && blockchain.IsTXOwnedAtHeight("installation carrying the confirming block", tXSpend.Hash, HEIGHT_CONFIRMED, ref message);
      }

      // Counter-tested 2026-11-13 17:16
      static bool TestSegmentDownload(Ledger ledger, BlockchainSynchronizing server, ref string message)
      {
        Console.WriteLine("  5. Segment Download: Install downloaded segments, reinstall after rolling back below installation.");

        Func<Header, byte[]> getSegmentServed = GetSegmentServed(ledger, server);

        BlockchainSynchronizing blockchain = StartNodeWithoutAccounts(ledger.GetBlocksInserted());

        const int HEIGHT_SEGMENT_MINER = 1000;

        Header headerInstalled = blockchain.ChainRoot.HeaderTipBlockchain.GetHeaderAncestor(HEIGHT_INSTALLED);
        byte[] segmentMiner = getSegmentServed(headerInstalled.GetHeaderAncestor(HEIGHT_SEGMENT_MINER));

        if (!blockchain.IsWithoutAccounts("Account synchronizer, download before any segment", HEIGHT_TIP_WINDOW, ref message)
          || !IsRejected($"Account synchronizer, segment of height {HEIGHT_SEGMENT_MINER} for the block at height {HEIGHT_INSTALLED}", () => blockchain.AddSegmentDownload(headerInstalled.Hash, segmentMiner, segmentMiner.Length), ref message))
          return false;

        blockchain.DownloadSegments(getSegmentServed);

        if (!blockchain.IsSynchronized(ledger, "Account synchronizer, segments downloaded and installed", HEIGHT_TIP_WINDOW, ref message))
          return false;

        blockchain.RollBackTo(HEIGHT_INSTALLED - 1);

        if (!blockchain.IsWithoutAccounts("Account synchronizer, rollback below the installed height", HEIGHT_INSTALLED - 1, ref message))
          return false;

        blockchain.InsertBlocks(ledger.GetBlocksInserted().Where(b => b.Header.Height >= HEIGHT_INSTALLED).ToList());
        blockchain.DownloadSegments(getSegmentServed);

        return blockchain.IsSynchronized(ledger, "Account synchronizer, segments downloaded again after the rollback below the installed height", HEIGHT_TIP_WINDOW, ref message);
      }

      // Counter-tested 2026-11-13 17:16
      static bool TestFaultySegment(Ledger ledger, BlockchainSynchronizing server, ref string message)
      {
        Console.WriteLine("  6. Faulty Segment: Reject the block committing to an invalid segment, then install.");

        const int HEIGHT_FORGED = HEIGHT_INSTALLED - 4;
        byte[] segmentInvalid = [1];

        Func<Header, byte[]> getSegmentServed = GetSegmentServed(ledger, server);
        List<Block> blocksLedger = ledger.GetBlocksInserted();

        BlockchainSynchronizing blockchain = StartNodeWithoutAccounts(blocksLedger.Where(b => b.Header.Height < HEIGHT_FORGED).ToList());
        blockchain.InsertBlocks(ForgeBlocks(blocksLedger, HEIGHT_FORGED, (tXs, header) => header.HashAccounts = SHA256.HashData(segmentInvalid)));

        blockchain.DownloadSegments(h => h.Height == HEIGHT_FORGED ? segmentInvalid : getSegmentServed(h));

        if (!blockchain.IsWithoutAccounts($"Account synchronizer, invalid segment committed by the block at height {HEIGHT_FORGED}", HEIGHT_FORGED - 1, ref message))
          return false;

        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height >= HEIGHT_FORGED).ToList());
        blockchain.DownloadSegments(getSegmentServed);

        return blockchain.IsSynchronized(ledger, "Account synchronizer, segments downloaded after the block with the invalid segment was replaced", HEIGHT_TIP_WINDOW, ref message);
      }

      // Counter-tested 2026-11-13 17:16
      static bool TestFaultyCarriedBlock(Wallet wallet, Ledger ledger, BlockchainSynchronizing server, ref string message)
      {
        Console.WriteLine("  7. Faulty Carried Block: Reject a carried block failing during installation, then install.");

        const int HEIGHT_FORGED = HEIGHT_INSTALLED;

        Func<Header, byte[]> getSegmentServed = GetSegmentServed(ledger, server);
        List<Block> blocksLedger = ledger.GetBlocksInserted();
        TXBToken tXSpendInvalid = ledger.CreateTXSpend(wallet, CreateIDAccount(1001, 2), VALUE_SPEND, t => t.Nonce = 99);

        BlockchainSynchronizing blockchain = StartNodeWithoutAccounts(blocksLedger.Where(b => b.Header.Height < HEIGHT_FORGED).ToList());
        blockchain.InsertBlocks(ForgeBlocks(blocksLedger, HEIGHT_FORGED, (tXs, header) => tXs.Add(tXSpendInvalid)));

        blockchain.DownloadSegments(getSegmentServed);

        if (!blockchain.IsWithoutAccounts($"Account synchronizer, carried block at height {HEIGHT_FORGED} failing during the installation", HEIGHT_FORGED - 1, ref message))
          return false;

        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height >= HEIGHT_FORGED).ToList());
        blockchain.DownloadSegments(getSegmentServed);

        return blockchain.IsSynchronized(ledger, "Account synchronizer, segments downloaded after the failing carried block was replaced", HEIGHT_TIP_WINDOW, ref message);
      }

      static List<Block> ForgeBlocks(List<Block> blocksLedger, int heightForged, Action<List<TXBToken>, HeaderBToken> alter)
      {
        List<Block> blocksForged = [];
        byte[] hashPrevious = blocksLedger.Single(b => b.Header.Height == heightForged - 1).Header.Hash;

        foreach (Block blockLedger in blocksLedger.Where(b => b.Header.Height >= heightForged))
        {
          List<TXBToken> tXs = [.. blockLedger.TXs.Cast<TXBToken>()];
          HeaderBToken header = new()
          {
            HashPrevious = hashPrevious,
            HashAccounts = ((HeaderBToken)blockLedger.Header).HashAccounts,
            Height = blockLedger.Header.Height
          };

          if (header.Height == heightForged)
            alter(tXs, header);

          Block block = CreateBlock(tXs);
          header.MerkleRoot = block.Header.MerkleRoot;
          header.ComputeHash();
          block.Header = header;
          block.Serialize();

          blocksForged.Add(block);
          hashPrevious = header.Hash;
        }

        return blocksForged;
      }

      static bool TryCreateServer(Wallet wallet, out Ledger ledger, out BlockchainSynchronizing server, ref string message)
      {
        ledger = new(heightRoot: 0);
        server = null;

        byte[] iDMiner = CreateIDAccount(1000, 1);
        byte[] iDReceiver = CreateIDAccount(1001, 2);

        if (!ledger.TryInsertBlock("coinbase creating the wallet account", [ledger.CreateCoinbase(wallet, wallet.Hash160KeyPublicReceiving, 0)], isValidExpected: true, null, ref message))
          return false;

        foreach (int heightSpend in new[] { 100, 1000, 2000, 2052 })
          if (!TryInsertCoinbasesUpTo(ledger, wallet, iDMiner, heightSpend - 1, ref message)
            || !ledger.TryInsertBlock($"spend at height {heightSpend}", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => { })], isValidExpected: true, null, ref message))
            return false;

        if (!TryInsertCoinbasesUpTo(ledger, wallet, iDMiner, HEIGHT_TIP_WINDOW, ref message))
          return false;

        server = StartNode(CreateLiteDatabase(), int.MaxValue);
        server.InsertBlocks(ledger.GetBlocksInserted());

        return server.IsSynchronized(ledger, "Account synchronizer, serving node", HEIGHT_TIP_WINDOW, ref message);
      }

      static Func<Header, byte[]> GetSegmentServed(Ledger ledger, BlockchainSynchronizing server)
      {
        Dictionary<int, byte[]> hashesBlock = ledger.GetBlocksInserted().ToDictionary(b => b.Header.Height, b => b.Header.Hash);

        return header => server.GetSegmentSweptByBlock(hashesBlock[header.Height]);
      }

      static BlockchainSynchronizing StartNodeWithoutAccounts(List<Block> blocksLedger)
      {
        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);

        blockchain.Synchronizer.Drop();
        blockchain.InsertBlocks(blocksLedger);

        return blockchain;
      }

      void DownloadSegments(Func<Header, byte[]> getSegment)
      {
        while (FetchHeaderSweepMissingNext(h => true) is Header header)
        {
          byte[] segment = getSegment(header);

          AddSegmentDownload(header.Hash, segment, segment.Length);
        }
      }

      void RollBackTo(int height)
      {
        Header headerCutOff = ChainRoot.HeaderTipBlockchain.GetHeaderAncestor(height + 1);

        RollBack(height);
        ChainRoot.CutOff(headerCutOff);

        UpdateTipBlockchain(ChainRoot);
      }

      static BlockchainSynchronizing StartNode(LiteDatabase liteDatabase, int heightAnchored)
      {
        CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);
        CreateWalletFile(NAME_TOKEN);

        BlockchainBitcoin blockchainParent = new(CreateLiteDatabase(), new SemaphoreSlim(1));

        BlockchainSynchronizing blockchain = new(liteDatabase, blockchainParent, heightAnchored);
        blockchain.Load();

        return blockchain;
      }

      protected override void ParseBlockStored(Block block)
      {
        if (block.Header.Height <= HeightAnchored)
          block.Header.HeaderParent = ChainRoot.HeaderRoot.HeaderParent;

        base.ParseBlockStored(block);
      }

      void InsertBlocks(List<Block> blocksLedger)
      {
        foreach (Block blockLedger in blocksLedger)
        {
          Block block = new(blockLedger.Buffer[..blockLedger.LengthDataPayload]);

          int index = 0;
          Header header = Parser.ParseHeader(block.Buffer, ref index);

          ChainRoot.AppendHeader(header);

          if (!TryParseBlockStored(header, block))
            throw new InvalidOperationException($"Block of the Ledger at height {header.Height} cannot be parsed.");

          ChainRoot.BlocksQueued.Add(header.Height, block);
        }

        UpdateTipBlockchain(ChainRoot);
      }

      bool IsInPool(string nameCase, byte[] hashTX, bool isInPoolExpected, ref string message)
      {
        if ((GetHashesTXNotInPool([hashTX]).Count == 0) == isInPoolExpected)
          return true;

        message = $"Account synchronizer, {nameCase}: TX {hashTX.ToHexString()} is {(isInPoolExpected ? "missing in" : "in")} the TX pool.";
        return false;
      }

    }
  }
}
