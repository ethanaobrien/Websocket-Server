using System;
using System.Threading;
using System.Collections.Generic;
using WebSocketUtils;

namespace main
{
    // Because static keyword
    public class StaticIsAnnoying {
        private List<WebSocketParser> connections;
        private void HandleSockets() {
            for (var i=0; i<connections.Count; i++) {
                var user = connections[i];
                if (!user.TryRead()) continue;
                if (!user.ClientConnected()) {
                    Console.WriteLine("Disconnect");
                    connections.Remove(user);
                    i--;
                    continue;
                }
                var message = user.ReadBytesAsString();
                Console.WriteLine("Got Message: '" + message + "'");
                user.WriteString(message);
            }
        }

        public StaticIsAnnoying() {
            connections = new List<WebSocketParser>();
            var server = new WebSocketServer();
			while (true) {
                Thread.Sleep(10);
                HandleSockets();
				if (!server.NewConnection()) continue;
				var connection = server.GetNewConnectionHandle();
                if (connection == null) continue;
                connections.Add(connection);
			}
        }
    }
    public class StaticIsAnnoyingPart2 {

        public StaticIsAnnoyingPart2() {
            var connection = new WebSocketClient("ws://127.0.0.1:8080/");
            Thread.Sleep(1000);
            connection.WriteData("test");
            while (true) {
                if (connection.TryRead()) {
                    Console.WriteLine(connection.ReadDataAsString());
                    Thread.Sleep(1000);
                    connection.WriteData("Boogerz");
                }
                Thread.Sleep(50);
            }
        }
    }
    public class main
    {
        public static void Main()
        {
            //new StaticIsAnnoying();
            new StaticIsAnnoyingPart2();
        }
    }
}
