using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BTokenCore;


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

      return Test_HeaderBToken(ref message)
        && Test_HeaderBitcoin(ref message)
        && Test_TXBToken(ref message);
    }

    const int COUNT_BYTES_LEADING = 7;

    static byte[] EmbedInBuffer(byte[] data)
    {
      return [0xff, 0xfe, 0xfd, 0xfc, 0xfb, 0xfa, 0xf9, .. data, 0xf8];
    }

    bool Test_HeaderBToken(ref string message)
    {
      Console.WriteLine("  Serialize a HeaderBToken and parse it back at index 0 and behind leading bytes.");

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

    bool Test_HeaderBitcoin(ref string message)
    {
      Console.WriteLine("  Parse the raw Bitcoin mainnet header of block 737857 and check its known hash and fields.");

      byte[] headerRaw = Convert.FromHexString(
        "0080d121824471242bb8751e2363732c0c6b91561eb81dbbd9300200000000000000000017478653c5ffd216f7c3dee1dc4bd790" +
        "b10dd2ffb2cbc30983dad4633004385a85458e62206a0917627da442");

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

    bool Test_TXBToken(ref string message)
    {
      Console.WriteLine("  Serialize a wallet-signed and a coinbase TXBToken and parse them back at index 0 and behind leading bytes.");

      string pathWallet = Path.GetTempFileName();

      try
      {
        File.WriteAllText(pathWallet, Crypto.GenerateKeyPrivate());

        Wallet wallet = new(pathWallet);

        BlockchainBToken.TXBToken tX = CreateTXBToken(wallet.KeyPublic, wallet.Hash160PKeyPublic);
        tX.Serialize(wallet);

        BlockchainBToken.TXBToken tXCoinbase = CreateTXBToken(wallet.KeyPublic, wallet.Hash160PKeyPublic);
        tXCoinbase.SerializeUnsigned();

        return IsTXBTokenParsedEqual(tX, tX.TXRaw, 0, isCoinbase: false, ref message)
          && IsTXBTokenParsedEqual(tX, EmbedInBuffer(tX.TXRaw), COUNT_BYTES_LEADING, isCoinbase: false, ref message)
          && IsTXBTokenParsedEqual(tXCoinbase, tXCoinbase.TXRaw, 0, isCoinbase: true, ref message)
          && IsTXBTokenParsedEqual(tXCoinbase, EmbedInBuffer(tXCoinbase.TXRaw), COUNT_BYTES_LEADING, isCoinbase: true, ref message);
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
}
