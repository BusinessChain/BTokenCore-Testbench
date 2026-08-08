using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;

using BTokenCore;


namespace BTokenCore_Testbench;

public class NetworkAdapterTCP : ISocketCommunication
{
  TcpClient TcpClient;
  string IP;
  int Port;


  public NetworkAdapterTCP(string iPAddress, int port)
  {
    TcpClient = new TcpClient();
    IP = iPAddress;
    Port = port;
  }

  public NetworkAdapterTCP(TcpClient tcpClient)
  {
    TcpClient = tcpClient;

    IPEndPoint iPEndPoint = TcpClient.Client.RemoteEndPoint as IPEndPoint;

    IP = iPEndPoint.Address.ToString();
    Port = iPEndPoint.Port;
  }

  public async Task<Stream> Start()
  {
    if (!TcpClient.Connected)
      await TcpClient.ConnectAsync(IP, Port).ConfigureAwait(false);

    return TcpClient.GetStream();
  }

  public void Dispose()
  {
    TcpClient.Dispose();
  }

  public string GetIP()
  {
    return IP;
  }
}
