using System;


namespace BTokenCore_Testbench;


class Program
{
  static void Main(string[] args)
  {
    Testbench testbench = new(new ConsoleLogger());
    testbench.Start();
  }
}