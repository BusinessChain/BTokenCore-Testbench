using System;
using System.Linq;
using System.Text;
using BTokenLib;


namespace BTokenCore_Testbench;


class ConsoleLogger : ILogEntryNotifier
{
  public void NotifyLogEntry(string logEntry, string source)
  {
    Console.WriteLine($"{source}: {logEntry}");
  }
}
