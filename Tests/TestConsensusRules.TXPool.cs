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
      const double FEE_PER_BYTE_INCREMENT = 1;
      const int COUNT_OUTPUTS_FILLER = (SIZE_BLOCK_MAX - 300) / 34;

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

      internal static bool TestPoolEviction(Wallet wallet, ref string message)
      {
        Console.WriteLine("  25. Pool Eviction: Reject low bids, evict worst bundles, report minimum fee.");

        Ledger ledger = new(heightRoot: 0);

        Wallet walletB = CreateWallet();
        Wallet walletC = CreateWallet();
        Wallet walletD = CreateWallet();
        Wallet walletE = CreateWallet();
        byte[] iDReceiver = CreateIDAccount(1001, 2);

        TXBToken tXCoinbaseWallets = ledger.CreateCoinbase(wallet, wallet.Hash160PKeyPublic, 0,
          new TXOutputP2PKH(BALANCE_WALLET, walletB.Hash160PKeyPublic),
          new TXOutputP2PKH(BALANCE_WALLET, walletC.Hash160PKeyPublic),
          new TXOutputP2PKH(BALANCE_WALLET, walletD.Hash160PKeyPublic),
          new TXOutputP2PKH(BALANCE_WALLET, walletE.Hash160PKeyPublic));

        if (!ledger.TryInsertBlock("coinbase creating five wallet accounts", [tXCoinbaseWallets], isValidExpected: true, null, ref message))
          return false;

        TXBToken tXB1 = CreateTXSpendFee(ledger, walletB, iDReceiver, 0, 3200);
        TXBToken tXE1 = CreateTXSpendFee(ledger, walletE, iDReceiver, 0, 1600);
        TXBToken tXE2 = CreateTXSpendFee(ledger, walletE, iDReceiver, 1, 1920);
        TXBToken tXE2Paying = CreateTXSpendFee(ledger, walletE, iDReceiver, 1, 16000);

        TXBToken tXD1 = ledger.CreateTXSpend(walletD, iDReceiver, 0, t =>
        {
          t.Fee = 200L * SIZE_BLOCK_MAX;
          t.TXOutputs.AddRange(Enumerable.Range(1, COUNT_OUTPUTS_FILLER - 1).Select(i => new TXOutputP2PKH(0, iDReceiver)));
        });

        TXBToken tXC1Low = ledger.CreateTXSpend(walletC, iDReceiver, 0, t =>
        {
          t.Fee = 800;
          t.TXOutputs.Clear();
        });

        TXBToken tXC1 = ledger.CreateTXSpend(walletC, iDReceiver, 0, t =>
        {
          t.Fee = 5040;
          t.TXOutputs.Clear();
        });

        PoolTXBToken pool = ledger.CreateTXPool(tXB1.TXRaw.Length + tXD1.TXRaw.Length + tXE1.TXRaw.Length + tXE2.TXRaw.Length);

        double feePerByteMinFull = (double)(tXE1.Fee + tXE2.Fee) / (tXE1.TXRaw.Length + tXE2.TXRaw.Length) + FEE_PER_BYTE_INCREMENT;

        return IsAdmitted("spend at about 20 per byte", pool, tXB1, true, ref message)
          && IsFeePerByteMinCorrect("pool with more than one block of room", pool, 0, ref message)
          && IsAdmitted("filler TX of almost one block", pool, tXD1, true, ref message)
          && IsAdmitted("first spend of the worst bundle", pool, tXE1, true, ref message)
          && IsAdmitted("second spend of the worst bundle filling the pool to its cap", pool, tXE2, true, ref message)
          && IsFeePerByteMinCorrect("pool at its cap", pool, feePerByteMinFull, ref message)
          && IsAdmitted("TX paying only a Fee below the outbid of the full pool", pool, tXC1Low, false, ref message)
          && IsAdmitted("TX paying only a Fee above the outbid of the full pool", pool, tXC1, true, ref message)
          && AreTXsRankedCorrect("pool after evicting the last TX of the worst bundle", pool.GetTXsRanked(), [tXD1, tXC1, tXB1, tXE1], ref message)
          && IsAdmitted("evicted spend merging into the worst bundle again", pool, tXE2, false, ref message)
          && IsAdmitted("evicted spend replaced with a higher Fee", pool, tXE2Paying, true, ref message)
          && AreTXsRankedCorrect("pool after evicting the worst bundle of a single TX", pool.GetTXsRanked(), [tXD1, tXE1, tXE2Paying, tXC1], ref message);
      }

      internal static bool TestBlockInsertion(Wallet wallet, ref string message)
      {
        Console.WriteLine("  26. Block Insertion: Drop confirmed, expired, conflicting, swept and over-limit TXs.");

        const long BALANCE_WALLET_SWEPT = 100000;

        Wallet walletB = CreateWallet();
        Wallet walletC = CreateWallet();
        Wallet walletD = CreateWallet();
        Wallet walletE = CreateWallet();

        int indexSegmentE = GetIndexSegment(walletE.Hash160PKeyPublic);
        HashSet<int> indicesSegmentWallets = [.. new[] { wallet, walletB, walletC, walletD, walletE }.Select(w => GetIndexSegment(w.Hash160PKeyPublic))];
        int indexSegmentCreation = Enumerable.Range(100, COUNT_SEGMENTS)
          .Select(i => (indexSegmentE + i) % COUNT_SEGMENTS)
          .First(i => !indicesSegmentWallets.Contains(i));

        byte[] iDMiner = CreateIDAccount(indexSegmentCreation, 0);
        byte[] iDCreatedPending = CreateIDAccount(indexSegmentCreation, 200);
        byte[] iDCreatedByBlock = CreateIDAccount(indexSegmentCreation, 201);

        Ledger ledger = new(heightRoot: (indexSegmentE - 3 + COUNT_SEGMENTS) % COUNT_SEGMENTS);

        TXBToken tXCoinbaseAccounts = ledger.CreateCoinbase(wallet, iDMiner, 0, [
          new TXOutputP2PKH(BALANCE_WALLET, wallet.Hash160PKeyPublic),
          new TXOutputP2PKH(BALANCE_WALLET, walletB.Hash160PKeyPublic),
          new TXOutputP2PKH(BALANCE_WALLET, walletC.Hash160PKeyPublic),
          new TXOutputP2PKH(BALANCE_WALLET, walletD.Hash160PKeyPublic),
          new TXOutputP2PKH(BALANCE_WALLET_SWEPT, walletE.Hash160PKeyPublic),
          .. Enumerable.Range(1, 98).Select(i => new TXOutputP2PKH(1, CreateIDAccount(indexSegmentCreation, (byte)i)))]);

        if (!ledger.TryInsertBlock("coinbase creating five wallet accounts and 99 accounts in one segment", [tXCoinbaseAccounts], isValidExpected: true, null, ref message))
          return false;

        int heightTip = ledger.GetHeightTip();

        TXBToken tXA1 = ledger.CreateTXSpend(wallet, walletB.Hash160PKeyPublic, VALUE_SPEND, t => { });
        TXBToken tXA2 = ledger.CreateTXSpend(wallet, walletB.Hash160PKeyPublic, VALUE_SPEND, t => t.Nonce += 1);
        TXBToken tXB1 = ledger.CreateTXSpend(walletB, wallet.Hash160PKeyPublic, VALUE_SPEND, t => AddAnchor(t, heightTip));
        TXBToken tXB2 = ledger.CreateTXSpend(walletB, wallet.Hash160PKeyPublic, VALUE_SPEND, t => t.Nonce += 1);
        TXBToken tXC1 = ledger.CreateTXSpend(walletC, wallet.Hash160PKeyPublic, VALUE_SPEND, t => { });
        TXBToken tXC2 = ledger.CreateTXSpend(walletC, wallet.Hash160PKeyPublic, VALUE_SPEND, t => t.Nonce += 1);
        TXBToken tXC1Conflicting = ledger.CreateTXSpend(walletC, walletD.Hash160PKeyPublic, VALUE_SPEND, t => { });
        TXBToken tXD1 = ledger.CreateTXSpend(walletD, iDCreatedPending, VALUE_SPEND, t => { });
        TXBToken tXE1 = ledger.CreateTXSpend(walletE, wallet.Hash160PKeyPublic, BALANCE_WALLET_SWEPT - FEE, t => { });

        PoolTXBToken pool = ledger.CreateTXPool(COUNT_BYTES_MAX_POOL);

        foreach ((string nameCase, TXBToken tX) in new List<(string, TXBToken)>
        {
          ("spend later confirmed", tXA1),
          ("spend following the confirmed spend", tXA2),
          ("anchor TX expiring with the next block", tXB1),
          ("spend following the expiring anchor TX", tXB2),
          ("spend later replaced by a conflicting spend", tXC1),
          ("spend following the replaced spend", tXC2),
          ("spend creating the last account of a segment", tXD1),
          ("spend of the whole Balance before a sweep", tXE1)
        })
          if (!IsAdmitted(nameCase, pool, tX, true, ref message))
            return false;

        TXBToken tXCoinbaseCreating = ledger.CreateCoinbase(wallet, iDMiner, tXA1.Fee + tXC1Conflicting.Fee, new TXOutputP2PKH(1000, iDCreatedByBlock));

        return ledger.TryInsertBlock("block confirming a spend, replacing a spend and creating an account", [tXCoinbaseCreating, tXA1, tXC1Conflicting], isValidExpected: true, null, ref message)
          && AreTXsRankedCorrect("pool after RemoveTXs", pool.GetTXsRanked(), [tXA2], ref message)
          && ledger.TryInsertBlockFromPool("block of the TX left in the pool", pool, wallet, iDMiner, 1, ref message);
      }

      static bool IsFeePerByteMinCorrect(string nameCase, PoolTXBToken pool, double feePerByteMinExpected, ref string message)
      {
        double feePerByteMin = pool.GetFeePerByteMin();

        if (feePerByteMin == feePerByteMinExpected)
          return true;

        message = $"TX pool, {nameCase}: GetFeePerByteMin returns {feePerByteMin}, expected {feePerByteMinExpected}.";
        return false;
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
        PoolTXBToken TXPool;

        internal PoolTXBToken CreateTXPool(long countBytesMaxPool)
        {
          TXPool = new PoolTXBToken(Accounts, countBytesMaxPool);

          return TXPool;
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
