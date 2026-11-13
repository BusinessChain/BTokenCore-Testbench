using BTokenCore;

using Fork = (int IndexTrunk, int HeightFork, int CountHeaders, int CountBlocks);

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  class TestForkChoice : Test_Testbench
  {
    internal TestForkChoice(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      Console.WriteLine("  1. Choose the strongest fork in every permutation of BToken header trees with up to three forks.");

      return BlockchainForkChoice.RunTests(ref message);
    }

    abstract class BlockchainForkChoice : Blockchain
    {
      const int COUNT_HEADERS_MAIN = 4;
      const int COUNT_HEADERS_FORK_MAX = 3;
      const int COUNT_FORKS_MAX = 3;

      BlockchainForkChoice()
        : base(null, 0, null, null, null)
      { }

      internal static bool RunTests(ref string message)
      {
        for (int countBlocksMain = 0; countBlocksMain <= COUNT_HEADERS_MAIN; countBlocksMain++)
          if (!TryForkPermutations(countBlocksMain, [], ref message))
            return false;

        return true;
      }

      static bool TryForkPermutations(int countBlocksMain, List<Fork> forks, ref string message)
      {
        if (forks.Count > 0 && !IsForkChoiceCorrect(countBlocksMain, forks, ref message))
          return false;

        if (forks.Count == COUNT_FORKS_MAX)
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

                bool isCorrect = TryForkPermutations(countBlocksMain, forks, ref message);

                forks.RemoveAt(forks.Count - 1);

                if (!isCorrect)
                  return false;
              }
        }

        return true;
      }

      static bool IsForkChoiceCorrect(int countBlocksMain, List<Fork> forks, ref string message)
      {
        HeaderBToken headerRoot = CreateHeaderBToken(new byte[32]);
        Chain chainRoot = new(headerRoot);

        foreach (Header header in CreateHeaders(headerRoot, COUNT_HEADERS_MAIN, nonceBranch: 0))
          chainRoot.AppendHeader(header);

        chainRoot.HeaderTipBlockchain = chainRoot.HeaderTip.GetHeaderAncestor(countBlocksMain);

        List<List<Header>> headersForks = [];

        for (int i = 0; i < forks.Count; i++)
        {
          Header headerTipTrunk = forks[i].IndexTrunk < 0 ? chainRoot.HeaderTip : headersForks[forks[i].IndexTrunk][^1];
          headersForks.Add(InsertFork(chainRoot, headerTipTrunk.GetHeaderAncestor(forks[i].HeightFork), forks[i], nonceBranch: (uint)i + 1));
        }

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
