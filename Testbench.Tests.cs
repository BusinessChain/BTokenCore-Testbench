using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BTokenCore;


namespace BTokenCore_Testbench;

internal partial class Testbench
{
  abstract class Test_Testbench
  {
    protected Testbench Testbench;

    internal Test_Testbench(Testbench testbench)
    {
      Testbench = testbench;
    }

    internal abstract bool TryRun(out string message);
  }

  class TestParsers : Test_Testbench
  {
    internal TestParsers(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      return Test_HeaderBToken(ref message)
        && Test_TXBToken(ref message);
    }

    const int COUNT_BYTES_LEADING = 7;

    static byte[] EmbedInBuffer(byte[] data)
    {
      return [0xff, 0xfe, 0xfd, 0xfc, 0xfb, 0xfa, 0xf9, .. data, 0xf8];
    }

    bool Test_HeaderBToken(ref string message)
    {
      HeaderBToken header = new(
        headerHash: null,
        hashPrevious: "00000000000000000002a7c4c1e48d76c5a37902165a270156b7a8d72728a054".ToBinary(),
        merkleRootHash: "5a38043063d4da8309c3cbb2ffd20db190d74bdce1dec3f716d2ffc553864717".ToBinary(),
        hashAccounts: "6e340b9cffb37a989ca544e6bb780a2c78901d3fb33738768511a30617afa01d".ToBinary(),
        nonce: 0x42a47d62);

      header.Version = 0x21d18000;
      header.ComputeHash();

      byte[] headerSerialized = header.Serialize();

      return IsHeaderBTokenParsedEqual(header, headerSerialized, 0, ref message)
        && IsHeaderBTokenParsedEqual(header, EmbedInBuffer(headerSerialized), COUNT_BYTES_LEADING, ref message);
    }

    static bool IsHeaderBTokenParsedEqual(HeaderBToken header, byte[] buffer, int indexStart, ref string message)
    {
      int index = indexStart;

      HeaderBToken headerParsed = new(buffer, ref index);

      string nameParser = $"{nameof(HeaderBToken)} parsed from index {indexStart}";

      return IsFieldEqual(nameParser, nameof(Header.Version), header.Version, headerParsed.Version, ref message)
        && IsFieldEqual(nameParser, nameof(Header.HashPrevious), header.HashPrevious, headerParsed.HashPrevious, ref message)
        && IsFieldEqual(nameParser, nameof(Header.MerkleRoot), header.MerkleRoot, headerParsed.MerkleRoot, ref message)
        && IsFieldEqual(nameParser, nameof(HeaderBToken.HashAccounts), header.HashAccounts, headerParsed.HashAccounts, ref message)
        && IsFieldEqual(nameParser, nameof(Header.Nonce), header.Nonce, headerParsed.Nonce, ref message)
        && IsFieldEqual(nameParser, nameof(Header.Hash), header.Hash, headerParsed.Hash, ref message)
        && IsFieldEqual(nameParser, "index after parsing", indexStart + HeaderBToken.COUNT_HEADER_BYTES, index, ref message);
    }

    bool Test_TXBToken(ref string message)
    {
      string pathWallet = Path.GetTempFileName();

      try
      {
        File.WriteAllText(pathWallet, Crypto.GenerateKeyPrivate());

        Wallet wallet = new(pathWallet);

        BlockchainBToken.TXBToken tX = CreateTXBToken(wallet.KeyPublic, wallet.Hash160PKeyPublic);
        tX.Serialize(wallet);

        return IsTXBTokenParsedEqual(tX, tX.TXRaw, 0, isCoinbase: false, ref message)
          && IsTXBTokenParsedEqual(tX, EmbedInBuffer(tX.TXRaw), COUNT_BYTES_LEADING, isCoinbase: false, ref message);
      }
      finally
      {
        File.Delete(pathWallet);
      }
    }

    static BlockchainBToken.TXBToken CreateTXBToken(byte[] keyPublic, byte[] iDAccountReceiving)
    {
      TXOutputTokenAnchor tXOutputTokenAnchor = new()
      {
        IDToken = [0x01, 0x02, 0x03, 0x04],
        HashBlockReferenced = "0000000000000000000735fc64773067fd0175461148902c97c494ffa3ca9306".ToBinary(),
        HashBlockPreviousReferenced = "0000000000000000000230d9bb1db81e56916b0c2c7363231e75b82b24714482".ToBinary()
      };

      tXOutputTokenAnchor.Serialize();

      BlockchainBToken.TXBToken tX = new()
      {
        KeyPublic = keyPublic,
        HeightAccountCreated = 1234,
        Nonce = 5,
        Fee = 1000,
        LockTime = 737857
      };

      tX.TXOutputs.Add(new TXOutputP2PKH(50000, iDAccountReceiving));
      tX.TXOutputs.Add(tXOutputTokenAnchor);

      return tX;
    }

    static bool IsTXBTokenParsedEqual(BlockchainBToken.TXBToken tX, byte[] buffer, int indexStart, bool isCoinbase, ref string message)
    {
      int index = indexStart;

      BlockchainBToken.TXBToken tXParsed = new(buffer, ref index, isCoinbase, verifySignatures: true);

      string nameParser = $"{nameof(BlockchainBToken.TXBToken)} parsed from index {indexStart}";

      return IsFieldEqual(nameParser, nameof(tX.KeyPublic), tX.KeyPublic, tXParsed.KeyPublic, ref message)
        && IsFieldEqual(nameParser, nameof(tX.IDAccountSource), Crypto.ComputeHash160(tX.KeyPublic), tXParsed.IDAccountSource, ref message)
        && IsFieldEqual(nameParser, nameof(tX.HeightAccountCreated), tX.HeightAccountCreated, tXParsed.HeightAccountCreated, ref message)
        && IsFieldEqual(nameParser, nameof(tX.Nonce), tX.Nonce, tXParsed.Nonce, ref message)
        && IsFieldEqual(nameParser, nameof(tX.Fee), tX.Fee, tXParsed.Fee, ref message)
        && IsFieldEqual(nameParser, nameof(tX.LockTime), tX.LockTime, tXParsed.LockTime, ref message)
        && IsFieldEqual(nameParser, nameof(tX.Hash), tX.Hash, tXParsed.Hash, ref message)
        && IsFieldEqual(nameParser, nameof(tX.TXRaw), tX.TXRaw, tXParsed.TXRaw, ref message)
        && IsFieldEqual(nameParser, nameof(tX.CountBytes), tX.TXRaw.Length, tXParsed.CountBytes, ref message)
        && AreTXOutputsParsedEqual(nameParser, tX.TXOutputs, tXParsed.TXOutputs, ref message)
        && IsFieldEqual(nameParser, "index after parsing", indexStart + tX.TXRaw.Length, index, ref message);
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

    static bool IsFieldEqual(string nameParser, string nameField, byte[] expected, byte[] parsed, ref string message)
    {
      if (expected.AsSpan().SequenceEqual(parsed))
        return true;

      message = $"{nameParser}: field {nameField} is {parsed.ToHexString()} after parsing, expected {expected.ToHexString()}.";
      return false;
    }

    static bool IsFieldEqual<T>(string nameParser, string nameField, T expected, T parsed, ref string message)
      where T : struct
    {
      if (EqualityComparer<T>.Default.Equals(expected, parsed))
        return true;

      message = $"{nameParser}: field {nameField} is {parsed} after parsing, expected {expected}.";
      return false;
    }
  }

  class TestModuleBlockchain : Test_Testbench
  {
    //Branch Blockchain;

    internal TestModuleBlockchain(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      //Blockchain = new(Testbench.CreateHeaderGenesis());

      message = "";

      if(Test_TryExtendHeaderchain(ref message)
        //&& Test_GetHeader(ref message)
        //&& Test_AppendHeader(ref message)
        //&& Test_FetchHeaderDownload(ref message)
        //&& Test_InsertBlockInChain(ref message)
        //&& Test_TryGetBlockNext(ref message)
        //&& Test_GetLocator(ref message)
        //&& Test_GetHeadersSerialized(ref message)
        )
        return true;

      return false;
    }

    bool Test_TryExtendHeaderchain(ref string messsage)
    {
      //HeaderBitcoin header737857 = new(
      //   headerHash: "0000000000000000000735fc64773067fd0175461148902c97c494ffa3ca9306".ToBinary(),
      //   version: 0x21d18000,
      //   hashPrevious: "0000000000000000000230d9bb1db81e56916b0c2c7363231e75b82b24714482".ToBinary(),
      //   merkleRootHash: "5a38043063d4da8309c3cbb2ffd20db190d74bdce1dec3f716d2ffc553864717".ToBinary(),
      //   unixTimeSeconds: 1653491077,
      //   nBits: 0x17096a20,
      //   nonce: 0x42a47d62);
      //HeaderBitcoin header737858 = new(
      //   headerHash: "0000000000000000000186f8f9c843648a0942e2eccc34e6614f48777451aa63".ToBinary(),
      //   version: 0x20200004,
      //   hashPrevious: "0000000000000000000735fc64773067fd0175461148902c97c494ffa3ca9306".ToBinary(),
      //   merkleRootHash: "41ccb67e2dffeabddd40c985f737fa7d62a6b2ab95a65712e6f18eb2a2b8e902".ToBinary(),
      //   unixTimeSeconds: 1653491090,
      //   nBits: 0x17096a20,
      //   nonce: 0x5a73d462);
      //HeaderBitcoin header737859 = new(
      //   headerHash: "00000000000000000007808c3027b98d88750122d752776c8556e781c881673f".ToBinary(),
      //   version: 0x2f25a004,
      //   hashPrevious: "0000000000000000000186f8f9c843648a0942e2eccc34e6614f48777451aa63".ToBinary(),
      //   merkleRootHash: "ad6ff2ad97e6db66396e9cea3d838e2c2ba9162ae167682d61e008ec808343fc".ToBinary(),
      //   unixTimeSeconds: 1653491330,
      //   nBits: 0x17096a20,
      //   nonce: 0x9c299771);

      //try
      //{
      //  header737858.TryAppendToHeader(header737857);
      //  header737857.HeaderNext = header737858;

      //  header737859.TryAppendToHeader(header737858);
      //  header737858.HeaderNext = header737859;
      //}
      //catch (Exception ex)
      //{
      //  messsage = $"{ex.GetType().Name} when appending headers:\n{ex.Message}";
      //  return false;
      //}

      //Blockchain chain = Blockchain.TryExtendHeaderchain(header737857);

      //if (Blockchain.HeaderRoot.HeaderNext != header737857
      //  || header737857.HeaderNext != header737858
      //  || header737858.HeaderNext != header737859)
      //{
      //  messsage = "TryExtendHeaderchain building chain incorrectly.";
      //  return false;
      //}

      return true;
    }

    bool Test_GetHeader(ref string messsage)
    {
      return false;
    }

    bool Test_AppendHeader(ref string messsage)
    {
      return false;
    }

    bool Test_FetchHeaderDownload(ref string messsage)
    {
      return false;
    }

    bool Test_InsertBlockInChain(ref string messsage)
    {
      return false;
    }

    bool Test_TryGetBlockNext(ref string messsage)
    {
      return false;
    }

    bool Test_GetLocator(ref string messsage)
    {
      return false;
    }

    bool Test_GetHeadersSerialized(ref string messsage)
    {
      return false;
    }
  }
}