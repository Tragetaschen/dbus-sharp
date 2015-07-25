// Copyright 2006 Alp Toker <alp@atoker.com>
// This software is made available under the MIT License
// See COPYING for details

using System.IO;
using System.Net;
using System.Net.Sockets;
using Mono.Unix;

namespace DBus.Transports
{
    class UnixMonoTransport : UnixTransport
    {
        public override void Open(string path, bool isAbstract)
        {
            EndPoint ep;

            if (isAbstract)
                ep = new AbstractUnixEndPoint(path);
            else
                ep = new UnixEndPoint(path);

            var client = new Socket(AddressFamily.Unix, SocketType.Stream, 0);
            client.Connect(ep);
            Stream = new NetworkStream(client);
        }

        //send peer credentials null byte. note that this might not be portable
        //there are also selinux, BSD etc. considerations
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
