using System.Security.Cryptography;

using BTokenCore;

using LiteDB;

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  abstract partial class Test_Testbench
  {
    protected abstract class BlockchainTestbench : BlockchainBToken
    {
      protected const int COUNT_SEGMENTS = 2048;
      protected const int SHIFT_INDEX_SEGMENT = 5;
      protected const long RENT_SWEEP = 36;
      protected const long BLOCK_REWARD_INITIAL = 20_000_000_000;
      protected const int PERIOD_HALVING = 52_500;
      protected const long FEE = 1000;
      protected const long VALUE_SPEND = 50000;

      static readonly Comparer<byte[]> ComparerID = Comparer<byte[]>.Create((a, b) => a.AsSpan().SequenceCompareTo(b));

      protected BlockchainTestbench()
        : base(null, null, null, null, null)
      { }

      protected BlockchainTestbench(string nameToken, LiteDatabase liteDatabase, Blockchain blockchainParent)
        : base(nameToken, liteDatabase, "BTK", blockchainParent, new SemaphoreSlim(1))
      { }

      protected static bool TryInsertCoinbasesUpTo(Ledger ledger, Wallet wallet, byte[] iDMiner, int height, ref string message)
      {
        while (ledger.GetHeightTip() < height)
          if (!ledger.TryInsertBlock("coinbase to the miner account", [ledger.CreateCoinbase(wallet, iDMiner, 0)], isValidExpected: true, null, ref message))
            return false;

        return true;
      }

      protected static void AddAnchor(TXBToken tX, int lockTime)
      {
        tX.TXOutputs.Add(CreateTXOutputTokenAnchor(ID_TOKEN, 0));
        tX.LockTime = lockTime;
      }

      protected static TXBToken CreateCoinbaseAccounts(Ledger ledger, Wallet wallet, int indexSegment, int countAccounts)
      {
        return ledger.CreateCoinbase(wallet, CreateIDAccount(indexSegment, 0), 0,
          [.. Enumerable.Range(1, countAccounts - 1).Select(i => new TXOutputP2PKH(1, CreateIDAccount(indexSegment, (byte)i)))]);
      }

      protected static int GetIndexSegment(byte[] iDAccount)
      {
        return (iDAccount[0] << 8 | iDAccount[1]) >> SHIFT_INDEX_SEGMENT;
      }

      protected static byte[] CreateIDAccount(int indexSegment, byte seed)
      {
        byte[] iDAccount = new byte[20];
        int prefix = indexSegment << SHIFT_INDEX_SEGMENT;

        iDAccount[0] = (byte)(prefix >> 8);
        iDAccount[1] = (byte)prefix;
        iDAccount[^1] = seed;

        return iDAccount;
      }

      protected static long GetBlockReward(int height)
      {
        int countHalvings = height / PERIOD_HALVING;

        return countHalvings >= 64 ? 0 : BLOCK_REWARD_INITIAL >> countHalvings;
      }

      protected class Ledger
      {
        LiteDatabase LiteDatabase = new(new MemoryStream(), new BsonMapper { IncludeFields = true, IncludeNonPublic = true });
        DatabaseAccounts Accounts = new();
        Dictionary<byte[], Account> AccountsReference = new(new EqualityComparerByteArray());
        HashSet<byte[]> IDsAccountSeen = new(new EqualityComparerByteArray());
        List<(Block block, List<TXBToken> tXs, Dictionary<byte[], Account> accountsBefore)> BlocksInserted = [];
        int HeightTip;
        byte[] HashTip = new byte[32];
        PoolTXBToken TXPool;


        internal Ledger(int heightRoot)
        {
          HeightTip = heightRoot;

          Accounts.Load(LiteDatabase);
          Accounts.Install([], new HeaderBToken { Hash = new byte[32], Height = heightRoot }, null, out _);
        }

        internal Ledger(Ledger ledgerServing, int heightInstalled)
        {
          Dictionary<Header, Block> blocksServed = ledgerServing.BlocksInserted.ToDictionary(b => b.block.Header, b => b.block);
          (byte[] segment, Header header)[] sweeps = new (byte[], Header)[COUNT_SEGMENTS];

          foreach (Header header in blocksServed.Keys.Where(h => h.Height > heightInstalled - COUNT_SEGMENTS && h.Height <= heightInstalled))
            sweeps[header.Height % COUNT_SEGMENTS] = (ledgerServing.Accounts.SerializeSegmentAtHeight(header.Height), header);

          Header headerInstalled = sweeps[heightInstalled % COUNT_SEGMENTS].header;

          Accounts.Load(LiteDatabase);
          LiteDatabase.RunTransaction(() => Accounts.Install(sweeps, headerInstalled, h => blocksServed[h], out _));

          AccountsReference = ledgerServing.BlocksInserted.FirstOrDefault(b => b.block.Header.Height == heightInstalled + 1).accountsBefore ?? ledgerServing.AccountsReference;
          IDsAccountSeen = new(ledgerServing.IDsAccountSeen, new EqualityComparerByteArray());
          HeightTip = heightInstalled;
          HashTip = headerInstalled.Hash;
        }

        internal PoolTXBToken CreateTXPool(long countBytesMaxPool)
        {
          TXPool = new PoolTXBToken(Accounts, countBytesMaxPool);

          return TXPool;
        }

        internal long GetRentNext()
        {
          ApplyBlock([], HeightTip + 1, out long rent);

          return rent;
        }

        internal int GetHeightTip()
        {
          return HeightTip;
        }

        internal List<Block> GetBlocksInserted()
        {
          return BlocksInserted.Select(b => b.block).ToList();
        }

        internal TXBToken CreateCoinbase(Wallet wallet, byte[] iDMiner, long fees, params TXOutputP2PKH[] tXOutputsAdded)
        {
          TXBToken tX = CreateTXBToken(wallet.KeyPublic, iDMiner);
          tX.TXOutputs[0] = new TXOutputP2PKH(GetBlockReward(HeightTip + 1) + fees + GetRentNext() - tXOutputsAdded.Sum(o => o.Value), iDMiner);
          tX.TXOutputs.AddRange(tXOutputsAdded);
          tX.SerializeUnsigned();

          return tX;
        }

        internal TXBToken CreateTXSpend(Wallet wallet, byte[] iDReceiving, long value, Action<TXBToken> alter)
        {
          Account account = AccountsReference[wallet.Hash160PKeyPublic];

          TXBToken tX = CreateTXBToken(wallet.KeyPublic, iDReceiving);
          tX.HeightAccountCreated = account.HeightAccountCreated;
          tX.Nonce = account.Nonce;
          tX.Fee = FEE;
          tX.TXOutputs[0] = new TXOutputP2PKH(value, iDReceiving);

          alter(tX);
          tX.Serialize(wallet);

          return tX;
        }

        internal long GetBalanceNext(byte[] iDAccount)
        {
          return ApplyBlock([], HeightTip + 1, out _)[iDAccount].Balance;
        }

        internal bool TryInsertBlock(string nameCase, List<TXBToken> tXs, bool isValidExpected, Action<Block> alter, ref string message)
        {
          int height = HeightTip + 1;
          Dictionary<byte[], Account> accountsAfter = ApplyBlock(tXs, height, out _);

          Block block = CreateBlock(tXs);
          HeaderBToken header = (HeaderBToken)block.Header;
          header.Height = height;
          header.HashPrevious = HashTip;
          header.HashAccounts = ComputeHashAccounts(accountsAfter, height);
          header.ComputeHash();

          Block blockParsed = ParseBlockSerialized(block);
          alter?.Invoke(blockParsed);

          try
          {
            LiteDatabase.RunTransaction(() => Accounts.InsertBlock(blockParsed));
          }
          catch (ProtocolException exception)
          {
            if (!isValidExpected)
              return true;

            message = $"Account rules, {nameCase}: rejected the valid block at height {height}: {exception.Message}";
            return false;
          }

          if (!isValidExpected)
          {
            message = $"Account rules, {nameCase}: accepted the invalid block at height {height}.";
            return false;
          }

          IDsAccountSeen.UnionWith(accountsAfter.Keys);

          if (!AreAccountsStoredCorrect($"{nameCase}, after the block at height {height}", accountsAfter, ref message))
            return false;

          BlocksInserted.Add((blockParsed, tXs, AccountsReference));
          AccountsReference = accountsAfter;
          HeightTip = height;
          HashTip = blockParsed.Header.Hash;

          TXPool?.RemoveTXs(blockParsed);

          return true;
        }

        internal bool TryRollBack(string nameCase, out List<TXBToken> tXsRolledBack, ref string message)
        {
          (Block block, tXsRolledBack, Dictionary<byte[], Account> accountsBefore) = BlocksInserted[^1];
          BlocksInserted.RemoveAt(BlocksInserted.Count - 1);

          Header header = block.Header;

          LiteDatabase.RunTransaction(() => Accounts.RollBack(header));

          AccountsReference = accountsBefore;
          HeightTip = header.Height - 1;
          HashTip = header.HashPrevious;

          TXPool?.ReturnTXs(block);

          string nameCaseRollBack = $"{nameCase}, after the rollback of the block at height {header.Height}";

          if (!AreAccountsStoredCorrect(nameCaseRollBack, accountsBefore, ref message))
            return false;

          if (!Accounts.GetHashBlockAppliedLast().IsAllBytesEqual(HashTip))
          {
            message = $"Account rules, {nameCaseRollBack}: the last applied block is {Accounts.GetHashBlockAppliedLast().ToHexString()}, expected {HashTip.ToHexString()}.";
            return false;
          }

          return true;
        }

        internal bool TryRollBackAndInsertAgain(string nameCase, int heightTarget, ref string message)
        {
          List<List<TXBToken>> tXsRolledBack = [];

          while (HeightTip > heightTarget)
          {
            if (!TryRollBack(nameCase, out List<TXBToken> tXs, ref message))
              return false;

            tXsRolledBack.Insert(0, tXs);
          }

          foreach (List<TXBToken> tXs in tXsRolledBack)
            if (!TryInsertBlock($"{nameCase}, block inserted again after rolling back to height {heightTarget}", tXs, isValidExpected: true, null, ref message))
              return false;

          return true;
        }

        bool AreAccountsStoredCorrect(string nameCase, Dictionary<byte[], Account> accountsExpected, ref string message)
        {
          foreach (byte[] iDAccount in IDsAccountSeen)
          {
            Account accountStored = Accounts.LoadAccount(iDAccount, heightAccountCreated: 0);
            Account accountExpected = accountsExpected.GetValueOrDefault(iDAccount) ?? new Account();

            if (accountStored.HeightAccountCreated != accountExpected.HeightAccountCreated
              || accountStored.Nonce != accountExpected.Nonce
              || accountStored.Balance != accountExpected.Balance)
            {
              message = $"Account rules, {nameCase}: account {iDAccount.ToHexString()} holds " +
                $"HeightAccountCreated {accountStored.HeightAccountCreated}, Nonce {accountStored.Nonce}, Balance {accountStored.Balance}, " +
                $"expected {accountExpected.HeightAccountCreated}, {accountExpected.Nonce}, {accountExpected.Balance}.";
              return false;
            }
          }

          return true;
        }

        Dictionary<byte[], Account> ApplyBlock(List<TXBToken> tXs, int height, out long rent)
        {
          Dictionary<byte[], Account> accounts = new(new EqualityComparerByteArray());

          foreach (Account account in AccountsReference.Values)
            accounts.Add(account.ID, new Account
            {
              ID = account.ID,
              HeightAccountCreated = account.HeightAccountCreated,
              Nonce = account.Nonce,
              Balance = account.Balance
            });

          rent = 0;

          foreach (Account account in accounts.Values.Where(a => GetIndexSegment(a.ID) == height % COUNT_SEGMENTS).ToList())
            if (account.Balance <= RENT_SWEEP)
            {
              rent += account.Balance;
              accounts.Remove(account.ID);
            }
            else
            {
              rent += RENT_SWEEP;
              account.Balance -= RENT_SWEEP;
            }

          for (int i = 0; i < tXs.Count; i++)
          {
            foreach (TXOutputP2PKH tXOutput in tXs[i].TXOutputs.OfType<TXOutputP2PKH>().Where(o => o.Value > 0))
            {
              if (!accounts.TryGetValue(tXOutput.IDAccount, out Account accountReceiving))
              {
                accountReceiving = new Account { ID = tXOutput.IDAccount, HeightAccountCreated = height };
                accounts.Add(accountReceiving.ID, accountReceiving);
              }

              accountReceiving.Balance += tXOutput.Value;
            }

            if (i > 0 && accounts.TryGetValue(Crypto.ComputeHash160(tXs[i].KeyPublic), out Account accountSource))
            {
              accountSource.Nonce += 1;
              accountSource.Balance -= tXs[i].GetValueOutputs() + tXs[i].Fee;
            }
          }

          foreach (Account account in accounts.Values.Where(a => a.Balance == 0).ToList())
            accounts.Remove(account.ID);

          return accounts;
        }

        static byte[] ComputeHashAccounts(Dictionary<byte[], Account> accounts, int height)
        {
          int indexSegment = height % COUNT_SEGMENTS;
          long offsetRent = RENT_SWEEP * ((height + COUNT_SEGMENTS - indexSegment) / COUNT_SEGMENTS);

          List<byte> bytes = [];
          int countAccounts = 0;

          foreach (Account account in accounts.Values.Where(a => GetIndexSegment(a.ID) == indexSegment).OrderBy(a => a.ID, ComparerID))
          {
            bytes.AddRange(account.ID);
            bytes.AddRange(BitConverter.GetBytes(account.HeightAccountCreated));
            bytes.AddRange(BitConverter.GetBytes(account.Nonce));
            bytes.AddRange(BitConverter.GetBytes(account.Balance + offsetRent));

            countAccounts += 1;
          }

          return SHA256.HashData([.. VarInt.GetBytes(countAccounts), .. bytes]);
        }

        internal bool TryInsertBlocksAbove(Ledger ledgerServing, ref string message)
        {
          foreach ((Block block, List<TXBToken> tXs, _) in ledgerServing.BlocksInserted.Where(b => b.block.Header.Height > HeightTip).ToList())
            if (!TryInsertBlock($"block of the serving ledger at height {block.Header.Height} inserted after the installation", tXs, isValidExpected: true, null, ref message))
              return false;

          return true;
        }

        internal bool AreAccountsInstalledCorrect(ref string message)
        {
          if (!AreAccountsStoredCorrect($"installed accounts at height {HeightTip}", AccountsReference, ref message))
            return false;

          int[] countsAccountExpected = new int[COUNT_SEGMENTS];

          foreach (byte[] iDAccount in ApplyBlock([], HeightTip + 1, out _).Keys)
            countsAccountExpected[GetIndexSegment(iDAccount)] += 1;

          (int[] countsAccount, _) = Accounts.LoadCountsAccountsPerSegmentNext();

          for (int i = 0; i < COUNT_SEGMENTS; i++)
            if (countsAccount[i] != countsAccountExpected[i])
            {
              message = $"Installed accounts at height {HeightTip}: segment {i} counts {countsAccount[i]} accounts, expected {countsAccountExpected[i]}.";
              return false;
            }

          return true;
        }

        internal bool AreSegmentsServedCorrect(string nameCase, int heightFirst, ref string message)
        {
          foreach (Header header in BlocksInserted.Select(b => b.block.Header).Where(h => h.Height >= heightFirst))
          {
            byte[] segment = Accounts.SerializeSegmentAtHeight(header.Height);

            if (segment == null)
            {
              message = $"Served segments, {nameCase}: no segment {header.Height % COUNT_SEGMENTS} served for height {header.Height}.";
              return false;
            }

            if (!SHA256.HashData(segment).IsAllBytesEqual(((HeaderBToken)header).HashAccounts))
            {
              message = $"Served segments, {nameCase}: segment {header.Height % COUNT_SEGMENTS} served for height {header.Height} doesn't match HashAccounts of its block.";
              return false;
            }
          }

          return true;
        }

        internal bool IsNoSegmentServed(string nameCase, int height, ref string message)
        {
          if (Accounts.SerializeSegmentAtHeight(height) == null)
            return true;

          message = $"Served segments, {nameCase}: served segment {height % COUNT_SEGMENTS} for height {height}.";
          return false;
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
