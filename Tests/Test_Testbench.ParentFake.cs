using System.Security.Cryptography;

using BTokenCore;

using LiteDB;

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  abstract partial class Test_Testbench
  {
    protected class BlockchainParentFake : BlockchainBitcoin
    {
      class ParserParentFake : ParserBitcoin
      {
        internal override Header ParseHeader(byte[] buffer, ref int index)
        {
          return new HeaderBToken(buffer, ref index);
        }
      }

      internal bool IsAnchorFailingOnce;

      internal BlockchainParentFake(LiteDatabase liteDatabase)
        : base(liteDatabase, new SemaphoreSlim(1))
      {
        Parser = new ParserParentFake();
      }

      internal override bool TryCreateTXAnchor(TXOutputTokenAnchor tokenAnchor, long feeAnchor, out int lockTime)
      {
        if (IsAnchorFailingOnce)
        {
          IsAnchorFailingOnce = false;
          throw new InvalidOperationException("Anchor creation fails on purpose.");
        }

        return base.TryCreateTXAnchor(tokenAnchor, feeAnchor, out lockTime);
      }

      protected override Header CreateHeaderRoot()
      {
        return new HeaderBToken { Hash = new byte[32], Height = 0 };
      }

      internal TXBitcoin Fund(long value)
      {
        TXBitcoin tXFunding = new();

        tXFunding.Inputs.Add(new TXInputBitcoin
        {
          TXIDOutput = RandomNumberGenerator.GetBytes(32),
          IndexOutput = 0
        });

        tXFunding.TXOutputs.Add(new TXOutputP2PKH(value, Wallet.Hash160KeyPublicReceiving));
        tXFunding.Serialize(Wallet, Wallet.Hash160KeyPublicReceiving);

        MineBlock([tXFunding]);

        return tXFunding;
      }

      internal Header MineBlock(List<TXBitcoin> tXs, params TXOutputTokenAnchor[] anchors)
      {
        Block block = AppendBlockMined(tXs, anchors);

        ChainRoot.BlocksQueued.Add(block.Header.Height, block);

        UpdateTipBlockchain(ChainRoot);

        return block.Header;
      }

      internal void CatchUpAnchors(List<Block> blocksLedger)
      {
        List<Block> blocks = blocksLedger.Select(b => AppendBlockMined([], CreateAnchor(b.Header.Hash, b.Header.HashPrevious))).ToList();

        AppendBlockMined([]);

        foreach (Block block in blocks)
          ChainRoot.BlocksQueued.Add(block.Header.Height, block);

        UpdateTipBlockchain(ChainRoot);
      }

      Block AppendBlockMined(List<TXBitcoin> tXs, params TXOutputTokenAnchor[] anchors)
      {
        int height = ChainRoot.HeaderTip.Height + 1;
        Block block = new(0);
        block.TXs.AddRange(tXs);

        if (anchors.Length > 0)
        {
          TXBitcoin tXAnchors = new() { LockTime = height - 1 };
          tXAnchors.TXOutputs.AddRange(anchors);
          block.TXs.Add(tXAnchors);
        }

        List<TXOutputTokenAnchor> anchorsEligible = ParserToken.EnumerateAnchorsEligible(block.TXs, height).ToList();
        HeaderBToken header;

        do
        {
          header = new(
            headerHash: null,
            hashPrevious: ChainRoot.HeaderTip.Hash,
            merkleRootHash: RandomNumberGenerator.GetBytes(32),
            hashAccounts: new byte[32],
            nonce: 0);

          header.ComputeHash();
          ParserToken.DrawAnchorsWinner(header, anchorsEligible, 0);
        }
        while (anchors.Any(a => header.AnchorsWinner[a.IDToken] != a));

        ChainRoot.AppendHeader(header);
        block.Header = header;

        return block;
      }

      internal void MineAnchors(List<Block> blocksLedger)
      {
        foreach (Block blockLedger in blocksLedger)
          MineBlock([], CreateAnchor(blockLedger.Header.Hash, blockLedger.Header.HashPrevious));
      }

      internal static TXOutputTokenAnchor CreateAnchor(byte[] hashBlock, byte[] hashBlockPrevious)
      {
        return new TXOutputTokenAnchor
        {
          IDToken = ID_TOKEN_BTOKEN,
          HashBlockReferenced = hashBlock,
          HashBlockPreviousReferenced = hashBlockPrevious
        };
      }

      internal BlockchainParentFake Restart()
      {
        return new BlockchainParentFake(LiteDatabase);
      }

      internal void DeleteBlocksStored(int heightFirst, int heightLast)
      {
        BlocksCompact.DeleteMany(Query.Between(NAME_FIELD_ID, heightFirst, heightLast));
      }

      internal void RollBackTo(int height)
      {
        Header headerCutOff = ChainRoot.HeaderTipBlockchain.GetHeaderAncestor(height + 1);

        RollBack(height);
        ChainRoot.CutOff(headerCutOff);

        UpdateTipBlockchain(ChainRoot);
      }
    }
  }
}
