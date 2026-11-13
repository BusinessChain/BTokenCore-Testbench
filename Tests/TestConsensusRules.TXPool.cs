using BTokenCore;

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  partial class TestConsensusRules
  {
    abstract partial class BlockchainAccounts
    {
      const int COUNT_TXS_MAX_ACCOUNT = 25;
      const long COUNT_BYTES_MAX_POOL = 100 * SIZE_BLOCK_MAX;
      const long BALANCE_WALLET = 1_000_000_000;

      internal static bool TestSpendAdmission(Wallet wallet, ref string message)
      {
        Console.WriteLine("  22. Spend Admission: Admit TXs by LockTime, sweep, Nonce, Balance and count.");

        Ledger ledger = new(heightRoot: 0);

        byte[] iDMiner = CreateIDAccount(1000, 1);
        byte[] iDReceiver = CreateIDAccount(1001, 2);

        if (!ledger.TryInsertBlock("coinbase creating the wallet account", [ledger.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, 0)], isValidExpected: true, null, ref message))
          return false;

        PoolTXBToken poolCount = ledger.CreateTXPool(COUNT_BYTES_MAX_POOL);

        for (int i = 0; i <= COUNT_TXS_MAX_ACCOUNT; i++)
          if (!IsAdmitted($"spend number {i + 1} of one account", poolCount, ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => t.Nonce += i), i < COUNT_TXS_MAX_ACCOUNT, ref message))
            return false;

        int heightTip = ledger.GetHeightTip();
        long balanceLeft = ledger.GetBalanceNext(wallet.Hash160PKeyPublic) - 2 * (VALUE_SPEND + FEE);

        PoolTXBToken pool = ledger.CreateTXPool(COUNT_BYTES_MAX_POOL);

        if (!IsAdmitted("anchor TX with LockTime below the tip height", pool, ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => AddAnchor(t, heightTip - 1)), false, ref message)
          || !IsAdmitted("anchor TX with LockTime above the tip height", pool, ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => AddAnchor(t, heightTip + 1)), false, ref message)
          || !IsAdmitted("anchor TX with LockTime of the tip height", pool, ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => AddAnchor(t, heightTip)), true, ref message)
          || !IsAdmitted("spend with the Nonce of the pooled anchor TX", pool, ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => { }), false, ref message)
          || !IsAdmitted("spend with a Nonce leaving a gap", pool, ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => t.Nonce += 2), false, ref message)
          || !IsAdmitted("spend with the Nonce after the pooled anchor TX", pool, ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t => t.Nonce += 1), true, ref message)
          || !IsAdmitted("spend of one more than the Balance left after the pooled TXs", pool, ledger.CreateTXSpend(wallet, iDReceiver, balanceLeft - FEE + 1, t => t.Nonce += 2), false, ref message)
          || !IsAdmitted("spend of the Balance left after the pooled TXs", pool, ledger.CreateTXSpend(wallet, iDReceiver, balanceLeft - FEE, t => t.Nonce += 2), true, ref message)
          || !IsAdmitted("TX paying nothing from the account spent to zero in the pool", pool, ledger.CreateTXSpend(wallet, iDReceiver, 0, t =>
            {
              t.Nonce += 3;
              t.Fee = 0;
              t.TXOutputs.Clear();
            }), false, ref message)
          || !ledger.TryInsertBlockFromPool("block of the pooled anchor TX and spends", pool, wallet, iDMiner, 3, ref message))
          return false;

        int heightRootSweep = (GetIndexSegment(wallet.Hash160PKeyPublic) - 2 + COUNT_SEGMENTS) % COUNT_SEGMENTS;

        return IsAdmittedBeforeSweep(wallet, iDMiner, iDReceiver, heightRootSweep, RENT_SWEEP, false, ref message)
          && IsAdmittedBeforeSweep(wallet, iDMiner, iDReceiver, heightRootSweep, RENT_SWEEP + 1, true, ref message);
      }

      internal static bool TestCreationAdmission(Wallet wallet, ref string message)
      {
        Console.WriteLine("  23. Creation Admission: Count pending account creations against CountAccountsMax and sweeps.");

        Ledger ledger = new(heightRoot: 0);

        int indexSegmentCreation = GetIndexSegment(wallet.Hash160PKeyPublic) == 4 ? 5 : 4;
        byte[] iDMiner = CreateIDAccount(1000, 1);
        byte[] iDExisting = CreateIDAccount(indexSegmentCreation, 1);
        byte[] iDCreatedPending = CreateIDAccount(indexSegmentCreation, 200);
        byte[] iDCreatedOverLimit = CreateIDAccount(indexSegmentCreation, 201);
        byte[] iDCreatedAfterSweep = CreateIDAccount(indexSegmentCreation, 202);

        if (!ledger.TryInsertBlock("coinbase creating the wallet account", [ledger.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, 0)], isValidExpected: true, null, ref message)
          || !ledger.TryInsertBlock("coinbase creating 99 accounts in one segment", [CreateCoinbaseAccounts(ledger, wallet, indexSegmentCreation, 99)], isValidExpected: true, null, ref message))
          return false;

        PoolTXBToken pool = ledger.CreateTXPool(COUNT_BYTES_MAX_POOL);

        if (!IsAdmitted("spend creating two accounts in a segment with room for one", pool, ledger.CreateTXSpend(wallet, iDCreatedPending, VALUE_SPEND, t => t.TXOutputs.Add(new TXOutputP2PKH(VALUE_SPEND, iDCreatedOverLimit))), false, ref message)
          || !IsAdmitted("spend creating one account in a segment with room for one", pool, ledger.CreateTXSpend(wallet, iDCreatedPending, VALUE_SPEND, t => { }), true, ref message)
          || !IsAdmitted("spend creating a second account beside a pending creation", pool, ledger.CreateTXSpend(wallet, iDCreatedOverLimit, VALUE_SPEND, t => t.Nonce += 1), false, ref message)
          || !IsAdmitted("spend to the account of a pending creation", pool, ledger.CreateTXSpend(wallet, iDCreatedPending, VALUE_SPEND, t => t.Nonce += 1), true, ref message)
          || !IsAdmitted("spend to an existing account in the full segment", pool, ledger.CreateTXSpend(wallet, iDExisting, VALUE_SPEND, t => t.Nonce += 2), true, ref message)
          || !ledger.TryInsertBlockFromPool("block of the pooled spends creating one account", pool, wallet, iDMiner, 3, ref message)
          || !TryInsertCoinbasesUpTo(ledger, wallet, iDMiner, indexSegmentCreation - 1, ref message))
          return false;

        return IsAdmitted("spend creating two accounts in a full segment the next sweep empties", ledger.CreateTXPool(COUNT_BYTES_MAX_POOL), ledger.CreateTXSpend(wallet, iDCreatedOverLimit, VALUE_SPEND, t => t.TXOutputs.Add(new TXOutputP2PKH(VALUE_SPEND, iDCreatedAfterSweep))), true, ref message);
      }

      internal static bool TestBundleRanking(Wallet wallet, ref string message)
      {
        Console.WriteLine("  24. Bundle Ranking: Merge bundles, rank by fee per byte, select by size.");

        Ledger ledger = new(heightRoot: 0);

        Wallet walletB = CreateWallet();
        Wallet walletC = CreateWallet();
        byte[] iDMiner = CreateIDAccount(1000, 1);
        byte[] iDReceiver = CreateIDAccount(1001, 2);

        TXBToken tXCoinbaseWallets = ledger.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, 0,
          new TXOutputP2PKH(BALANCE_WALLET, walletB.Hash160PKeyPublic),
          new TXOutputP2PKH(BALANCE_WALLET, walletC.Hash160PKeyPublic));

        if (!ledger.TryInsertBlock("coinbase creating three wallet accounts", [tXCoinbaseWallets], isValidExpected: true, null, ref message))
          return false;

        TXBToken tXA1 = CreateTXSpendFee(ledger, wallet, iDReceiver, 0, 1000);
        TXBToken tXA2 = CreateTXSpendFee(ledger, wallet, iDReceiver, 1, 31000);
        TXBToken tXB1 = CreateTXSpendFee(ledger, walletB, iDReceiver, 0, 8000);
        TXBToken tXB2 = CreateTXSpendFee(ledger, walletB, iDReceiver, 1, 0);
        TXBToken tXC1 = CreateTXSpendFee(ledger, walletC, iDReceiver, 0, 12000);
        TXBToken tXC2 = CreateTXSpendFee(ledger, walletC, iDReceiver, 1, 4000);
        TXBToken tXC3 = CreateTXSpendFee(ledger, walletC, iDReceiver, 2, 0);

        PoolTXBToken pool = ledger.CreateTXPool(COUNT_BYTES_MAX_POOL);

        foreach (TXBToken tX in new List<TXBToken> { tXA1, tXB1, tXC1, tXA2, tXC2, tXB2, tXC3 })
          if (!IsAdmitted($"spend with Nonce {tX.Nonce} and Fee {tX.Fee} for ranking", pool, tX, true, ref message))
            return false;

        int countBytesSelected = tXA1.TXRaw.Length + tXA2.TXRaw.Length + tXC1.TXRaw.Length + tXB1.TXRaw.Length - 1;

        return AreTXsRankedCorrect("GetTXsRanked of merged and separate bundles", pool.GetTXsRanked(), [tXA1, tXA2, tXC1, tXB1, tXC2, tXB2, tXC3], ref message)
          && AreTXsRankedCorrect("GetTXs stopping at the first TX over the byte limit", pool.GetTXs(countBytesSelected).Cast<TXBToken>(), [tXA1, tXA2, tXC1], ref message)
          && ledger.TryInsertBlockFromPool("block of all ranked TXs", pool, wallet, iDMiner, 7, ref message);
      }

      static Wallet CreateWallet()
      {
        string pathWallet = Path.GetTempFileName();

        try
        {
          File.WriteAllText(pathWallet, Crypto.GenerateKeyPrivate());

          return new Wallet(pathWallet);
        }
        finally
        {
          File.Delete(pathWallet);
        }
      }

      static TXBToken CreateTXSpendFee(Ledger ledger, Wallet wallet, byte[] iDReceiver, int offsetNonce, long fee)
      {
        return ledger.CreateTXSpend(wallet, iDReceiver, VALUE_SPEND, t =>
        {
          t.Nonce += offsetNonce;
          t.Fee = fee;
        });
      }

      static bool AreTXsRankedCorrect(string nameCase, IEnumerable<TXBToken> tXs, List<TXBToken> tXsExpected, ref string message)
      {
        List<TXBToken> tXsRanked = tXs.ToList();

        for (int i = 0; i < Math.Max(tXsRanked.Count, tXsExpected.Count); i++)
          if (i >= tXsRanked.Count || i >= tXsExpected.Count || !tXsRanked[i].Hash.IsAllBytesEqual(tXsExpected[i].Hash))
          {
            message = $"TX pool, {nameCase}: position {i} holds {(i < tXsRanked.Count ? tXsRanked[i].Hash.ToHexString() : "no TX")}, " +
              $"expected {(i < tXsExpected.Count ? tXsExpected[i].Hash.ToHexString() : "no TX")}.";
            return false;
          }

        return true;
      }

      static bool IsAdmittedBeforeSweep(Wallet wallet, byte[] iDMiner, byte[] iDReceiver, int heightRoot, long balanceWallet, bool isAdmittedExpected, ref string message)
      {
        Ledger ledger = new(heightRoot);

        return ledger.TryInsertBlock($"coinbase creating the wallet account with Balance {balanceWallet} before its sweep", [ledger.CreateCoinbase(wallet, iDMiner, 0, new TXOutputP2PKH(balanceWallet, wallet.Hash160PKeyPublic))], isValidExpected: true, null, ref message)
          && IsAdmitted($"spend of 1 from the wallet account with Balance {balanceWallet} before its sweep", ledger.CreateTXPool(COUNT_BYTES_MAX_POOL), ledger.CreateTXSpend(wallet, iDReceiver, 1, t => t.Fee = 0), isAdmittedExpected, ref message);
      }

      static bool IsAdmitted(string nameCase, PoolTXBToken pool, TXBToken tX, bool isAdmittedExpected, ref string message)
      {
        string nameRule = $"TX pool, {nameCase}";

        if (isAdmittedExpected)
          return IsAccepted(nameRule, () => pool.AddTX(ParseTXRaw(tX.TXRaw)), ref message);

        return IsRejected(nameRule, () => pool.AddTX(ParseTXRaw(tX.TXRaw)), ref message);
      }

      partial class Ledger
      {
        internal PoolTXBToken CreateTXPool(long countBytesMaxPool)
        {
          return new PoolTXBToken(Accounts, countBytesMaxPool);
        }

        internal bool TryInsertBlockFromPool(string nameCase, PoolTXBToken pool, Wallet wallet, byte[] iDMiner, int countTXsExpected, ref string message)
        {
          List<TXBToken> tXs = pool.GetTXs(SIZE_BLOCK_MAX).Cast<TXBToken>().ToList();

          if (tXs.Count != countTXsExpected)
          {
            message = $"TX pool, {nameCase}: GetTXs selects {tXs.Count} TXs, expected {countTXsExpected}.";
            return false;
          }

          return TryInsertBlock(nameCase, [CreateCoinbase(wallet, iDMiner, tXs.Sum(t => t.Fee)), .. tXs], isValidExpected: true, null, ref message);
        }
      }
    }
  }
}
