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

      class ParserParentFake : ParserBitcoin
      {
        internal override Header ParseHeader(byte[] buffer, ref int index)
        {
          return new HeaderBToken(buffer, ref index);
        }
      }

      internal BlockchainParentFake(LiteDatabase liteDatabase)
        : base(liteDatabase, new SemaphoreSlim(1))
      {
        Parser = new ParserParentFake();
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

        tXFunding.TXOutputs.Add(new TXOutputP2PKH(value, Wallet.Hash160PKeyPublic));
        tXFunding.Serialize(Wallet);

        MineBlock([tXFunding]);

        return tXFunding;
      }

      internal Header MineBlock(List<TXBitcoin> tXs, params TXOutputTokenAnchor[] anchors)
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
        Block block = new(0) { Header = header };
        block.TXs.AddRange(tXs);

        ChainRoot.BlocksQueued.Add(header.Height, block);

        UpdateTipBlockchain(ChainRoot);

        return header;
      }

      internal void MineAnchors(List<Block> blocksLedger)
      {
        foreach (Block blockLedger in blocksLedger)
          MineBlock([], new TXOutputTokenAnchor
          {
            IDToken = ID_TOKEN_BTOKEN,
            HashBlockReferenced = blockLedger.Header.Hash,
            HashBlockPreviousReferenced = blockLedger.Header.HashPrevious
          });
      }

      internal static TXOutputTokenAnchor CreateAnchorWithheld(byte[] hashBlockPrevious)
      {
        return new TXOutputTokenAnchor
        {
          IDToken = ID_TOKEN_BTOKEN,
          HashBlockReferenced = RandomNumberGenerator.GetBytes(32),
          HashBlockPreviousReferenced = hashBlockPrevious
        };
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
