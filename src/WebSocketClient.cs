using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net.WebSockets;

namespace WebSocketUtils
{
    public class WebSocketClient {
        readonly ConcurrentQueue<byte[]> _readBuffer;
        readonly ConcurrentQueue<byte[]> _writeBuffer;

        private ClientWebSocket connection;
        private bool _closed;

        private async void ProcessQueue() {
            while (!_closed) {
                try {
                    while (_writeBuffer.IsEmpty && !_closed) {
                        Thread.Sleep(10);
                    }
                    if (_closed) return;
                    while (_writeBuffer.TryDequeue(out var data)) {
                        // TODO - Is the data text?
                        await connection.SendAsync(data, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }
        private async void Read() {
            try {
                while (!_closed) {
                    var buffer = new ArraySegment<byte>(new byte[1000]);
                    WebSocketReceiveResult result = await connection.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) OnClose(false);
                    byte[] rv = buffer.Slice(0, result.Count).Array;
                    while (!result.EndOfMessage) {
                        result = await connection.ReceiveAsync(buffer, CancellationToken.None);
                        if (result.MessageType == WebSocketMessageType.Close) OnClose(false);
                        rv = rv.Concat(buffer.Slice(0, result.Count).Array).ToArray();
                    }
                    _readBuffer.Enqueue(rv);
                }
            } catch(Exception) {
                OnClose(true);
            }
        }

        private void OnClose(bool force) {
            if (force) {
                connection.CloseAsync(WebSocketCloseStatus.InternalServerError, "Error", CancellationToken.None);
            }
            _closed = true;
        }

        private async void Start(string url) {
            connection = new ClientWebSocket();
            var uri = new Uri(url);

            await connection.ConnectAsync(uri, CancellationToken.None);
            if (connection.State == WebSocketState.Open) {
                var t = new Thread(ProcessQueue);
                t.Start();
                var t2 = new Thread(Read);
                t2.Start();
            }
        }
        public WebSocketClient(string url) {
            _readBuffer = new ConcurrentQueue<byte[]>();
            _writeBuffer = new ConcurrentQueue<byte[]>();
            Start(url);
        }

        public void WriteData(string data) {
            WriteData(Encoding.UTF8.GetBytes(data));
        }
        public void WriteData(byte[] data) {
            _writeBuffer.Enqueue(data);
        }

        public bool TryRead() {
            return !_readBuffer.IsEmpty;
        }

        public byte[] ReadData() {
            if (_readBuffer.IsEmpty) return new byte[0];
            _readBuffer.TryDequeue(out var data);
            return data;
        }

        public string ReadDataAsString() {

            return Encoding.UTF8.GetString(ReadData());
        }
    }
}
