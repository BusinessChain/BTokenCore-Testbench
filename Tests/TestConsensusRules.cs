using System.Numerics;
using System.Security.Cryptography;

using BTokenCore;

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  class TestConsensusRules : Test_Testbench
  {
    internal TestConsensusRules(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      return Test_BlockRules(Testbench.Wallet, ref message)
        && Test_TXRules(Testbench.Wallet, ref message)
        && Test_HeaderBitcoinRules(ref message);
    }

    const uint UNIX_TIME_START = 1_600_000_000;
    const uint SECONDS_BLOCK = 600;
    const uint SECONDS_TWO_WEEKS = 14 * 24 * 60 * 60;
    const uint NBITS_MAINNET = 0x17096a20;
    const uint NBITS_DIFFICULTY_1 = 0x1d00ffff;
    const int COUNT_HEADERS_RETARGET = 2016;
    const int INDEX_UNIX_TIME = 68;
    const int INDEX_NONCE = 76;

    bool Test_HeaderBitcoinRules(ref string message)
    {
      Console.WriteLine("  3. Reject HeaderBitcoin with premature timestamp, failed proof of work, wrong HashPrevious, time below median or wrong NBits.");

      byte[] headerRawNonceChanged = Convert.FromHexString(HEADER_BITCOIN_737857);
      headerRawNonceChanged[INDEX_NONCE] ^= 0x01;

      byte[] headerRawPremature = Convert.FromHexString(HEADER_BITCOIN_737857);
      BitConverter.GetBytes((uint)DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeSeconds()).CopyTo(headerRawPremature, INDEX_UNIX_TIME);

      HeaderBitcoin headerTip = CreateChainBitcoin(11, SECONDS_BLOCK, NBITS_MAINNET);
      uint unixTimeMedian = UNIX_TIME_START + 5 * SECONDS_BLOCK;

      AppendHeaderBitcoin(headerTip, unixTimeMedian, NBITS_MAINNET);

      return IsRejected("HeaderBitcoin with Nonce changed", () => ParseHeaderBitcoin(headerRawNonceChanged), ref message)
        && IsRejected("HeaderBitcoin with timestamp three hours ahead", () => ParseHeaderBitcoin(headerRawPremature), ref message)
        && IsRejected("HeaderBitcoin with wrong HashPrevious", () =>
          CreateHeaderBitcoin(SHA256.HashData(headerTip.Hash), headerTip.UnixTimeSeconds + SECONDS_BLOCK, NBITS_MAINNET).AppendToHeader(headerTip), ref message)
        && IsRejected("HeaderBitcoin with timestamp below median time past", () => AppendHeaderBitcoin(headerTip, unixTimeMedian - 1, NBITS_MAINNET), ref message)
        && IsRejected("HeaderBitcoin with NBits changed outside retarget", () => AppendHeaderBitcoin(headerTip, headerTip.UnixTimeSeconds + SECONDS_BLOCK, NBITS_MAINNET + 1), ref message)
        && IsRetargetCorrect(NBITS_MAINNET, SECONDS_TWO_WEEKS, ref message)
        && IsRetargetCorrect(NBITS_MAINNET, SECONDS_TWO_WEEKS / 2, ref message)
        && IsRetargetCorrect(NBITS_MAINNET, 5 * SECONDS_TWO_WEEKS, ref message)
        && IsRetargetCorrect(NBITS_DIFFICULTY_1, 2 * SECONDS_TWO_WEEKS, ref message);
    }

    static void ParseHeaderBitcoin(byte[] headerRaw)
    {
      int index = 0;
      _ = new HeaderBitcoin(headerRaw, ref index);
    }

    static HeaderBitcoin CreateHeaderBitcoin(byte[] hashPrevious, uint unixTimeSeconds, uint nBits)
    {
      HeaderBitcoin header = new(
        headerHash: null,
        version: 0x20000000,
        hashPrevious: hashPrevious,
        merkleRootHash: new byte[32],
        unixTimeSeconds: unixTimeSeconds,
        nBits: nBits,
        nonce: 0);

      header.ComputeHash();

      return header;
    }

    static HeaderBitcoin AppendHeaderBitcoin(HeaderBitcoin headerPrevious, uint unixTimeSeconds, uint nBits)
    {
      HeaderBitcoin header = CreateHeaderBitcoin(headerPrevious.Hash, unixTimeSeconds, nBits);
      header.AppendToHeader(headerPrevious);

      return header;
    }

    static HeaderBitcoin CreateChainBitcoin(int countHeaders, uint secondsBetweenHeaders, uint nBits)
    {
      HeaderBitcoin header = CreateHeaderBitcoin(new byte[32], UNIX_TIME_START, nBits);

      for (int i = 1; i < countHeaders; i++)
        header = AppendHeaderBitcoin(header, header.UnixTimeSeconds + secondsBetweenHeaders, nBits);

      return header;
    }

    static bool IsRetargetCorrect(uint nBits, uint secondsTimespan, ref string message)
    {
      HeaderBitcoin headerTip = CreateChainBitcoin(COUNT_HEADERS_RETARGET - 1, secondsTimespan / (COUNT_HEADERS_RETARGET - 1), nBits);
      headerTip = AppendHeaderBitcoin(headerTip, UNIX_TIME_START + secondsTimespan, nBits);

      uint nBitsExpected = ComputeNBitsRetarget(nBits, secondsTimespan);
      string nameRule = $"Retarget of NBits {nBits:x8} after a timespan of {secondsTimespan} seconds to {nBitsExpected:x8}";

      try
      {
        AppendHeaderBitcoin(headerTip, headerTip.UnixTimeSeconds + SECONDS_BLOCK, nBitsExpected);
      }
      catch (ProtocolException exception)
      {
        message = $"{nameRule}: rejected the expected NBits: {exception.Message}";
        return false;
      }

      return IsRejected(nameRule, () => AppendHeaderBitcoin(headerTip, headerTip.UnixTimeSeconds + SECONDS_BLOCK, nBitsExpected + 1), ref message);
    }

    static uint ComputeNBitsRetarget(uint nBits, uint secondsTimespan)
    {
      uint secondsClamped = Math.Clamp(secondsTimespan, SECONDS_TWO_WEEKS / 4, SECONDS_TWO_WEEKS * 4);

      BigInteger target = DecodeCompact(nBits) * secondsClamped / SECONDS_TWO_WEEKS;

      return EncodeCompact(BigInteger.Min(target, DecodeCompact(NBITS_DIFFICULTY_1)));
    }

    static BigInteger DecodeCompact(uint compact)
    {
      int exponent = (int)(compact >> 24);
      BigInteger mantissa = compact & 0x007fffff;

      return exponent <= 3 ? mantissa >> 8 * (3 - exponent) : mantissa << 8 * (exponent - 3);
    }

    static uint EncodeCompact(BigInteger target)
    {
      int size = target.GetByteCount(isUnsigned: true);
      uint mantissa = (uint)(size <= 3 ? target << 8 * (3 - size) : target >> 8 * (size - 3));

      if ((mantissa & 0x00800000) != 0)
      {
        mantissa >>= 8;
        size += 1;
      }

      return mantissa | (uint)size << 24;
    }

    const long SUPPLY_MAX = 2 * 20_000_000_000 * 52_500;
    const long VALUE_OUTPUT = 50000;

    bool Test_TXRules(Wallet wallet, ref string message)
    {
      Console.WriteLine("  2. Reject TXs with invalid Fee, Value, script, signature or count of outputs, and check IsLockTimeValid.");

      ParseTXRaw(CreateTXRawSigned(wallet, t =>
      {
        t.Fee = SUPPLY_MAX;
        t.TXOutputs.Clear();
      }));

      ParseTXRaw(CreateTXRawSigned(wallet, t => t.Fee = SUPPLY_MAX - VALUE_OUTPUT));

      byte[] tXRawSignatureFlipped = CreateTXRawSigned(wallet, t => { });
      tXRawSignatureFlipped[^1] ^= 0x01;

      byte[] tXRawBodyChanged = CreateTXRawSigned(wallet, t => { });
      tXRawBodyChanged[LENGTH_PUBKEY + 2 * sizeof(int) + 1] ^= 0x01;

      byte[] tXRawCountOutputsTooLarge = [
        .. CreateTXRawSigned(wallet, t => { })[..(LENGTH_PUBKEY + 2 * sizeof(int) + sizeof(long))],
        0xff,
        .. BitConverter.GetBytes((ulong)int.MaxValue + 1)];

      return IsRejected("TX with Fee minus one", () => ParseTXRaw(CreateTXRawSigned(wallet, t => t.Fee = -1)), ref message)
        && IsRejected("TX with Fee above SUPPLY_MAX", () => ParseTXRaw(CreateTXRawSigned(wallet, t =>
          {
            t.Fee = SUPPLY_MAX + 1;
            t.TXOutputs.Clear();
          })), ref message)
        && IsRejected("TX with Fee and Values above SUPPLY_MAX", () => ParseTXRaw(CreateTXRawSigned(wallet, t => t.Fee = SUPPLY_MAX - VALUE_OUTPUT + 1)), ref message)
        && IsRejected("TX with Value minus one", () => ParseTXRaw(CreateTXRawSigned(wallet, t => t.TXOutputs[0] = new TXOutputP2PKH(-1, wallet.Hash160PKeyPublic))), ref message)
        && IsRejected("TX with two Values above SUPPLY_MAX", () => ParseTXRaw(CreateTXRawSigned(wallet, t =>
          {
            t.TXOutputs[0] = new TXOutputP2PKH(SUPPLY_MAX / 2 + 1, wallet.Hash160PKeyPublic);
            t.TXOutputs.Add(new TXOutputP2PKH(SUPPLY_MAX / 2 + 1, wallet.Hash160PKeyPublic));
          })), ref message)
        && IsRejected("TX with unknown output script", () => ParseTXRaw(CreateTXRawSigned(wallet, t => t.TXOutputs.Add(new TXOutput { Script = [0x6a, 0x01, 0x02] }))), ref message)
        && IsRejected("TX with flipped signature byte", () => ParseTXRaw(tXRawSignatureFlipped), ref message)
        && IsRejected("TX with body changed after signing", () => ParseTXRaw(tXRawBodyChanged), ref message)
        && IsRejected("TX with count of outputs above int.MaxValue", () => ParseTXRaw(tXRawCountOutputsTooLarge), ref message)
        && IsLockTimeValidExpected(wallet, hasAnchor: true, offsetLockTime: -1, isValidExpected: true, ref message)
        && IsLockTimeValidExpected(wallet, hasAnchor: true, offsetLockTime: -2, isValidExpected: false, ref message)
        && IsLockTimeValidExpected(wallet, hasAnchor: true, offsetLockTime: 0, isValidExpected: false, ref message)
        && IsLockTimeValidExpected(wallet, hasAnchor: false, offsetLockTime: -2, isValidExpected: true, ref message)
        && IsLockTimeValidExpected(wallet, hasAnchor: false, offsetLockTime: 5, isValidExpected: true, ref message);
    }

    const int LENGTH_PUBKEY = 33;
    const int HEIGHT_BLOCK = 737858;

    static byte[] CreateTXRawSigned(Wallet wallet, Action<BlockchainBToken.TXBToken> alter)
    {
      BlockchainBToken.TXBToken tX = CreateTXBToken(wallet.KeyPublic, wallet.Hash160PKeyPublic);
      alter(tX);
      tX.Serialize(wallet);

      return tX.TXRaw;
    }

    static void ParseTXRaw(byte[] tXRaw)
    {
      int index = 0;
      _ = new BlockchainBToken.TXBToken(tXRaw, ref index, isCoinbase: false, verifySignatures: true);
    }

    static bool IsLockTimeValidExpected(Wallet wallet, bool hasAnchor, int offsetLockTime, bool isValidExpected, ref string message)
    {
      BlockchainBToken.TXBToken tX = hasAnchor
        ? CreateTXBToken(wallet.KeyPublic, wallet.Hash160PKeyPublic, CreateTXOutputTokenAnchor(ID_TOKEN, 0))
        : CreateTXBToken(wallet.KeyPublic, wallet.Hash160PKeyPublic);

      tX.LockTime = HEIGHT_BLOCK + offsetLockTime;

      return IsFieldEqual(
        $"IsLockTimeValid of TX {(hasAnchor ? "with" : "without")} anchor at LockTime {tX.LockTime} in block {HEIGHT_BLOCK}",
        "result",
        isValidExpected,
        tX.IsLockTimeValid(HEIGHT_BLOCK),
        ref message);
    }

    bool Test_BlockRules(Wallet wallet, ref string message)
    {
      Console.WriteLine("  1. Reject blocks with unexpected header, without TXs, with a TX twice, mutated or with wrong MerkleRoot.");

      BlockchainBToken.TXBToken tXCoinbase = CreateTXBToken(wallet.KeyPublic, wallet.Hash160PKeyPublic);
      tXCoinbase.SerializeUnsigned();

      BlockchainBToken.TXBToken tX = CreateTXBToken(wallet.KeyPublic, wallet.Hash160PKeyPublic);
      tX.Serialize(wallet);

      BlockchainBToken.TXBToken tXOther = CreateTXBToken(wallet.KeyPublic, wallet.Hash160PKeyPublic);
      tXOther.Nonce += 1;
      tXOther.Serialize(wallet);

      Block block = CreateBlock([tXCoinbase, tX]);
      block.Serialize();
      byte[] bufferBlock = block.Buffer[..block.LengthDataPayload];

      HeaderBToken headerHashOther = new()
      {
        Hash = SHA256.HashData(block.Header.Hash),
        HashPrevious = block.Header.HashPrevious,
        Height = block.Header.Height
      };

      HeaderBToken headerHashPreviousOther = new()
      {
        Hash = block.Header.Hash,
        HashPrevious = SHA256.HashData(block.Header.HashPrevious),
        Height = block.Header.Height
      };

      HeaderBToken headerExpected = new()
      {
        Hash = block.Header.Hash,
        HashPrevious = block.Header.HashPrevious,
        Height = block.Header.Height
      };

      Block blockMutated = CreateBlock([tXCoinbase, tX, tXOther, tXOther]);

      if (!blockMutated.Header.MerkleRoot.IsAllBytesEqual(CreateBlock([tXCoinbase, tX, tXOther]).Header.MerkleRoot))
      {
        message = "Block rules: the mutated block doesn't keep the merkle root of the block without its repeated TX.";
        return false;
      }

      Block blockMerkleRootWrong = CreateBlock([tXCoinbase, tX]);
      blockMerkleRootWrong.Header.MerkleRoot = SHA256.HashData(blockMerkleRootWrong.Header.MerkleRoot);
      blockMerkleRootWrong.Header.ComputeHash();

      return IsRejected("Block with unexpected Hash", () => ParseBlockBuffer(bufferBlock, headerHashOther), ref message)
        && IsRejected("Block with unexpected HashPrevious", () => ParseBlockBuffer(bufferBlock, headerHashPreviousOther), ref message)
        && IsRejected("Block without TXs", () => ParseBlockBuffer([.. block.Header.Serialize(), 0x00], headerExpected), ref message)
        && IsRejected("Block with a TX twice", () => ParseBlockSerialized(CreateBlock([tXCoinbase, tX, tX])), ref message)
        && IsRejected("Mutated block repeating its last TX", () => ParseBlockSerialized(blockMutated), ref message)
        && IsRejected("Block with wrong MerkleRoot", () => ParseBlockSerialized(blockMerkleRootWrong), ref message);
    }

    static bool IsRejected(string nameRule, Action action, ref string message)
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
  }
}
