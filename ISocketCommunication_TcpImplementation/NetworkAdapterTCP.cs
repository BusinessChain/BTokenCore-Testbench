using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;

using BTokenCore;


namespace BTokenCore_Testbench;

public class NetworkAdapterTCP : ISocketCommunication
{
  TcpClient TcpClient;


  public NetworkAdapterTCP(string address)
  {
    TcpClient = new TcpClient();
  }

}
