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
        && BlockchainSynchronizing.TestSegmentInstallation(Testbench.Wallet, ref message);
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

      internal static bool TestFreshStart(Wallet wallet, ref string message)
      {
        Console.WriteLine("  1. Fresh Start: Install empty accounts at height 0, follow inserted blocks.");

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);

        if (!blockchain.IsSynchronized("fresh start before any block", 0, ref message)
          || !TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        foreach (Block blockLedger in ledger.GetBlocksInserted())
        {
          blockchain.InsertBlocks([blockLedger]);

          if (!blockchain.IsSynchronized($"fresh start after the block at height {blockLedger.Header.Height}", blockLedger.Header.Height, ref message))
            return false;
        }

        return true;
      }

      internal static bool TestRollbackAtTip(Wallet wallet, ref string message)
      {
        Console.WriteLine("  2. Rollback At Tip: Roll back accounts with blocks, then insert a replacing fork.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);
        blockchain.InsertBlocks(ledger.GetBlocksInserted());
        blockchain.RollBackTo(3);

        if (!blockchain.IsSynchronized("rollback to height 3", 3, ref message))
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

        return blockchain.IsSynchronized("replacing fork inserted after the rollback to height 3", 6, ref message);
      }

      internal static bool TestRestartRollback(Wallet wallet, ref string message)
      {
        Console.WriteLine("  3. Restart Rollback: Restart rolls accounts back with unanchored blocks before replaying.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);
        blockchain.InsertBlocks(ledger.GetBlocksInserted());

        BlockchainSynchronizing blockchainRestarted = StartNode(blockchain.LiteDatabase, 3);

        if (!blockchainRestarted.IsSynchronized("restart with blocks anchored up to height 3", 3, ref message))
          return false;

        blockchainRestarted.InsertBlocks(ledger.GetBlocksInserted().Where(b => b.Header.Height > 3).ToList());

        return blockchainRestarted.IsSynchronized("blocks above height 3 inserted again after the restart", 6, ref message);
      }

      internal static bool TestReplayFromRoot(Wallet wallet, ref string message)
      {
        Console.WriteLine("  4. Replay From Root: Replay all stored blocks after dropped or unknown accounts.");

        if (!TryCreateLedger(wallet, out Ledger ledger, ref message))
          return false;

        BlockchainSynchronizing blockchain = StartNode(CreateLiteDatabase(), int.MaxValue);
        blockchain.InsertBlocks(ledger.GetBlocksInserted());
        blockchain.Synchronizer.Drop();

        BlockchainSynchronizing blockchainRestarted = StartNode(blockchain.LiteDatabase, int.MaxValue);

        if (!blockchainRestarted.IsSynchronized("restart after the account database was dropped", 6, ref message))
          return false;

        blockchainRestarted.BlocksStored.Delete(6);

        return StartNode(blockchainRestarted.LiteDatabase, int.MaxValue)
          .IsSynchronized("restart without the stored block the accounts applied last", 5, ref message);
      }

      internal static bool TestSegmentInstallation(Wallet wallet, ref string message)
      {
        return TryCreateServer(wallet, out Ledger ledger, out BlockchainSynchronizing server, ref message)
          && TestSegmentDownload(ledger, server, ref message)
          && TestFaultySegment(ledger, server, ref message)
          && TestFaultyCarriedBlock(wallet, ledger, server, ref message);
      }

      static bool TestSegmentDownload(Ledger ledger, BlockchainSynchronizing server, ref string message)
      {
        Console.WriteLine("  5. Segment Download: Install downloaded segments, reinstall after rolling back below installation.");

        Func<Header, byte[]> getSegmentServed = GetSegmentServed(ledger, server);

        BlockchainSynchronizing blockchain = StartNodeWithoutAccounts(ledger.GetBlocksInserted());

        Header headerInstalled = blockchain.ChainRoot.HeaderTipBlockchain.GetHeaderAncestor(HEIGHT_INSTALLED);
        byte[] segmentBelow = getSegmentServed(headerInstalled.HeaderPrevious);

        if (!blockchain.IsWithoutAccounts("download before any segment", HEIGHT_TIP_WINDOW, ref message)
          || !IsRejected($"Account synchronizer, segment of height {HEIGHT_INSTALLED - 1} for the block at height {HEIGHT_INSTALLED}", () => blockchain.AddSegmentDownload(headerInstalled.Hash, segmentBelow, segmentBelow.Length), ref message))
          return false;

        blockchain.DownloadSegments(getSegmentServed);

        if (!blockchain.IsSynchronized("segments downloaded and installed", HEIGHT_TIP_WINDOW, ref message))
          return false;

        blockchain.RollBackTo(HEIGHT_INSTALLED - 1);

        if (!blockchain.IsWithoutAccounts("rollback below the installed height", HEIGHT_INSTALLED - 1, ref message))
          return false;

        blockchain.InsertBlocks(ledger.GetBlocksInserted().Where(b => b.Header.Height >= HEIGHT_INSTALLED).ToList());
        blockchain.DownloadSegments(getSegmentServed);

        return blockchain.IsSynchronized("segments downloaded again after the rollback below the installed height", HEIGHT_TIP_WINDOW, ref message);
      }

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

        if (!blockchain.IsWithoutAccounts($"invalid segment committed by the block at height {HEIGHT_FORGED}", HEIGHT_FORGED - 1, ref message))
          return false;

        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height >= HEIGHT_FORGED).ToList());
        blockchain.DownloadSegments(getSegmentServed);

        return blockchain.IsSynchronized("segments downloaded after the block with the invalid segment was replaced", HEIGHT_TIP_WINDOW, ref message);
      }

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

        if (!blockchain.IsWithoutAccounts($"carried block at height {HEIGHT_FORGED} failing during the installation", HEIGHT_FORGED - 1, ref message))
          return false;

        blockchain.InsertBlocks(blocksLedger.Where(b => b.Header.Height >= HEIGHT_FORGED).ToList());
        blockchain.DownloadSegments(getSegmentServed);

        return blockchain.IsSynchronized("segments downloaded after the failing carried block was replaced", HEIGHT_TIP_WINDOW, ref message);
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

        if (!ledger.TryInsertBlock("coinbase creating the wallet account", [ledger.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, 0)], isValidExpected: true, null, ref message))
          return false;

        foreach (int heightSpend in new[] { 100, 1000, 2000, 2052 })
          if (!TryInsertCoinbasesUpTo(ledger, wallet, iDMiner, heightSpend - 1, ref message)
            || !ledger.TryInsertBlock($"spend at height {heightSpend}", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => { })], isValidExpected: true, null, ref message))
            return false;

        if (!TryInsertCoinbasesUpTo(ledger, wallet, iDMiner, HEIGHT_TIP_WINDOW, ref message))
          return false;

        server = StartNode(CreateLiteDatabase(), int.MaxValue);
        server.InsertBlocks(ledger.GetBlocksInserted());

        return server.IsSynchronized("serving node", HEIGHT_TIP_WINDOW, ref message);
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

      static bool TryCreateLedger(Wallet wallet, out Ledger ledger, ref string message)
      {
        ledger = new(heightRoot: 0);

        byte[] iDMiner = CreateIDAccount(1000, 1);
        byte[] iDReceiver = CreateIDAccount(1001, 2);

        return ledger.TryInsertBlock("coinbase creating the wallet account", [ledger.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, 0)], isValidExpected: true, null, ref message)
          && ledger.TryInsertBlock("spend from the wallet account", [ledger.CreateCoinbase(wallet, iDMiner, FEE), ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => { })], isValidExpected: true, null, ref message)
          && TryInsertCoinbasesUpTo(ledger, wallet, iDMiner, 6, ref message);
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

      static void CreateWalletFile(string nameToken)
      {
        string pathWallet = $"Wallet{nameToken}/wallet";

        if (File.Exists(pathWallet))
          return;

        Directory.CreateDirectory($"Wallet{nameToken}");
        File.WriteAllText(pathWallet, Crypto.GenerateKeyPrivate());
      }

      static LiteDatabase CreateLiteDatabase()
      {
        return new LiteDatabase(new MemoryStream(), new BsonMapper { IncludeFields = true, IncludeNonPublic = true });
      }

      protected override Header CreateHeaderRoot()
      {
        return new HeaderBToken { Hash = new byte[32], Height = 0 };
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

      bool IsWithoutAccounts(string nameCase, int heightExpected, ref string message)
      {
        if (!IsAtHeight(nameCase, heightExpected, ref message))
          return false;

        if (!Synchronizer.IsAccountsAtTip())
          return true;

        message = $"Account synchronizer, {nameCase}: the accounts are at the tip without a database download.";
        return false;
      }

      bool IsAtHeight(string nameCase, int heightExpected, ref string message)
      {
        if (GetHeight() == heightExpected)
          return true;

        message = $"Account synchronizer, {nameCase}: the tip is at height {GetHeight()}, expected {heightExpected}.";
        return false;
      }

      bool IsSynchronized(string nameCase, int heightExpected, ref string message)
      {
        if (!IsAtHeight(nameCase, heightExpected, ref message))
          return false;

        if (!Synchronizer.IsAccountsAtTip())
        {
          message = $"Account synchronizer, {nameCase}: the accounts are not at the tip.";
          return false;
        }

        for (Header header = ChainRoot.HeaderTipBlockchain; header.Height > 0 && header.Height >= heightExpected - BITCOIN_REORG_MARGIN; header = header.HeaderPrevious)
        {
          byte[] segment = GetSegmentSweptByBlock(header.Hash);

          if (segment == null || !SHA256.HashData(segment).IsAllBytesEqual(((HeaderBToken)header).HashAccounts))
          {
            message = $"Account synchronizer, {nameCase}: segment {header.Height % COUNT_SEGMENTS} served for height {header.Height} " +
              $"{(segment == null ? "is missing" : "doesn't match HashAccounts of its block")}.";
            return false;
          }
        }

        return true;
      }
    }
  }
}
