using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using BTokenCore;

using LiteDB;


namespace BTokenCore_Testbench;

internal partial class Testbench
{
  class TestParsers : Test_Testbench
  {
    internal TestParsers(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      Wallet wallet = Testbench.Wallet;

      return Test_HeaderBToken(ref message)
        && Test_HeaderBitcoin(ref message)
        && Test_TXBToken(wallet, ref message)
        && Test_Block(wallet, ref message)
        && Test_AnchorsWinner(wallet, ref message)
        && Test_ScoreRootHash(wallet, out Block blockParsed, ref message)
        && Test_WinnersStored(blockParsed, ref message);
    }

    const int COUNT_BYTES_LEADING = 7;

    static byte[] EmbedInBuffer(byte[] data)
    {
      return [0xff, 0xfe, 0xfd, 0xfc, 0xfb, 0xfa, 0xf9, .. data, 0xf8];
    }

    // Counter-tested 2026-11-13 14:03
    bool Test_HeaderBToken(ref string message)
    {
      Console.WriteLine("  1. HeaderBToken Round-Trip: Serialize and parse HeaderBToken at two buffer offsets.");

      HeaderBToken header = CreateHeaderBToken("5a38043063d4da8309c3cbb2ffd20db190d74bdce1dec3f716d2ffc553864717".ToBinary());

      byte[] headerSerialized = header.Serialize();

      return IsHeaderBTokenParsedEqual(header, headerSerialized, 0, ref message)
        && IsHeaderBTokenParsedEqual(header, EmbedInBuffer(headerSerialized), COUNT_BYTES_LEADING, ref message);
    }

    static bool IsHeaderBTokenParsedEqual(HeaderBToken header, byte[] buffer, int indexStart, ref string message)
    {
      int index = indexStart;

      HeaderBToken headerParsed = new(buffer, ref index);

      string nameParser = $"{nameof(HeaderBToken)} parsed from index {indexStart}";

      return AreHeadersBTokenEqual(nameParser, header, headerParsed, ref message)
        && IsFieldEqual(nameParser, "index after parsing", indexStart + HeaderBToken.COUNT_HEADER_BYTES, index, ref message);
    }

    static bool AreHeadersBTokenEqual(string nameParser, HeaderBToken header, HeaderBToken headerParsed, ref string message)
    {
      return IsFieldEqual(nameParser, nameof(Header.Version), header.Version, headerParsed.Version, ref message)
        && IsFieldEqual(nameParser, nameof(Header.HashPrevious), header.HashPrevious, headerParsed.HashPrevious, ref message)
        && IsFieldEqual(nameParser, nameof(Header.MerkleRoot), header.MerkleRoot, headerParsed.MerkleRoot, ref message)
        && IsFieldEqual(nameParser, nameof(HeaderBToken.HashAccounts), header.HashAccounts, headerParsed.HashAccounts, ref message)
        && IsFieldEqual(nameParser, nameof(Header.Nonce), header.Nonce, headerParsed.Nonce, ref message)
        && IsFieldEqual(nameParser, nameof(Header.Hash), header.Hash, headerParsed.Hash, ref message);
    }

    // Counter-tested 2026-11-13 14:03
    bool Test_HeaderBitcoin(ref string message)
    {
      Console.WriteLine("  2. HeaderBitcoin Parsing: Parse mainnet HeaderBitcoin of block 737857.");

      byte[] headerRaw = Convert.FromHexString(HEADER_BITCOIN_737857);

      int index = 0;

      HeaderBitcoin headerParsed = new(headerRaw, ref index);

      string nameParser = $"{nameof(HeaderBitcoin)} of block 737857";

      return IsFieldEqual(nameParser, nameof(Header.Hash), "0000000000000000000735fc64773067fd0175461148902c97c494ffa3ca9306".ToBinary(), headerParsed.Hash, ref message)
        && IsFieldEqual(nameParser, nameof(Header.Version), 0x21d18000u, headerParsed.Version, ref message)
        && IsFieldEqual(nameParser, nameof(Header.HashPrevious), "0000000000000000000230d9bb1db81e56916b0c2c7363231e75b82b24714482".ToBinary(), headerParsed.HashPrevious, ref message)
        && IsFieldEqual(nameParser, nameof(Header.MerkleRoot), "5a38043063d4da8309c3cbb2ffd20db190d74bdce1dec3f716d2ffc553864717".ToBinary(), headerParsed.MerkleRoot, ref message)
        && IsFieldEqual(nameParser, nameof(HeaderBitcoin.UnixTimeSeconds), 1653491077u, headerParsed.UnixTimeSeconds, ref message)
        && IsFieldEqual(nameParser, nameof(HeaderBitcoin.NBits), 0x17096a20u, headerParsed.NBits, ref message)
        && IsFieldEqual(nameParser, nameof(Header.Nonce), 0x42a47d62u, headerParsed.Nonce, ref message)
        && IsFieldEqual(nameParser, "index after parsing", HeaderBitcoin.COUNT_HEADER_BYTES, index, ref message)
        && IsFieldEqual(nameParser, "serialized bytes", headerRaw, headerParsed.Serialize(), ref message);
    }

    // Counter-tested 2026-11-13 14:03
    bool Test_TXBToken(Wallet wallet, ref string message)
    {
      Console.WriteLine("  3. TXBToken Round-Trip: Serialize and parse signed and coinbase TXBToken.");

      TXOutputTokenAnchor tXOutputTokenAnchor = CreateTXOutputTokenAnchor(ID_TOKEN, 0);

      BlockchainBToken.TXBToken tX = CreateTXBToken(wallet.KeyPublicReceiving, wallet.Hash160KeyPublicReceiving, tXOutputTokenAnchor);
      tX.Serialize(wallet, ID_TOKEN_BTOKEN);

      BlockchainBToken.TXBToken tXCoinbase = CreateTXBToken(wallet.KeyPublicReceiving, wallet.Hash160KeyPublicReceiving, tXOutputTokenAnchor);
      tXCoinbase.SerializeUnsigned();

      return IsTXBTokenParsedEqual(tX, tX.TXRaw, 0, isCoinbase: false, ref message)
        && IsTXBTokenParsedEqual(tX, EmbedInBuffer(tX.TXRaw), COUNT_BYTES_LEADING, isCoinbase: false, ref message)
        && IsTXBTokenParsedEqual(tXCoinbase, tXCoinbase.TXRaw, 0, isCoinbase: true, ref message)
        && IsTXBTokenParsedEqual(tXCoinbase, EmbedInBuffer(tXCoinbase.TXRaw), COUNT_BYTES_LEADING, isCoinbase: true, ref message);
    }

    bool Test_Block(Wallet wallet, ref string message)
    {
      Console.WriteLine("  4. Block Round-Trip: Serialize and parse BToken block with anchor winner.");

      BlockchainBToken.TXBToken tXCoinbase = CreateTXBToken(wallet.KeyPublicReceiving, wallet.Hash160KeyPublicReceiving);
      tXCoinbase.SerializeUnsigned();

      TXOutputTokenAnchor tXOutputTokenAnchor = CreateTXOutputTokenAnchor(ID_TOKEN, 0);

      BlockchainBToken.TXBToken tX = CreateTXBToken(wallet.KeyPublicReceiving, wallet.Hash160KeyPublicReceiving, tXOutputTokenAnchor);
      tX.Serialize(wallet, ID_TOKEN_BTOKEN);

      Block block = CreateBlock([tXCoinbase, tX]);
      Block blockParsed = ParseBlockSerialized(block);

      string nameParser = "Block parsed by ParserBToken";

      if (!AreHeadersBTokenEqual(nameParser, (HeaderBToken)block.Header, (HeaderBToken)blockParsed.Header, ref message)
        || !IsFieldEqual(nameParser, "TXs.Count", block.TXs.Count, blockParsed.TXs.Count, ref message))
        return false;

      for (int i = 0; i < block.TXs.Count; i++)
        if (!AreTXsBTokenEqual($"{nameParser}, TXs[{i}]", (BlockchainBToken.TXBToken)block.TXs[i], (BlockchainBToken.TXBToken)blockParsed.TXs[i], ref message))
          return false;

      return IsFieldEqual(nameParser, "AnchorsWinner.Count", 1, blockParsed.Header.AnchorsWinner.Count, ref message)
        && IsAnchorWinnerLowestScore(nameParser, blockParsed.Header.Hash, blockParsed.Header, [tXOutputTokenAnchor], ref message);
    }

    bool Test_AnchorsWinner(Wallet wallet, ref string message)
    {
      Console.WriteLine("  5. Anchor Winners: Draw winners of two IDTokens, ignoring anchors with ineligible LockTime.");

      byte[] iDTokenSecond = [0x05, 0x06, 0x07, 0x08];

      TXOutputTokenAnchor[] anchorsFirst = [
        CreateTXOutputTokenAnchor(ID_TOKEN, 1),
        CreateTXOutputTokenAnchor(ID_TOKEN, 2),
        CreateTXOutputTokenAnchor(ID_TOKEN, 3)];

      TXOutputTokenAnchor[] anchorsSecond = [
        CreateTXOutputTokenAnchor(iDTokenSecond, 4),
        CreateTXOutputTokenAnchor(iDTokenSecond, 5)];

      TXOutputTokenAnchor[] anchorsIneligible = [
        CreateTXOutputTokenAnchor(ID_TOKEN, 6),
        CreateTXOutputTokenAnchor(ID_TOKEN, 7)];

      BlockchainBToken.TXBToken tXCoinbase = CreateTXBToken(wallet.KeyPublicReceiving, wallet.Hash160KeyPublicReceiving);
      tXCoinbase.SerializeUnsigned();

      List<BlockchainBToken.TXBToken> tXs = [
        tXCoinbase,
        CreateTXBToken(wallet.KeyPublicReceiving, wallet.Hash160KeyPublicReceiving, anchorsFirst[0], anchorsSecond[0]),
        CreateTXBToken(wallet.KeyPublicReceiving, wallet.Hash160KeyPublicReceiving, anchorsFirst[1], anchorsSecond[1]),
        CreateTXBToken(wallet.KeyPublicReceiving, wallet.Hash160KeyPublicReceiving, anchorsFirst[2]),
        CreateTXBToken(wallet.KeyPublicReceiving, wallet.Hash160KeyPublicReceiving, anchorsIneligible[0]),
        CreateTXBToken(wallet.KeyPublicReceiving, wallet.Hash160KeyPublicReceiving, anchorsIneligible[1])];

      tXs[^2].LockTime = 737856;
      tXs[^1].LockTime = 737858;

      foreach (BlockchainBToken.TXBToken tX in tXs.Skip(1))
        tX.Serialize(wallet, ID_TOKEN_BTOKEN);

      Block block = CreateBlock(tXs);

      string nameParser = "Anchor winners drawn by ParserBToken";
      TXOutputTokenAnchor anchorWinnerFirstPrevious = null;

      for (int round = 0; round < 2; round++)
      {
        do
        {
          block.Header.Nonce += 1;
          block.Header.ComputeHash();
        }
        while (!IsDrawDiscriminating(block.Header.Hash, anchorsFirst, anchorsSecond, anchorsIneligible, isRoundFirst: anchorWinnerFirstPrevious == null)
          || GetAnchorScoreLowest(block.Header.Hash, anchorsFirst) == anchorWinnerFirstPrevious);

        Block blockParsed = ParseBlockSerialized(block);

        if (!IsFieldEqual(nameParser, "AnchorsWinner.Count", 2, blockParsed.Header.AnchorsWinner.Count, ref message)
          || !IsAnchorWinnerLowestScore(nameParser, blockParsed.Header.Hash, blockParsed.Header, anchorsFirst, ref message)
          || !IsAnchorWinnerLowestScore(nameParser, blockParsed.Header.Hash, blockParsed.Header, anchorsSecond, ref message))
          return false;

        anchorWinnerFirstPrevious = GetAnchorScoreLowest(block.Header.Hash, anchorsFirst);
      }

      return true;
    }

    // Counter-tested 2026-11-15 16:34
    bool Test_ScoreRootHash(Wallet wallet, out Block blockParsed, ref string message)
    {
      Console.WriteLine("  6. Root Hash Score: Score child anchors with the root chain's header hash.");

      TXOutputTokenAnchor[] anchors = [
        CreateTXOutputTokenAnchor(ID_TOKEN, 1),
        CreateTXOutputTokenAnchor(ID_TOKEN, 2),
        CreateTXOutputTokenAnchor(ID_TOKEN, 3)];

      BlockchainBToken.TXBToken tXCoinbase = CreateTXBToken(wallet.KeyPublicReceiving, wallet.Hash160KeyPublicReceiving);
      tXCoinbase.SerializeUnsigned();

      List<BlockchainBToken.TXBToken> tXs = [
        tXCoinbase,
        CreateTXBToken(wallet.KeyPublicReceiving, wallet.Hash160KeyPublicReceiving, anchors[0]),
        CreateTXBToken(wallet.KeyPublicReceiving, wallet.Hash160KeyPublicReceiving, anchors[1], anchors[2])];

      foreach (BlockchainBToken.TXBToken tX in tXs.Skip(1))
        tX.Serialize(wallet, ID_TOKEN_BTOKEN);

      Block block = CreateBlock(tXs);
      block.Serialize();

      HeaderBToken headerRoot = new();
      HeaderBToken headerMiddle = new() { Hash = RandomNumberGenerator.GetBytes(32), HeaderParent = headerRoot };

      do
        headerRoot.Hash = RandomNumberGenerator.GetBytes(32);
      while (GetAnchorScoreLowest(headerRoot.Hash, anchors) == GetAnchorScoreLowest(block.Header.Hash, anchors)
        || GetAnchorScoreLowest(headerRoot.Hash, anchors) == GetAnchorScoreLowest(headerMiddle.Hash, anchors));

      blockParsed = null;

      foreach ((string nameLevel, Header headerParent) in new[] { ("one level", (Header)headerRoot), ("two levels", headerMiddle) })
      {
        blockParsed = ParseBlockBuffer(
          block.Buffer[..block.LengthDataPayload],
          new HeaderBToken
          {
            Hash = block.Header.Hash,
            HashPrevious = block.Header.HashPrevious,
            Height = block.Header.Height,
            HeaderParent = headerParent
          });

        string nameParser = $"Anchor winners of a BToken block {nameLevel} below the root chain";

        if (!IsFieldEqual(nameParser, "AnchorsWinner.Count", 1, blockParsed.Header.AnchorsWinner.Count, ref message)
          || !IsAnchorWinnerLowestScore(nameParser, headerRoot.Hash, blockParsed.Header, anchors, ref message))
          return false;
      }

      return true;
    }

    class BlockchainStore : BlockchainTestbench
    {
      const string NAME_TOKEN = "TestParsers";

      BlockchainStore()
        : base(NAME_TOKEN, CreateLiteDatabase(), new BlockchainParentFake(CreateLiteDatabase()))
      { }

      internal static BlockchainStore Create()
      {
        CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);
        CreateWalletFile(NAME_TOKEN);

        return new BlockchainStore();
      }

      internal BsonDocument CreateDocument(Block block)
      {
        return CreateDocumentBlockStored(block);
      }

      internal static BsonDocument CreateDocumentWithoutWinners(Block block)
      {
        return new BsonDocument { [NAME_FIELD_BLOCK_BYTES] = block.Buffer[..block.LengthDataPayload] };
      }

      internal static byte[] GetBytesBlock(BsonDocument documentBlock)
      {
        return documentBlock[NAME_FIELD_BLOCK_BYTES].AsBinary;
      }

      internal Block ReloadBlockStored(Block block, BsonDocument documentBlock)
      {
        Header header = new HeaderBToken
        {
          Hash = block.Header.Hash,
          HashPrevious = block.Header.HashPrevious,
          Height = block.Header.Height
        };

        return TryParseBlockStored(header, documentBlock, out Block blockReloaded) ? blockReloaded : null;
      }
    }

    // Counter-tested 2026-11-15 16:34
    bool Test_WinnersStored(Block blockParsed, ref string message)
    {
      Console.WriteLine("  7. Stored BToken Winners: Reload drawn winners from BlocksStored, never redraw.");

      const string NAME_CASE = "BToken block reloaded from its stored document";
      const string NAME_CASE_WITHOUT_WINNERS = "BToken block reloaded from a document without winners";

      BlockchainStore blockchain = BlockchainStore.Create();
      BsonDocument documentBlock = blockchain.CreateDocument(blockParsed);

      if (!IsFieldEqual(NAME_CASE, "stored block bytes", blockParsed.Buffer[..blockParsed.LengthDataPayload], BlockchainStore.GetBytesBlock(documentBlock), ref message))
        return false;

      if (blockchain.ReloadBlockStored(blockParsed, documentBlock) is not Block blockReloaded
        || blockchain.ReloadBlockStored(blockParsed, BlockchainStore.CreateDocumentWithoutWinners(blockParsed)) is not Block blockReloadedWithoutWinners)
      {
        message = $"{NAME_CASE}: the stored block cannot be parsed.";
        return false;
      }

      if (!IsFieldEqual(NAME_CASE_WITHOUT_WINNERS, "AnchorsWinner.Count", 0, blockReloadedWithoutWinners.Header.AnchorsWinner.Count, ref message)
        || !IsFieldEqual(NAME_CASE, "AnchorsWinner.Count", blockParsed.Header.AnchorsWinner.Count, blockReloaded.Header.AnchorsWinner.Count, ref message))
        return false;

      foreach (TXOutputTokenAnchor anchor in blockParsed.Header.AnchorsWinner.Values)
      {
        string iDToken = anchor.IDToken.ToHexString();

        if (!blockReloaded.Header.AnchorsWinner.TryGetValue(anchor.IDToken, out TXOutputTokenAnchor anchorReloaded))
        {
          message = $"{NAME_CASE}: no anchor winner of IDToken {iDToken}.";
          return false;
        }

        if (!IsFieldEqual(NAME_CASE, $"HashBlockReferenced of IDToken {iDToken}", anchor.HashBlockReferenced, anchorReloaded.HashBlockReferenced, ref message))
          return false;
      }

      return true;
    }

    static bool IsDrawDiscriminating(byte[] hashBlock, TXOutputTokenAnchor[] anchorsFirst, TXOutputTokenAnchor[] anchorsSecond, TXOutputTokenAnchor[] anchorsIneligible, bool isRoundFirst)
    {
      TXOutputTokenAnchor anchorLowestFirst = GetAnchorScoreLowest(hashBlock, anchorsFirst);

      bool isOrderDiscriminating = isRoundFirst
        ? anchorLowestFirst == anchorsFirst[1] && string.CompareOrdinal(ComputeScoreAnchor(hashBlock, anchorsFirst[2]), ComputeScoreAnchor(hashBlock, anchorsFirst[0])) < 0
        : anchorLowestFirst != anchorsFirst[0];

      return isOrderDiscriminating
        && GetAnchorScoreLowest(hashBlock, anchorsSecond) != anchorsSecond[0]
        && anchorsIneligible.All(a => string.CompareOrdinal(ComputeScoreAnchor(hashBlock, a), ComputeScoreAnchor(hashBlock, anchorLowestFirst)) < 0);
    }

    static bool IsTXBTokenParsedEqual(BlockchainBToken.TXBToken tX, byte[] buffer, int indexStart, bool isCoinbase, ref string message)
    {
      int index = indexStart;

      BlockchainBToken.TXBToken tXParsed = new(buffer, ref index, isCoinbase, verifySignatures: true, ID_TOKEN_BTOKEN);

      string nameParser = $"{nameof(BlockchainBToken.TXBToken)} parsed from index {indexStart}";

      return AreTXsBTokenEqual(nameParser, tX, tXParsed, ref message)
        && IsFieldEqual(nameParser, "index after parsing", indexStart + tX.TXRaw.Length, index, ref message);
    }

    static bool AreTXsBTokenEqual(string nameParser, BlockchainBToken.TXBToken tX, BlockchainBToken.TXBToken tXParsed, ref string message)
    {
      return IsFieldEqual(nameParser, nameof(tX.KeyPublic), tX.KeyPublic, tXParsed.KeyPublic, ref message)
        && IsFieldEqual(nameParser, nameof(tX.IDAccountSource), Crypto.ComputeHash160(tX.KeyPublic), tXParsed.IDAccountSource, ref message)
        && IsFieldEqual(nameParser, nameof(tX.HeightAccountCreated), tX.HeightAccountCreated, tXParsed.HeightAccountCreated, ref message)
        && IsFieldEqual(nameParser, nameof(tX.Nonce), tX.Nonce, tXParsed.Nonce, ref message)
        && IsFieldEqual(nameParser, nameof(tX.Fee), tX.Fee, tXParsed.Fee, ref message)
        && IsFieldEqual(nameParser, nameof(tX.LockTime), tX.LockTime, tXParsed.LockTime, ref message)
        && IsFieldEqual(nameParser, nameof(tX.Hash), tX.Hash, tXParsed.Hash, ref message)
        && IsFieldEqual(nameParser, nameof(tX.TXRaw), tX.TXRaw, tXParsed.TXRaw, ref message)
        && IsFieldEqual(nameParser, nameof(tX.CountBytes), tX.TXRaw.Length, tXParsed.CountBytes, ref message)
        && AreTXOutputsParsedEqual(nameParser, tX.TXOutputs, tXParsed.TXOutputs, ref message);
    }

    static bool AreTXOutputsParsedEqual(string nameParser, List<TXOutput> tXOutputs, List<TXOutput> tXOutputsParsed, ref string message)
    {
      if (!IsFieldEqual(nameParser, "TXOutputs.Count", tXOutputs.Count, tXOutputsParsed.Count, ref message))
        return false;

      for (int i = 0; i < tXOutputs.Count; i++)
      {
        string nameOutput = $"TXOutputs[{i}]";

        if (tXOutputs[i].GetType() != tXOutputsParsed[i].GetType())
        {
          message = $"{nameParser}: {nameOutput} is {tXOutputsParsed[i].GetType().Name} after parsing, expected {tXOutputs[i].GetType().Name}.";
          return false;
        }

        if (tXOutputs[i] is TXOutputP2PKH outputP2PKH)
        {
          TXOutputP2PKH outputP2PKHParsed = (TXOutputP2PKH)tXOutputsParsed[i];

          if (!IsFieldEqual(nameParser, $"{nameOutput}.Value", outputP2PKH.Value, outputP2PKHParsed.Value, ref message)
            || !IsFieldEqual(nameParser, $"{nameOutput}.IDAccount", outputP2PKH.IDAccount, outputP2PKHParsed.IDAccount, ref message))
            return false;
        }
        else if (tXOutputs[i] is TXOutputTokenAnchor outputAnchor)
        {
          TXOutputTokenAnchor outputAnchorParsed = (TXOutputTokenAnchor)tXOutputsParsed[i];

          if (!IsFieldEqual(nameParser, $"{nameOutput}.IDToken", outputAnchor.IDToken, outputAnchorParsed.IDToken, ref message)
            || !IsFieldEqual(nameParser, $"{nameOutput}.HashBlockReferenced", outputAnchor.HashBlockReferenced, outputAnchorParsed.HashBlockReferenced, ref message)
            || !IsFieldEqual(nameParser, $"{nameOutput}.HashBlockPreviousReferenced", outputAnchor.HashBlockPreviousReferenced, outputAnchorParsed.HashBlockPreviousReferenced, ref message))
            return false;
        }
      }

      return true;
    }
  }
}
