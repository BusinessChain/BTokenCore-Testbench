using BTokenCore;

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

      Console.WriteLine("  1. Choose the strongest fork in branched BToken header trees.");

      return BlockchainForkChoice.RunTests(ref message);
    }

    abstract class BlockchainForkChoice : Blockchain
    {
      BlockchainForkChoice()
        : base(null, 0, null, null, null)
      { }

      internal static bool RunTests(ref string message)
      {
        return Test_ForkHoldingMoreBlocks(ref message)
          && Test_ForkHoldingEqualBlocks(ref message)
          && Test_ForkWithheldTip(ref message)
          && Test_ForkOfFork(ref message)
          && Test_ForkNotConnected(ref message);
      }

      static bool Test_ForkHoldingMoreBlocks(ref string message)
      {
        Chain chainRoot = CreateChainMain(countHeaders: 5, countBlocks: 5);
        Chain chainFork = InsertFork(chainRoot, heightFork: 3, countHeaders: 4, countBlocks: 4, nonceBranch: 1);

        return IsForkChosen("fork holding more blocks", chainRoot, chainFork, isStrongerExpected: true, ref message);
      }

      static bool Test_ForkHoldingEqualBlocks(ref string message)
      {
        Chain chainRoot = CreateChainMain(countHeaders: 5, countBlocks: 5);
        Chain chainFork = InsertFork(chainRoot, heightFork: 3, countHeaders: 2, countBlocks: 2, nonceBranch: 1);

        return IsForkChosen("fork holding as many blocks", chainRoot, chainFork, isStrongerExpected: false, ref message);
      }

      static bool Test_ForkWithheldTip(ref string message)
      {
        Chain chainRoot = CreateChainMain(countHeaders: 5, countBlocks: 5);
        Chain chainFork = InsertFork(chainRoot, heightFork: 3, countHeaders: 4, countBlocks: 2, nonceBranch: 1);

        return IsForkChosen("fork with more headers but withheld blocks", chainRoot, chainFork, isStrongerExpected: false, ref message);
      }

      static bool Test_ForkOfFork(ref string message)
      {
        Chain chainRoot = CreateChainMain(countHeaders: 5, countBlocks: 5);
        Chain chainFork = InsertFork(chainRoot, heightFork: 3, countHeaders: 3, countBlocks: 3, nonceBranch: 1);
        Chain chainForkOfFork = InsertFork(chainFork, heightFork: 4, countHeaders: 4, countBlocks: 4, nonceBranch: 2);

        return IsForkChosen("fork of a fork holding the most blocks", chainRoot, chainForkOfFork, isStrongerExpected: true, ref message);
      }

      static bool Test_ForkNotConnected(ref string message)
      {
        Chain chainRoot = CreateChainMain(countHeaders: 5, countBlocks: 2);
        InsertFork(chainRoot, heightFork: 3, countHeaders: 4, countBlocks: 4, nonceBranch: 1);

        return IsForkChosen("fork above the blocks of the main chain", chainRoot, chainExpected: null, isStrongerExpected: false, ref message);
      }

      static Chain CreateChainMain(int countHeaders, int countBlocks)
      {
        HeaderBToken headerRoot = CreateHeaderBToken(new byte[32]);

        Chain chainRoot = new(headerRoot);

        foreach (Header header in CreateHeaders(headerRoot, countHeaders, nonceBranch: 0))
          chainRoot.AppendHeader(header);

        chainRoot.HeaderTipBlockchain = chainRoot.HeaderTip.GetHeaderAncestor(countBlocks);

        return chainRoot;
      }

      static Chain InsertFork(Chain chainTrunk, int heightFork, int countHeaders, int countBlocks, uint nonceBranch)
      {
        List<Header> headers = CreateHeaders(chainTrunk.HeaderTip.GetHeaderAncestor(heightFork), countHeaders, nonceBranch);

        chainTrunk.InsertHeadersInTree([.. headers], heightBlockStoredOldest: 0, out _);

        Chain chainFork = chainTrunk.FindInTree(c => c.HeaderRoot == headers[0] ? c : null);

        foreach (Header header in headers.Take(countBlocks))
          chainFork.BlocksQueued.Add(header.Height, new Block(0) { Header = header });

        chainFork.AdvanceTipBlockchain();

        return chainFork;
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

      static bool IsForkChosen(string nameCase, Chain chainRoot, Chain chainExpected, bool isStrongerExpected, ref string message)
      {
        Chain chainFound = chainRoot.FindForkStrongest();

        if (chainFound != chainExpected)
        {
          message = $"Fork choice, {nameCase}: FindForkStrongest returned {DescribeChain(chainFound)}, expected {DescribeChain(chainExpected)}.";
          return false;
        }

        if (chainFound != null && chainFound.IsStrongerThan(chainRoot) != isStrongerExpected)
        {
          message = $"Fork choice, {nameCase}: IsStrongerThan of {DescribeChain(chainFound)} over the main chain is {!isStrongerExpected}, expected {isStrongerExpected}.";
          return false;
        }

        return true;
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
