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

  public async Task SendMessage(string commandString, int lengthDataPayload, byte[] payload)
  {

  }

  public async Task<string> ReceiveCommandMessageNext()
  {
    return "test";
  }

  public async Task LoadMessageNext(MessageNetworkProtocol message)
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
