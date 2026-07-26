using System;
using System.Linq;
using System.Text;
using BTokenLib;


namespace BTokenCore_Testbench;


class Testbench : ILogEntryNotifier
{
  public Testbench()
  { }

  public void Start()
  {
    TokenBitcoin tokenBitcoin = new TokenBitcoin(this);

  }

  public void NotifyLogEntry(string logEntry, string source)
  {
    Console.WriteLine($"{source}: {logEntry}");
  }
}
