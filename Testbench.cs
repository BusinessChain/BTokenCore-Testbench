using System;
using System.Linq;
using System.Text;
using BTokenLib;


namespace BTokenCore_Testbench;


class Testbench
{
  public Testbench(ILogEntryNotifier logEntryNotifier)
  {
    TokenBitcoin tokenBitcoin = new TokenBitcoin(logEntryNotifier);
    TokenBToken tokenBToken = new TokenBToken(logEntryNotifier, tokenBitcoin);
  }

  public void Start()
  {
  }
}
