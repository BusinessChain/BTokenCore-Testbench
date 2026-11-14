using System.Security.Cryptography;

using BTokenCore;

using LiteDB;

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  abstract partial class Test_Testbench
  {
    protected Testbench Testbench;

    internal Test_Testbench(Testbench testbench)
    {
      Testbench = testbench;
    }

    internal abstract bool TryRun(out string message);

    protected static void CreateWalletFile(string nameToken)
    {
      string pathWallet = $"Wallet{nameToken}/wallet";

      if (File.Exists(pathWallet))
        return;

      Directory.CreateDirectory($"Wallet{nameToken}");
      File.WriteAllText(pathWallet, Crypto.GenerateKeyPrivate());
    }

    protected static LiteDatabase CreateLiteDatabase()
    {
      return new LiteDatabase(new MemoryStream(), new BsonMapper { IncludeFields = true, IncludeNonPublic = true });
    }

    protected static HeaderBToken CreateHeaderBToken(byte[] merkleRoot)
    {
      HeaderBToken header = new(
        headerHash: null,
        hashPrevious: "00000000000000000002a7c4c1e48d76c5a37902165a270156b7a8d72728a054".ToBinary(),
        merkleRootHash: merkleRoot,
        hashAccounts: "6e340b9cffb37a989ca544e6bb780a2c78901d3fb33738768511a30617afa01d".ToBinary(),
        nonce: 0x42a47d62);

      header.Version = 0x21d18000;
      header.ComputeHash();

      return header;
    }

    protected static BlockchainBToken.TXBToken CreateTXBToken(byte[] keyPublic, byte[] iDAccountReceiving, params TXOutputTokenAnchor[] tXOutputsTokenAnchor)
    {
      BlockchainBToken.TXBToken tX = new()
      {
        KeyPublic = keyPublic,
        HeightAccountCreated = 1234,
        Nonce = 5,
        Fee = 1000,
        LockTime = 737857
      };

      tX.TXOutputs.Add(new TXOutputP2PKH(50000, iDAccountReceiving));
      tX.TXOutputs.AddRange(tXOutputsTokenAnchor);

      return tX;
    }

    protected static readonly byte[] ID_TOKEN = [0x01, 0x02, 0x03, 0x04];

    protected const string NAME_ID_TOKEN_BTOKEN = "BTK";
    protected static readonly byte[] ID_TOKEN_BTOKEN = new ParserBToken(NAME_ID_TOKEN_BTOKEN).IDToken;

    protected const string HEADER_BITCOIN_737857 =
      "0080d121824471242bb8751e2363732c0c6b91561eb81dbbd9300200000000000000000017478653c5ffd216f7c3dee1dc4bd790" +
      "b10dd2ffb2cbc30983dad4633004385a85458e62206a0917627da442";

    protected static TXOutputTokenAnchor CreateTXOutputTokenAnchor(byte[] iDToken, byte seedHashBlockReferenced)
    {
      TXOutputTokenAnchor tXOutputTokenAnchor = new()
      {
        IDToken = iDToken,
        HashBlockReferenced = SHA256.HashData([seedHashBlockReferenced]),
        HashBlockPreviousReferenced = "0000000000000000000230d9bb1db81e56916b0c2c7363231e75b82b24714482".ToBinary()
      };

      tXOutputTokenAnchor.Serialize();

      return tXOutputTokenAnchor;
    }

    protected static TXOutputTokenAnchor GetAnchorScoreLowest(byte[] hashBlock, TXOutputTokenAnchor[] anchors)
    {
      return anchors.MinBy(a => ComputeScoreAnchor(hashBlock, a), StringComparer.Ordinal);
    }

    protected static string ComputeScoreAnchor(byte[] hashBlock, TXOutputTokenAnchor tXOutputTokenAnchor)
    {
      return Convert.ToHexString(SHA256.HashData(
        [.. hashBlock, .. tXOutputTokenAnchor.HashBlockReferenced, .. tXOutputTokenAnchor.HashBlockPreviousReferenced]));
    }

    protected static bool IsAnchorWinnerLowestScore(string nameParser, Header headerParsed, TXOutputTokenAnchor[] anchorsEligible, ref string message)
    {
      TXOutputTokenAnchor anchorExpected = GetAnchorScoreLowest(headerParsed.Hash, anchorsEligible);

      string iDToken = anchorExpected.IDToken.ToHexString();

      if (!headerParsed.AnchorsWinner.TryGetValue(anchorExpected.IDToken, out TXOutputTokenAnchor anchorWinner))
      {
        message = $"{nameParser}: no anchor winner of IDToken {iDToken}.";
        return false;
      }

      return IsFieldEqual(nameParser, $"HashBlockReferenced of anchor winner of IDToken {iDToken}", anchorExpected.HashBlockReferenced, anchorWinner.HashBlockReferenced, ref message);
    }

    protected static Block CreateBlock(List<BlockchainBToken.TXBToken> tXs)
    {
      Block block = new(HeaderBToken.COUNT_HEADER_BYTES + 1 + tXs.Sum(t => t.TXRaw.Length));
      block.TXs.AddRange(tXs);

      HeaderBToken header = CreateHeaderBToken(block.ComputeMerkleRoot());
      header.Height = 737858;

      block.Header = header;

      return block;
    }

    protected static Block ParseBlockSerialized(Block block)
    {
      block.Serialize();

      return ParseBlockBuffer(
        block.Buffer[..block.LengthDataPayload],
        new HeaderBToken
        {
          Hash = block.Header.Hash,
          HashPrevious = block.Header.HashPrevious,
          Height = block.Header.Height
        });
    }

    protected static Block ParseBlockBuffer(byte[] buffer, HeaderBToken headerExpected)
    {
      Block blockParsed = new(buffer)
      {
        Header = headerExpected
      };

      new ParserBToken(NAME_ID_TOKEN_BTOKEN).ParseBlock(blockParsed, verifySignatures: true);

      return blockParsed;
    }

    protected static bool IsFieldEqual(string nameParser, string nameField, byte[] expected, byte[] parsed, ref string message)
    {
      if (expected.AsSpan().SequenceEqual(parsed))
        return true;

      message = $"{nameParser}: field {nameField} is {parsed.ToHexString()} after parsing, expected {expected.ToHexString()}.";
      return false;
    }

    protected static bool IsFieldEqual<T>(string nameParser, string nameField, T expected, T parsed, ref string message)
      where T : struct
    {
      if (EqualityComparer<T>.Default.Equals(expected, parsed))
        return true;

      message = $"{nameParser}: field {nameField} is {parsed} after parsing, expected {expected}.";
      return false;
    }

    protected const int SIZE_BLOCK_MAX = 1 << 20;

    protected static BlockchainBToken.TXBToken ParseTXRaw(byte[] tXRaw)
    {
      int index = 0;
      return new BlockchainBToken.TXBToken(tXRaw, ref index, isCoinbase: false, verifySignatures: true, ID_TOKEN_BTOKEN);
    }

    protected static bool IsRejected(string nameRule, Action action, ref string message)
    {
      try
      {
        action();
      }
      catch (ProtocolException)
      {
        return true;
      }
      catch (Exception exception)
      {
        message = $"{nameRule}: threw {exception.GetType().Name} instead of rejecting with ProtocolException: {exception.Message}";
        return false;
      }

      message = $"{nameRule}: accepted the invalid case.";
      return false;
    }

    protected static bool IsAccepted(string nameRule, Action action, ref string message)
    {
      try
      {
        action();
      }
      catch (Exception exception)
      {
        message = $"{nameRule}: rejected the valid case with {exception.GetType().Name}: {exception.Message}";
        return false;
      }

      return true;
    }
  }
}
