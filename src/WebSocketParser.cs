using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace WebSocketUtils
{
    public class WebSocketParser {
        private readonly byte[] _mask;
        private ulong _length;
        private ulong _consumed;
        private readonly Socket _handler;
        private bool _inIsString;
        private readonly bool _echo;
        
        readonly ConcurrentQueue<List<byte[]>> _writeBuffer;
        
        private void AddToQueue(byte[] header, byte[] data) {
            var toAdd = new List<byte[]> {header, data};
            _writeBuffer.Enqueue(toAdd);
        }
        private void ProcessQueue() {
            while (_handler.Connected) {
                try {
                    while (_writeBuffer.IsEmpty && _handler.Connected) {
                        Thread.Sleep(10);
                    }
                    if (!_handler.Connected) return;
                    while (_writeBuffer.TryDequeue(out var data)) {
                        foreach (var toWrite in data) {
                            _handler.Send(toWrite);
                        }
                    }
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }


        public WebSocketParser(Socket handler, bool echo) {
            _writeBuffer = new ConcurrentQueue<List<byte[]>>();
            _mask = new byte[4];
            _length = 0;
            _consumed = 0;
            _inIsString = false;
            _handler = handler;
            _echo = echo;
            var t = new Thread(ProcessQueue);
            t.Start();
        }

        private void StreamBytes(IReadOnlyCollection<WebSocketParser> dest) {
            if (dest.Count == 0) return;
            var header = GetHeader(_length-_consumed, _inIsString);
            var data = ReadBytes(_length-_consumed);
            foreach(var parser in dest) {
                if (!_echo && Equals(parser._handler, _handler)) continue;
                parser.AddToQueue(header, data);
            }
        }
        public string ReadBytesAsString() {
            var res = ReadBytes(_length);
            return Encoding.UTF8.GetString(res, 0, res.Length);
        }
        public byte[] ReadBytes() {
            return ReadBytes(_length);
        }

        public byte[] ReadBytes(ulong len) {
            if (_length-_consumed < len) {
                len = _length-_consumed;
            }
            byte[] decoded = new byte[len];
            byte[] bytes = new byte[len];
            _handler.Receive(bytes, 0, (int)len, SocketFlags.None);
            for (ulong i=0; i < len; ++i, ++_consumed) {
                decoded[i] = (byte)(bytes[i] ^ _mask[_consumed % 4]);
            }
            return decoded;
        }
        public void WriteString(string data) {
            var toWrite = Encoding.UTF8.GetBytes(data);
            AddToQueue(GetHeader((ulong)toWrite.Length, true), toWrite);
        }
        public void WriteBytes(byte[] data) {
            AddToQueue(GetHeader((ulong)data.Length, false), data);
        }
        public void WriteBytes(byte[] data, bool isString) {
            AddToQueue(GetHeader((ulong)data.Length, isString), data);
        }
        private byte[] GetHeader(ulong len, bool isString) {
            return GetHeader(len, (isString?1:2));
        }

        private byte[] GetHeader(ulong len, int opcode) {
            //Credit to https://github.com/MazyModz/CSharp-WebSocket-Server/blob/master/Library/Helpers.cs
            int indexStartRawData;
            var frame = new byte[10];

            frame[0] = (byte)(128 + opcode);
            switch (len)
            {
                case <= 125:
                    frame[1] = (byte)len;
                    indexStartRawData = 2;
                    break;
                case >= 126 and <= 65535:
                    frame[1] = (byte)126;
                    frame[2] = (byte)((len >> 8) & 255);
                    frame[3] = (byte)(len & 255);
                    indexStartRawData = 4;
                    break;
                default:
                    frame[1] = (byte)127;
                    frame[2] = (byte)((len >> 56) & 255);
                    frame[3] = (byte)((len >> 48) & 255);
                    frame[4] = (byte)((len >> 40) & 255);
                    frame[5] = (byte)((len >> 32) & 255);
                    frame[6] = (byte)((len >> 24) & 255);
                    frame[7] = (byte)((len >> 16) & 255);
                    frame[8] = (byte)((len >> 8) & 255);
                    frame[9] = (byte)(len & 255);
                    indexStartRawData = 10;
                    break;
            }

            var response = new byte[indexStartRawData];
            for (var i=0; i < indexStartRawData; i++) {
                response[i] = frame[i];
            }
            return response;
        }
        public bool ClientConnected() {
            return _handler.Connected;
        }
        public void CloseSocket() {
            Console.WriteLine("close");
            while (!_writeBuffer.IsEmpty) {
                Thread.Sleep(10);
            }
            try {
                _handler.Shutdown(SocketShutdown.Both);
                _handler.Close();
            }
            catch (Exception)
            {
                // ignored
            }
        }
        public bool TryRead() {
            if (!_handler.Connected) Console.WriteLine("not connected");
            if (_handler.Available == 0 && _handler.Connected) return false;
            if (!_handler.Connected) return true;
            var head = new byte[2];
            _handler.Receive(head, 0, 2, SocketFlags.None);
            var opcode = (head[0]-128);
            Console.WriteLine(opcode);
            _inIsString = (opcode == 1);
            var mask = (head[1] & 0b10000000) != 0;
            var msglen = (ulong)(head[1] & 0b01111111);
        
            switch (msglen)
            {
                case 126:
                {
                    var size = new byte[2];
                    _handler.Receive(size, 0, 2, SocketFlags.None);
                    msglen = (ulong)BitConverter.ToInt16(new[] { size[1], size[0] }, 0);
                    break;
                }
                case 127:
                {
                    var size = new byte[8];
                    _handler.Receive(size, 0, 8, SocketFlags.None);
                    msglen = BitConverter.ToUInt64(new[] { size[7], size[6], size[5], size[4], size[3], size[2], size[1], size[0] }, 0);
                    break;
                }
            }
            _length = msglen;
            _consumed = 0;
        
            if (mask) {
                var masks = new byte[4];
                _handler.Receive(masks, 0, 4, SocketFlags.None);
                _mask[0] = masks[0];
                _mask[1] = masks[1];
                _mask[2] = masks[2];
                _mask[3] = masks[3];
                if (opcode == 9) AddToQueue(GetHeader(0, 10), new byte[0]);
                if (msglen == 0) return false;
            } else {
                CloseSocket();
            }
            return true;
        }
        public override int GetHashCode() {
            return _handler.GetHashCode();
        }
        public override bool Equals(object otherObj) {
            var other = otherObj as WebSocketParser;
            return other != null && other._handler.Equals(_handler);
        }

        public static bool operator == (WebSocketParser p1, WebSocketParser p2)
        {
            return p1 is not null && p1.Equals(p2);
        }

        public static bool operator != (WebSocketParser p1, WebSocketParser p2) {
            if(p1 is null) return false;
            return !p1.Equals(p2);
        }
    }
}
