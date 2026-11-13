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
      static readonly byte[] ID_TOKEN_BTOKEN = [(byte)'B', (byte)'T', (byte)'K', 0];

      internal TXOutputTokenAnchor AnchorCreatedLast;

      internal BlockchainParentFake(LiteDatabase liteDatabase)
        : base(liteDatabase, new SemaphoreSlim(1))
      {
        Parser = new ParserBToken();
      }

      protected override Header CreateHeaderRoot()
      {
        return new HeaderBToken { Hash = new byte[32], Height = 0 };
      }

      internal override bool TryCreateTXAnchor(TXOutputTokenAnchor tokenAnchor, long feeAnchor, out int lockTime)
      {
        AnchorCreatedLast = tokenAnchor;
        lockTime = GetHeight();

        return true;
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

      internal void MineAnchors(List<Block> blocksLedger)
      {
        foreach (Block blockLedger in blocksLedger)
          MineBlock(new TXOutputTokenAnchor
          {
            IDToken = ID_TOKEN_BTOKEN,
            HashBlockReferenced = blockLedger.Header.Hash,
            HashBlockPreviousReferenced = blockLedger.Header.HashPrevious
          });
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
  }
}
