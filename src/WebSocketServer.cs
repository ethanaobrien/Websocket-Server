using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace WebSocketUtils
{
    public class WebSocketServer
    {

        private Socket _listener;
        private bool _running;
        private Set _settings;
        private struct Set
        {
            public readonly int Port;
            public bool LocalNetwork { get; set; }
            public Set(int port, bool localNetwork)
            {
                Port = port;
                LocalNetwork = localNetwork;
            }
        }
        private void SetupServer(int port, bool localNetwork)
        {
            _listener = null;
            _running = true;
            _settings = new Set(port, localNetwork);
            var mainThread = new Thread(ServerMain);
            mainThread.Start();
            NewConnections = new ConcurrentQueue<Socket>();
        }

        public WebSocketServer()
        {
            SetupServer(8080, true);
        }
        public WebSocketServer(int port, bool localNetwork)
        {
            SetupServer(port, localNetwork);
        }
        public void Terminate()
        {
            if (_listener == null) return;
            _running = false;
            try
            {
                _listener.Shutdown(SocketShutdown.Both);
            }
            catch (Exception)
            {
                // ignored
            }

            try
            {
                _listener.Close();
            }
            catch (Exception)
            {
                // ignored
            }

            _listener = null;
        }
        private void ServerMain()
        {
            try
            {
                var listenHost = _settings.LocalNetwork ? "0.0.0.0" : "127.0.0.1";
                var ipAddress = IPAddress.Parse(listenHost);
                var localEndPoint = new IPEndPoint(ipAddress, _settings.Port);
                _listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _listener.Bind(localEndPoint);
                _listener.Listen(100); //connection limit
                Console.WriteLine("Listening on http://{0}:{1}", listenHost, _settings.Port);
                while (_running)
                {
                    try
                    {
                        var handler = _listener.Accept();
                        var t = new Thread(OnRequest);
                        t.Start(handler);
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
                _listener.Shutdown(SocketShutdown.Both);
                _listener.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
        //Returns a list of
        public string[] GetUrLs()
        {
            if (_settings.LocalNetwork)
            {
                var rv = new List<string>();
                var ipEntry = Dns.GetHostEntry(Dns.GetHostName());
                var addr = ipEntry.AddressList;
                rv.Add("127.0.0.1");
                for (int i = 0; i < addr.Length; i++)
                {
                    if (addr[i].AddressFamily != AddressFamily.InterNetwork) continue;
                    rv.Add(addr[i].ToString());
                }
                return rv.ToArray();
            }
            else
            {
                return new [] { "127.0.0.1" };
            }
        }
        
        private void OnRequest(object obj)
        {
            var handler = (Socket)obj;
            try
            {
                var consumed = false;
                var data = "";
                while (!consumed)
                {
                    var bytes = new byte[1];
                    var bytesRec = handler.Receive(bytes);
                    if (bytesRec == 0) break;
                    data += Encoding.UTF8.GetString(bytes, 0, bytesRec);
                    consumed = (data.Contains("\r\n\r\n"));
                }

                if (!data.Contains("Sec-WebSocket-Key: ") && consumed)
                {
                    var msg = Encoding.UTF8.GetBytes("You shouldn't be here");
                    var header = "HTTP/1.1 403 Forbidden\r\n" + 
                                 "Connection: close\r\n" + 
                                 "Access-Control-Allow-Origin: *\r\n" + 
                                 "Content-Length: " + msg.Length + "\r\n\r\n" + 
                                 msg;
                    var toSend = Encoding.UTF8.GetBytes(header);
                    handler.Send(toSend);
                }
                else if (consumed)
                {
                    //Websocket connection
                    //Console.WriteLine("Websocket connection");
                    //First, We need to do a handshake
                    var response = Encoding.UTF8.GetBytes("HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nAccess-Control-Allow-Origin: *\r\nSec-WebSocket-Accept: " + Convert.ToBase64String(
                        System.Security.Cryptography.SHA1.Create().ComputeHash(
                            Encoding.UTF8.GetBytes(
                                new System.Text.RegularExpressions.Regex("Sec-WebSocket-Key: (.*)").Match(data).Groups[1].Value.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"
                            )
                        )
                    ) + "\r\n\r\n");

                    handler.Send(response);
                    //We are now connected
                    
                    NewConnections.Enqueue(handler);

                    while (handler.Connected) Thread.Sleep(50);

                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: {0}", e);
            }
            try {
                handler.Shutdown(SocketShutdown.Both);
                handler.Close();
            }
            catch (Exception)
            {
                // ignored
            }
        }
        
        
        private ConcurrentQueue<Socket> NewConnections;
        public bool NewConnection() {
            if (NewConnections is null) return false;
            return !NewConnections.IsEmpty;
        }

        public WebSocketParser GetNewConnectionHandle()
        {
            if (NewConnections.IsEmpty) {
                return null;
            }
            NewConnections.TryDequeue(out var socket);
            return new WebSocketParser(socket, false);
        }
    }
}
