using BTokenCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BTokenCore_Testbench;

public class NetworkAdapterTestbench : ISocketCommunication
{
  string ID;

  public NetworkAdapterTestbench(string id)
  {
    ID = id;
  }

  public async Task Start()
  {
  }

  public void Dispose()
  {

  }

  public string GetIP()
  {
    return ID;
  }
}
