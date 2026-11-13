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
        && Test_TXRules(Testbench.Wallet, ref message);
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
