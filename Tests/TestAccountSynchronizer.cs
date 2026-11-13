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
        && BlockchainSynchronizing.TestRollbackAtTip(Testbench.Wallet, ref message);
    }

    class BlockchainSynchronizing : BlockchainTestbench
    {
      const string NAME_TOKEN = "TestSynchronizer";

      BlockchainSynchronizing(LiteDatabase liteDatabase, Blockchain blockchainParent)
        : base(NAME_TOKEN, liteDatabase, blockchainParent)
      { }

      internal static bool TestFreshStart(Wallet wallet, ref string message)
      {
        Console.WriteLine("  1. Fresh Start: Install empty accounts at height 0, follow inserted blocks.");

        BlockchainSynchronizing blockchain = Create();
        blockchain.Load();

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

        BlockchainSynchronizing blockchain = Create();
        blockchain.Load();
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

      static BlockchainSynchronizing Create()
      {
        CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);
        CreateWalletFile(NAME_TOKEN);

        BlockchainBitcoin blockchainParent = new(CreateLiteDatabase(), new SemaphoreSlim(1));

        return new BlockchainSynchronizing(CreateLiteDatabase(), blockchainParent);
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

      void InsertBlocks(List<Block> blocksLedger)
      {
        foreach (Block blockLedger in blocksLedger)
        {
          Block block = new(blockLedger.Buffer[..blockLedger.LengthDataPayload]);

          int index = 0;
          Header header = Parser.ParseHeader(block.Buffer, ref index);
          header.HeaderParent = ChainRoot.HeaderRoot.HeaderParent;

          ChainRoot.AppendHeader(header);

          if (!TryParseBlockStored(header, block))
            throw new InvalidOperationException($"Block of the Ledger at height {header.Height} cannot be parsed.");

          ChainRoot.BlocksQueued.Add(header.Height, block);
        }

        UpdateTipBlockchain(ChainRoot);
      }

      bool IsSynchronized(string nameCase, int heightExpected, ref string message)
      {
        if (GetHeight() != heightExpected)
        {
          message = $"Account synchronizer, {nameCase}: the tip is at height {GetHeight()}, expected {heightExpected}.";
          return false;
        }

        if (!Synchronizer.IsAccountsAtTip())
        {
          message = $"Account synchronizer, {nameCase}: the accounts are not at the tip.";
          return false;
        }

        for (Header header = ChainRoot.HeaderRoot.HeaderNext; header != null; header = header.HeaderNext)
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
