using BTokenCore;
using System.Security.Cryptography;


namespace BTokenCore_Testbench;

internal partial class Testbench : IToken
{
  public int GetSizeBlockBuffer()
  {
    return 1;
  }

  public void InsertBlock(Block block)
  {

  }

  public Header CreateHeaderGenesis()
  {
    return TokenBitcoin.CreateHeaderGenesis();
  }

  public Block MineBlock(int height, out TXOutputTokenAnchor anchorToken)
  {
    anchorToken = null;
    return null;
  }

  public void ReverseBlock(Block block)
  {

  }

  public Header ParseHeader(byte[] buffer, ref int startIndex, SHA256 sha256)
  {
    return null;
  }

  public TX ParseTX(byte[] buffer, ref int startIndex, SHA256 sha256, bool flagIsCoinbase)
  {
    return null;
  }
}
