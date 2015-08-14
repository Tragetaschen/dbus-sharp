// Copyright 2007 Alp Toker <alp@atoker.com>
// This software is made available under the MIT License
// See COPYING for details

using System.Threading;
using System.Threading.Tasks;

namespace DBus.Protocol
{
    public class PendingCall
    {
        private TaskCompletionSource<Message> tcs;
        private Connection conn;

        public PendingCall(Connection conn)
        {
            this.conn = conn;
            tcs = new TaskCompletionSource<Message>();
        }

        public Task<Message> GetReply()
        {
            var task = tcs.Task;

            if (Thread.CurrentThread == conn.mainThread)
            {
                while (!task.IsCompleted)
                    conn.HandleMessage(conn.Transport.ReadMessage());

                conn.DispatchSignals();
            }

            return task;
        }

        public void SetReply(Message value)
        {
            tcs.SetResult(value);
        }
    }
}
