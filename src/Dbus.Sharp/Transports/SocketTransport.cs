// Copyright 2006 Alp Toker <alp@atoker.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Mono.Unix;

namespace DBus.Transports
{
    internal class SocketTransport : Transport
    {
        public override void Open(AddressEntry entry)
        {
            string path;
            bool isAbstract;

            if (entry.Properties.TryGetValue("path", out path))
                isAbstract = false;
            else if (entry.Properties.TryGetValue("abstract", out path))
                isAbstract = true;
            else
                throw new ArgumentException("No path specified for UNIX transport");

            EndPoint ep;

            if (isAbstract)
                ep = new AbstractUnixEndPoint(path);
            else
                ep = new UnixEndPoint(path);

            var client = new Socket(AddressFamily.Unix, SocketType.Stream, 0);
            client.Connect(ep);
            Stream = new NetworkStream(client);
        }

        public override void WriteCred()
        {
            Stream.WriteByte(0);
        }

        public override string AuthString()
        {
            long uid = UnixUserInfo.GetRealUserId();

            return uid.ToString();
        }
    }
}
