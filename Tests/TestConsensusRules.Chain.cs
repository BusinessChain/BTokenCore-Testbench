using System.Security.Cryptography;

using BTokenCore;

using Fork = (int IndexTrunk, int HeightFork, int CountHeaders, int CountBlocks);

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  partial class TestConsensusRules
  {
    abstract class BlockchainChain : Blockchain
    {
      const int COUNT_HEADERS_MAIN = 4;
      const int COUNT_HEADERS_FORK_MAX = 3;
      const int COUNT_FORKS_MAX = 3;
      const int COUNT_FORKS_PRUNING_MAX = 2;
      const int COUNT_ROUNDS_SWITCH = 6;

      BlockchainChain()
        : base(null, 0, null, null, null)
      { }

      internal static bool TestForkPermutations(ref string message)
      {
        Console.WriteLine("  7. Choose the strongest fork in every permutation of BToken header trees with up to three forks.");

        for (int countBlocksMain = 0; countBlocksMain <= COUNT_HEADERS_MAIN; countBlocksMain++)
          if (!TryForkPermutations(countBlocksMain, [], COUNT_FORKS_MAX, IsForkChoiceCorrect, ref message))
            return false;

        return true;
      }

      internal static bool TestForkPruning(ref string message)
      {
        Console.WriteLine("  11. Check DropForksDeep and ReRoot at every height on every permutation of BToken header trees with up to two forks.");

        for (int countBlocksMain = 0; countBlocksMain <= COUNT_HEADERS_MAIN; countBlocksMain++)
          if (!TryForkPermutations(countBlocksMain, [], COUNT_FORKS_PRUNING_MAX, IsPruningCorrect, ref message))
            return false;

        return true;
      }

      delegate bool CheckPermutation(int countBlocksMain, List<Fork> forks, ref string message);

      static bool IsPruningCorrect(int countBlocksMain, List<Fork> forks, ref string message)
      {
        for (int height = 1; height <= COUNT_HEADERS_MAIN; height++)
        {
          Chain chainRoot = BuildTree(countBlocksMain, forks, out Header headerRoot, out List<Header> headersMain, out List<List<Header>> headersForks, out HashSet<Header> headersExpected, out HashSet<Header> headersHeldExpected);

          RemoveForksDeep(forks, headersForks, height, headersExpected, headersHeldExpected);
          chainRoot.DropForksDeep(height);

          if (!IsTreeConsistent(chainRoot, headerRoot, headersExpected, headersHeldExpected, out string error))
          {
            message = $"Fork pruning, DropForksDeep at height {height}, {DescribePermutation(countBlocksMain, forks)}: {error}";
            return false;
          }

          chainRoot = BuildTree(countBlocksMain, forks, out headerRoot, out headersMain, out headersForks, out headersExpected, out headersHeldExpected);

          Header headerRootNew = headersMain[height - 1];

          RemoveForksDeep(forks, headersForks, height, headersExpected, headersHeldExpected);
          headersExpected.RemoveWhere(h => h.Height < height && IsDescendantOf(headerRootNew, h));
          headersHeldExpected.RemoveWhere(h => h == headerRoot || headersMain.Contains(h));
          headersHeldExpected.Add(headerRootNew);

          chainRoot.ReRoot(headerRootNew);

          if (!IsTreeConsistent(chainRoot, headerRootNew, headersExpected, headersHeldExpected, out error))
          {
            message = $"Fork pruning, ReRoot at height {height}, {DescribePermutation(countBlocksMain, forks)}: {error}";
            return false;
          }
        }

        return true;
      }

      static void RemoveForksDeep(List<Fork> forks, List<List<Header>> headersForks, int height, HashSet<Header> headersExpected, HashSet<Header> headersHeldExpected)
      {
        for (int i = 0; i < forks.Count; i++)
          if (forks[i].IndexTrunk < 0 && forks[i].HeightFork < height)
          {
            Header headerRootFork = headersForks[i][0];

            headersExpected.RemoveWhere(h => IsDescendantOf(h, headerRootFork));
            headersHeldExpected.RemoveWhere(h => IsDescendantOf(h, headerRootFork));
          }
      }

      internal static bool TestKnownHeaders(ref string message)
      {
        Console.WriteLine("  9. Insert known, partly known, unconnected and too deep headers.");

        HeaderBToken headerRoot = CreateHeaderBToken(new byte[32]);
        Chain chainRoot = new(headerRoot);

        List<Header> headersMain = CreateHeaders(headerRoot, COUNT_HEADERS_MAIN, nonceBranch: 0);

        foreach (Header header in headersMain)
          chainRoot.AppendHeader(header);

        chainRoot.HeaderTipBlockchain = chainRoot.HeaderTip;

        List<Header> headersFork = InsertFork(chainRoot, headersMain[0], (IndexTrunk: -1, HeightFork: 1, CountHeaders: 2, CountBlocks: 0), nonceBranch: 1);

        HashSet<Header> headersExpected = [headerRoot, .. headersMain, .. headersFork];
        HashSet<Header> headersHeldExpected = [headerRoot, .. headersMain];

        List<Header> headersAppended = CreateHeaders(headersMain[^1], 2, nonceBranch: 2);
        List<Header> headersForkNew = CreateHeaders(headersMain[0], 1, nonceBranch: 3);
        List<Header> headersUnconnected = CreateHeaders(CreateHeaderBToken(SHA256.HashData(new byte[32])), 1, nonceBranch: 4);
        List<Header> headersForkAtOldest = CreateHeaders(headersMain[1], 1, nonceBranch: 5);
        List<Header> headersForkBelowOldest = CreateHeaders(headersMain[0], 1, nonceBranch: 6);

        if (!IsInsertionCorrect("all headers of the main chain known", chainRoot, headerRoot, CopyHeaders(headersMain), 0, headersMain[^1], headersExpected, headersHeldExpected, ref message)
          || !IsInsertionCorrect("known headers inside the main chain", chainRoot, headerRoot, CopyHeaders(headersMain[1..3]), 0, headersMain[2], headersExpected, headersHeldExpected, ref message)
          || !IsInsertionCorrect("known prefix leading into a fork", chainRoot, headerRoot, CopyHeaders([headersMain[0], .. headersFork]), 0, headersFork[^1], headersExpected, headersHeldExpected, ref message))
          return false;

        headersExpected.UnionWith(headersAppended);

        if (!IsInsertionCorrect("known headers followed by new ones at the tip", chainRoot, headerRoot, [.. CopyHeaders(headersMain[2..]), .. headersAppended], 0, headersAppended[^1], headersExpected, headersHeldExpected, ref message))
          return false;

        headersExpected.UnionWith(headersForkNew);

        if (!IsInsertionCorrect("known header followed by a new fork", chainRoot, headerRoot, [.. CopyHeaders(headersMain[..1]), .. headersForkNew], 0, headersForkNew[^1], headersExpected, headersHeldExpected, ref message)
          || !IsInsertionCorrect("unconnected headers", chainRoot, headerRoot, headersUnconnected, 0, null, headersExpected, headersHeldExpected, ref message)
          || !IsInsertionCorrect("fork below heightBlockStoredOldest", chainRoot, headerRoot, headersForkBelowOldest, 2, null, headersExpected, headersHeldExpected, ref message))
          return false;

        headersExpected.UnionWith(headersForkAtOldest);

        return IsInsertionCorrect("fork at heightBlockStoredOldest", chainRoot, headerRoot, headersForkAtOldest, 2, headersForkAtOldest[^1], headersExpected, headersHeldExpected, ref message);
      }

      internal static bool TestHeaderPlaceholders(ref string message)
      {
        Console.WriteLine("  10. Insert, find and cut off header placeholders in the main chain and in forks.");

        HeaderBToken headerRoot = CreateHeaderBToken(new byte[32]);
        Chain chainRoot = new(headerRoot);

        List<Header> headersMain = CreateHeaders(headerRoot, COUNT_HEADERS_MAIN, nonceBranch: 0);

        foreach (Header header in headersMain)
          chainRoot.AppendHeader(header);

        chainRoot.HeaderTipBlockchain = chainRoot.HeaderTip;

        List<Header> headersFork = InsertFork(chainRoot, headersMain[0], (IndexTrunk: -1, HeightFork: 1, CountHeaders: 2, CountBlocks: 0), nonceBranch: 1);

        HashSet<Header> headersExpected = [headerRoot, .. headersMain, .. headersFork];
        HashSet<Header> headersHeldExpected = [headerRoot, .. headersMain];

        Header[] headersParent = [.. Enumerable.Range(0, 7).Select(i => CreateHeaderBToken(SHA256.HashData([(byte)i])))];

        Header placeholderTipMain = CreatePlaceholder(SHA256.HashData([10]), headersMain[^1].Hash, headersParent[0]);
        Header placeholderTipFork = CreatePlaceholder(SHA256.HashData([11]), headersFork[^1].Hash, headersParent[1]);
        Header placeholderKnown = CreatePlaceholder(headersMain[1].Hash, headersMain[1].HashPrevious, headersParent[2]);
        Header placeholderConflicting = CreatePlaceholder(headersMain[2].Hash, SHA256.HashData([12]), headersParent[3]);
        Header placeholderRootFork = CreatePlaceholder(SHA256.HashData([13]), headersMain[2].Hash, headersParent[5]);

        chainRoot.InsertHeaderPlaceholder(placeholderTipMain, 0);
        chainRoot.InsertHeaderPlaceholder(placeholderTipFork, 0);
        chainRoot.InsertHeaderPlaceholder(placeholderKnown, 0);
        chainRoot.InsertHeaderPlaceholder(placeholderConflicting, 0);
        chainRoot.InsertHeaderPlaceholder(placeholderRootFork, 0);

        headersExpected.UnionWith([placeholderTipMain, placeholderTipFork, placeholderRootFork]);

        if (!IsTreeConsistent(chainRoot, headerRoot, headersExpected, headersHeldExpected, out string error))
        {
          message = $"Header placeholders, after insertion: {error}";
          return false;
        }

        if (!IsPlaceholderFound("new placeholder at the tip of the main chain", chainRoot, headersParent[0], placeholderTipMain, ref message)
          || !IsPlaceholderFound("new placeholder at the tip of a fork", chainRoot, headersParent[1], placeholderTipFork, ref message)
          || !IsPlaceholderFound("placeholder of a known header", chainRoot, headersParent[2], headersMain[1], ref message)
          || !IsPlaceholderFound("placeholder of a known hash with other HashPrevious", chainRoot, headersParent[3], null, ref message)
          || !IsPlaceholderFound("parent header without placeholder", chainRoot, headersParent[4], null, ref message)
          || !IsPlaceholderFound("new placeholder as root of a fork", chainRoot, headersParent[5], placeholderRootFork, ref message))
          return false;

        chainRoot.TryGetHeaderPlaceholder(headersParent[1], out _, out Chain chainPlaceholderTipFork);
        chainPlaceholderTipFork.CutOff(placeholderTipFork);
        headersExpected.Remove(placeholderTipFork);

        if (!IsTreeConsistent(chainRoot, headerRoot, headersExpected, headersHeldExpected, out error))
        {
          message = $"Header placeholders, after CutOff of the placeholder at the tip of a fork: {error}";
          return false;
        }

        chainRoot.TryGetHeaderPlaceholder(headersParent[5], out _, out Chain chainPlaceholderRootFork);
        chainPlaceholderRootFork.CutOff(placeholderRootFork);
        headersExpected.Remove(placeholderRootFork);

        if (!IsTreeConsistent(chainRoot, headerRoot, headersExpected, headersHeldExpected, out error))
        {
          message = $"Header placeholders, after CutOff of the placeholder at the root of a fork: {error}";
          return false;
        }

        return true;
      }

      static Header CreatePlaceholder(byte[] hash, byte[] hashPrevious, Header headerParent)
      {
        return new HeaderBToken(
          headerHash: hash,
          hashPrevious: hashPrevious,
          merkleRootHash: new byte[32],
          hashAccounts: new byte[32],
          nonce: 0)
        {
          HeaderParent = headerParent
        };
      }

      static bool IsPlaceholderFound(string nameCase, Chain chainRoot, Header headerParent, Header headerExpected, ref string message)
      {
        bool isFound = chainRoot.TryGetHeaderPlaceholder(headerParent, out Header headerPlaceholder, out Chain chainPlaceholder);

        if (headerExpected == null)
        {
          if (!isFound)
            return true;

          message = $"Header placeholders, {nameCase}: TryGetHeaderPlaceholder found {DescribeHeader(headerPlaceholder)}, expected none.";
          return false;
        }

        if (!isFound || headerPlaceholder != headerExpected)
        {
          message = $"Header placeholders, {nameCase}: TryGetHeaderPlaceholder found {DescribeHeader(headerPlaceholder)}, expected {DescribeHeader(headerExpected)}.";
          return false;
        }

        for (Header header = chainPlaceholder.HeaderTip; header != chainPlaceholder.HeaderRoot.HeaderPrevious; header = header.HeaderPrevious)
          if (header == headerExpected)
            return true;

        message = $"Header placeholders, {nameCase}: TryGetHeaderPlaceholder returned {DescribeChain(chainPlaceholder)}, which doesn't hold {DescribeHeader(headerExpected)}.";
        return false;
      }

      static bool IsInsertionCorrect(string nameCase, Chain chainRoot, Header headerRoot, List<Header> headers, int heightBlockStoredOldest, Header headerLastExpected, HashSet<Header> headersExpected, HashSet<Header> headersHeldExpected, ref string message)
      {
        chainRoot.InsertHeadersInTree([.. headers], heightBlockStoredOldest, out Header headerLastInTree);

        if (headerLastInTree != headerLastExpected)
        {
          message = $"Known headers, {nameCase}: headerLastInTree is {DescribeHeader(headerLastInTree)}, expected {DescribeHeader(headerLastExpected)}.";
          return false;
        }

        if (!IsTreeConsistent(chainRoot, headerRoot, headersExpected, headersHeldExpected, out string error))
        {
          message = $"Known headers, {nameCase}: {error}";
          return false;
        }

        return true;
      }

      static List<Header> CopyHeaders(List<Header> headers)
      {
        List<Header> copies = [];

        foreach (Header header in headers)
        {
          HeaderBToken copy = CreateHeaderBToken(new byte[32]);
          copy.HashPrevious = header.HashPrevious;
          copy.Nonce = header.Nonce;
          copy.ComputeHash();

          copies.Add(copy);
        }

        return copies;
      }

      static string DescribeHeader(Header header)
      {
        if (header == null)
          return "null";

        return $"the header at height {header.Height} with hash {header.Hash.ToHexString()}";
      }

      internal static bool TestAlternatingSwitches(ref string message)
      {
        Console.WriteLine("  8. Switch two competing branches back and forth.");

        HeaderBToken headerRoot = CreateHeaderBToken(new byte[32]);
        Chain chainRoot = new(headerRoot);

        List<Header> headersMain = CreateHeaders(headerRoot, COUNT_HEADERS_MAIN, nonceBranch: 0);

        foreach (Header header in headersMain)
          chainRoot.AppendHeader(header);

        chainRoot.HeaderTipBlockchain = chainRoot.HeaderTip;

        HashSet<Header> headersExpected = [headerRoot, .. headersMain];
        HashSet<Header> headersHeldExpected = [.. headersExpected];

        Header[] headersTipBranch = [chainRoot.HeaderTip, headersMain[0]];

        for (int round = 1; round <= COUNT_ROUNDS_SWITCH; round++)
        {
          int indexBranchWeak = chainRoot.HeaderTip == headersTipBranch[0] ? 1 : 0;
          int heightTipBlockchainExpected = chainRoot.HeaderTipBlockchain.Height + 1;

          List<Header> headersNew = CreateHeaders(
            headersTipBranch[indexBranchWeak],
            heightTipBlockchainExpected - headersTipBranch[indexBranchWeak].Height + 1,
            nonceBranch: (uint)round);

          chainRoot.InsertHeadersInTree([.. headersNew], heightBlockStoredOldest: 0, out _);
          headersExpected.UnionWith(headersNew);
          headersTipBranch[indexBranchWeak] = headersNew[^1];

          Chain chainWeak = chainRoot.FindInTree(c => c.HeaderTip == headersNew[^1] ? c : null);

          if (chainWeak == null || chainWeak == chainRoot)
          {
            message = $"Alternating switches, round {round}: the extended branch isn't a fork.";
            return false;
          }

          int heightTipBlockchainWeak = chainWeak.HeaderTipBlockchain?.Height ?? chainWeak.HeaderRoot.Height - 1;

          for (Header header = headersNew[^1]; header.Height > heightTipBlockchainWeak; header = header.HeaderPrevious)
            if (header.Height <= heightTipBlockchainExpected)
            {
              chainWeak.BlocksQueued.Add(header.Height, new Block(0) { Header = header });
              headersHeldExpected.Add(header);
            }

          chainWeak.AdvanceTipBlockchain();

          Header headerTipBlockchainExpected = headersNew[^1].HeaderPrevious;

          if (!TryReorgToForkStrongest(chainRoot) || TryReorgToForkStrongest(chainRoot))
          {
            message = $"Alternating switches, round {round}: the reorg to the stronger branch doesn't happen exactly once.";
            return false;
          }

          if (chainRoot.HeaderTipBlockchain != headerTipBlockchainExpected)
          {
            message = $"Alternating switches, round {round}: the main chain holds blocks up to height {chainRoot.HeaderTipBlockchain.Height}, expected {heightTipBlockchainExpected}.";
            return false;
          }

          if (!IsTreeConsistent(chainRoot, headerRoot, headersExpected, headersHeldExpected, out string error))
          {
            message = $"Alternating switches, round {round}: {error}";
            return false;
          }
        }

        return true;
      }

      static bool TryForkPermutations(int countBlocksMain, List<Fork> forks, int countForksMax, CheckPermutation check, ref string message)
      {
        if (forks.Count > 0 && !check(countBlocksMain, forks, ref message))
          return false;

        if (forks.Count == countForksMax)
          return true;

        for (int indexTrunk = -1; indexTrunk < forks.Count; indexTrunk++)
        {
          int heightRootTrunk = indexTrunk < 0 ? 0 : forks[indexTrunk].HeightFork + 1;
          int heightTipTrunk = indexTrunk < 0 ? COUNT_HEADERS_MAIN : forks[indexTrunk].HeightFork + forks[indexTrunk].CountHeaders;

          for (int heightFork = heightRootTrunk; heightFork < heightTipTrunk; heightFork++)
            for (int countHeaders = 1; countHeaders <= COUNT_HEADERS_FORK_MAX; countHeaders++)
              for (int countBlocks = 0; countBlocks <= countHeaders; countBlocks++)
              {
                forks.Add((indexTrunk, heightFork, countHeaders, countBlocks));

                bool isCorrect = TryForkPermutations(countBlocksMain, forks, countForksMax, check, ref message);

                forks.RemoveAt(forks.Count - 1);

                if (!isCorrect)
                  return false;
              }
        }

        return true;
      }

      static Chain BuildTree(int countBlocksMain, List<Fork> forks, out Header headerRoot, out List<Header> headersMain, out List<List<Header>> headersForks, out HashSet<Header> headersExpected, out HashSet<Header> headersHeldExpected)
      {
        headerRoot = CreateHeaderBToken(new byte[32]);
        Chain chainRoot = new(headerRoot);

        headersMain = CreateHeaders(headerRoot, COUNT_HEADERS_MAIN, nonceBranch: 0);

        foreach (Header header in headersMain)
          chainRoot.AppendHeader(header);

        chainRoot.HeaderTipBlockchain = chainRoot.HeaderTip.GetHeaderAncestor(countBlocksMain);

        headersExpected = [headerRoot, .. headersMain];
        headersHeldExpected = [headerRoot, .. headersMain.Take(countBlocksMain)];
        headersForks = [];

        for (int i = 0; i < forks.Count; i++)
        {
          Header headerTipTrunk = forks[i].IndexTrunk < 0 ? chainRoot.HeaderTip : headersForks[forks[i].IndexTrunk][^1];
          List<Header> headersFork = InsertFork(chainRoot, headerTipTrunk.GetHeaderAncestor(forks[i].HeightFork), forks[i], nonceBranch: (uint)i + 1);

          headersForks.Add(headersFork);
          headersExpected.UnionWith(headersFork);
          headersHeldExpected.UnionWith(headersFork.Take(forks[i].CountBlocks));
        }

        return chainRoot;
      }

      static bool IsForkChoiceCorrect(int countBlocksMain, List<Fork> forks, ref string message)
      {
        Chain chainRoot = BuildTree(countBlocksMain, forks, out Header headerRoot, out _, out List<List<Header>> headersForks, out HashSet<Header> headersExpected, out HashSet<Header> headersHeldExpected);

        int heightStrongest = -1;
        bool[] isConnected = new bool[forks.Count];

        for (int i = 0; i < forks.Count; i++)
        {
          Fork fork = forks[i];
          int heightTipBlockchainTrunk = fork.IndexTrunk < 0 ? countBlocksMain : GetHeightTipBlockchain(forks[fork.IndexTrunk]);

          isConnected[i] = fork.CountBlocks > 0
            && (fork.IndexTrunk < 0 || isConnected[fork.IndexTrunk])
            && fork.HeightFork <= heightTipBlockchainTrunk;

          if (isConnected[i])
            heightStrongest = Math.Max(heightStrongest, GetHeightTipBlockchain(fork));
        }

        Chain chainFound = chainRoot.FindForkStrongest();
        int indexFound = chainFound == null ? -1 : headersForks.FindIndex(h => h[0] == chainFound.HeaderRoot);

        bool isChainFoundCorrect = heightStrongest < 0
          ? chainFound == null
          : indexFound >= 0 && isConnected[indexFound] && chainFound.HeaderTipBlockchain.Height == heightStrongest;

        if (!isChainFoundCorrect)
        {
          message = $"Fork choice, {DescribePermutation(countBlocksMain, forks)}: FindForkStrongest returned {DescribeChain(chainFound)}, expected a connected fork holding blocks up to height {heightStrongest}.";
          return false;
        }

        bool isStrongerExpected = heightStrongest > countBlocksMain;

        if (chainFound != null && chainFound.IsStrongerThan(chainRoot) != isStrongerExpected)
        {
          message = $"Fork choice, {DescribePermutation(countBlocksMain, forks)}: IsStrongerThan of {DescribeChain(chainFound)} over the main chain is {!isStrongerExpected}, expected {isStrongerExpected}.";
          return false;
        }

        if (!TryCheckSwitches(chainRoot, headerRoot, headersExpected, headersHeldExpected, Math.Max(countBlocksMain, heightStrongest), out string error))
        {
          message = $"Switch checks, {DescribePermutation(countBlocksMain, forks)}: {error}";
          return false;
        }

        return true;
      }

      static bool TryCheckSwitches(Chain chainRoot, Header headerRoot, HashSet<Header> headersExpected, HashSet<Header> headersHeldExpected, int heightTipBlockchainExpected, out string error)
      {
        for (int countReorgs = 0; TryReorgToForkStrongest(chainRoot); countReorgs++)
          if (countReorgs > COUNT_FORKS_MAX)
          {
            error = "the reorg to the strongest fork doesn't settle.";
            return false;
          }

        if (!IsTreeConsistent(chainRoot, headerRoot, headersExpected, headersHeldExpected, out error))
        {
          error = $"after the reorg {error}";
          return false;
        }

        if (chainRoot.HeaderTipBlockchain.Height != heightTipBlockchainExpected)
        {
          error = $"after the reorg the main chain holds blocks up to height {chainRoot.HeaderTipBlockchain.Height}, expected {heightTipBlockchainExpected}.";
          return false;
        }

        while (chainRoot.HeaderTip != headerRoot)
        {
          Header headerCut = chainRoot.HeaderTip.GetHeaderAncestor((chainRoot.HeaderTip.Height + 1) / 2);

          headersExpected.RemoveWhere(h => IsDescendantOf(h, headerCut));
          headersHeldExpected.RemoveWhere(h => IsDescendantOf(h, headerCut));

          chainRoot.CutOff(headerCut);
          InsertBlocksQueued(chainRoot);

          if (!IsTreeConsistent(chainRoot, headerRoot, headersExpected, headersHeldExpected, out error))
          {
            error = $"after CutOff at height {headerCut.Height} {error}";
            return false;
          }
        }

        return true;
      }

      static bool TryReorgToForkStrongest(Chain chainRoot)
      {
        InsertBlocksQueued(chainRoot);

        if (chainRoot.FindForkStrongest() is not Chain chain || !chain.IsStrongerThan(chainRoot))
          return false;

        while (chain != chainRoot)
        {
          if (chain.ChainTrunk == chainRoot)
            while (chainRoot.HeaderTipBlockchain.Height >= chain.HeaderRoot.Height)
            {
              Header header = chainRoot.HeaderTipBlockchain;
              chainRoot.BlocksQueued.Add(header.Height, new Block(0) { Header = header });
              chainRoot.HeaderTipBlockchain = header.HeaderPrevious;
            }

          chain.SwitchWithTrunk();
          chain = chain.ChainTrunk;
        }

        InsertBlocksQueued(chainRoot);

        return true;
      }

      static void InsertBlocksQueued(Chain chainRoot)
      {
        while (chainRoot.BlocksQueued.Remove(chainRoot.HeaderTipBlockchain.Height + 1, out Block block))
          chainRoot.HeaderTipBlockchain = block.Header;
      }

      static bool IsDescendantOf(Header header, Header headerAncestor)
      {
        while (header.Height > headerAncestor.Height)
          header = header.HeaderPrevious;

        return header == headerAncestor;
      }

      static bool IsTreeConsistent(Chain chainRoot, Header headerRoot, HashSet<Header> headersExpected, HashSet<Header> headersHeldExpected, out string error)
      {
        List<Chain> chains = [];
        chainRoot.FindInTree<Chain>(c =>
        {
          chains.Add(c);
          return null;
        });

        Dictionary<Chain, List<Header>> headersByChain = [];

        foreach (Chain chain in chains)
        {
          List<Header> headersChain = [chain.HeaderRoot];

          while (headersChain[^1].HeaderNext is Header headerNext && headersChain.Count <= headersExpected.Count)
          {
            if (headerNext.HeaderPrevious != headersChain[^1] || headerNext.Height != headersChain[^1].Height + 1)
            {
              error = $"{DescribeChain(chain)} has broken links at height {headerNext.Height}.";
              return false;
            }

            headersChain.Add(headerNext);
          }

          if (headersChain[^1] != chain.HeaderTip)
          {
            error = $"the headers of {DescribeChain(chain)} don't end at its HeaderTip.";
            return false;
          }

          headersByChain.Add(chain, headersChain);
        }

        HashSet<Header> headersFound = [];
        HashSet<Header> headersHeldFound = [];

        foreach (Chain chain in chains)
        {
          List<Header> headersChain = headersByChain[chain];

          bool isTrunkCorrect = chain == chainRoot
            ? chain.ChainTrunk == null && chain.HeaderRoot == headerRoot
            : chain.ChainTrunk != null && headersByChain.TryGetValue(chain.ChainTrunk, out List<Header> headersTrunk) && headersTrunk.Contains(chain.HeaderRoot.HeaderPrevious);

          if (!isTrunkCorrect)
          {
            error = $"{DescribeChain(chain)} doesn't branch from its ChainTrunk.";
            return false;
          }

          foreach (Header header in headersChain)
            if (!headersFound.Add(header))
            {
              error = $"the header at height {header.Height} lies in more than one chain.";
              return false;
            }

          foreach (KeyValuePair<int, Block> blockQueued in chain.BlocksQueued)
          {
            if (blockQueued.Key != blockQueued.Value.Header.Height || !headersChain.Contains(blockQueued.Value.Header))
            {
              error = $"{DescribeChain(chain)} queues a block at height {blockQueued.Key} of another chain.";
              return false;
            }

            headersHeldFound.Add(blockQueued.Value.Header);
          }

          if (chain == chainRoot)
          {
            int indexTipBlockchain = headersChain.IndexOf(chain.HeaderTipBlockchain);

            if (indexTipBlockchain < 0)
            {
              error = "HeaderTipBlockchain of the main chain lies outside the main chain.";
              return false;
            }

            headersHeldFound.UnionWith(headersChain.Take(indexTipBlockchain + 1));
          }
          else
          {
            Header headerTipBlockchainExpected = headersChain.TakeWhile(h => chain.BlocksQueued.ContainsKey(h.Height)).LastOrDefault();

            if (chain.HeaderTipBlockchain != headerTipBlockchainExpected)
            {
              error = $"HeaderTipBlockchain of {DescribeChain(chain)} is at height {chain.HeaderTipBlockchain?.Height}, expected {headerTipBlockchainExpected?.Height}.";
              return false;
            }
          }
        }

        if (!headersFound.SetEquals(headersExpected))
        {
          error = $"the header tree holds {headersFound.Count} headers, expected {headersExpected.Count}.";
          return false;
        }

        if (!headersHeldFound.SetEquals(headersHeldExpected))
        {
          error = $"the header tree holds {headersHeldFound.Count} blocks, expected {headersHeldExpected.Count}.";
          return false;
        }

        error = "";
        return true;
      }

      static int GetHeightTipBlockchain(Fork fork)
      {
        return fork.HeightFork + fork.CountBlocks;
      }

      static List<Header> InsertFork(Chain chainRoot, Header headerFork, Fork fork, uint nonceBranch)
      {
        List<Header> headers = CreateHeaders(headerFork, fork.CountHeaders, nonceBranch);

        chainRoot.InsertHeadersInTree([.. headers], heightBlockStoredOldest: 0, out _);

        Chain chainFork = chainRoot.FindInTree(c => c.HeaderRoot == headers[0] ? c : null);

        foreach (Header header in headers.Take(fork.CountBlocks))
          chainFork.BlocksQueued.Add(header.Height, new Block(0) { Header = header });

        chainFork.AdvanceTipBlockchain();

        return headers;
      }

      static List<Header> CreateHeaders(Header headerPrevious, int count, uint nonceBranch)
      {
        List<Header> headers = [];

        for (int i = 0; i < count; i++)
        {
          HeaderBToken header = CreateHeaderBToken(new byte[32]);
          header.HashPrevious = headerPrevious.Hash;
          header.Nonce = nonceBranch;
          header.ComputeHash();

          headers.Add(header);
          headerPrevious = header;
        }

        return headers;
      }

      static string DescribePermutation(int countBlocksMain, List<Fork> forks)
      {
        IEnumerable<string> descriptionsFork = forks.Select((f, i) =>
          $"fork {i + 1} from {(f.IndexTrunk < 0 ? "the main chain" : $"fork {f.IndexTrunk + 1}")} at height {f.HeightFork} with {f.CountHeaders} headers and {f.CountBlocks} blocks");

        return $"main chain with {COUNT_HEADERS_MAIN} headers and {countBlocksMain} blocks, {string.Join(", ", descriptionsFork)}";
      }

      static string DescribeChain(Chain chain)
      {
        if (chain == null)
          return "no chain";

        return $"the chain with root at height {chain.HeaderRoot.Height} and tip at height {chain.HeaderTip.Height}";
      }
    }
  }
}
