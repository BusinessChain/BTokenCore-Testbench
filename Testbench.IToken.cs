using BTokenCore;

namespace BTokenCore_Testbench;

internal partial class Testbench : IToken
{
  public Block CreateBlock()
  {
    return null;
  }

  public void InsertBlock(Block block)
  {

  }

  public Header CreateHeaderGenesis()
  {
    return null;
  }

  public Block MineBlock(int height, out TXOutputTokenAnchor anchorToken)
  {
    anchorToken = null;
    return null;
  }

  public void ReverseBlock(Block block)
  {

  }
}
