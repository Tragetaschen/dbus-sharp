// Copyright 2006 Alp Toker <alp@atoker.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using System.Collections.Generic;
using org.freedesktop.DBus;
using System.Threading.Tasks;

namespace DBus
{
    public sealed class Bus : Connection
    {
        static readonly string DBusName = "org.freedesktop.DBus";
        static readonly ObjectPath DBusPath = new ObjectPath("/org/freedesktop/DBus");

        static Dictionary<string, Bus> buses = new Dictionary<string, Bus>();

        static Bus starterBus = null;

        static Bus systemBus = Address.StarterBusType == "system" ? Starter : (Address.System != null ? Bus.Open(Address.System) : null);
        static Bus sessionBus = Address.StarterBusType == "session" ? Starter : (Address.Session != null ? Bus.Open(Address.Session) : null);

        IBus bus;
        string address;
        string uniqueName;

        public static Bus System
        {
            get
            {
                return systemBus;
            }
        }

        public static Bus Session
        {
            get
            {
                return sessionBus;
            }
        }

        public static Bus Starter
        {
            get
            {
                if (starterBus == null)
                {
                    try
                    {
                        starterBus = Bus.Open(Address.Starter);
                    }
                    catch (Exception e)
                    {
                        throw new Exception("Unable to open the starter message bus.", e);
                    }
                }

                return starterBus;
            }
        }

        public static new Bus Open(string address)
        {
            if (address == null)
                throw new ArgumentNullException("address");

            Bus bus;
            if (buses.TryGetValue(address, out bus))
                return bus;

            bus = new Bus(address);
            buses[address] = bus;

            return bus;
        }

        public Bus(string address) : base(address)
        {
            this.bus = GetObjectAsync<IBus>(DBusName, DBusPath, false).Result;
            this.address = address;
            RegisterAsync().Wait();
        }

        //should this be public?
        //as long as Bus subclasses Connection, having a Register with a completely different meaning is bad
        async Task RegisterAsync()
        {
            if (uniqueName != null)
                throw new Exception("Bus already has a unique name");

            uniqueName = await bus.HelloAsync();
        }

        protected override void CloseInternal()
        {
            /* In case the bus was opened with static method
			 * Open, clear it from buses dictionary
			 */
            if (buses.ContainsKey(address))
                buses.Remove(address);
        }

        protected override Task<bool> CheckBusNameExistsAsync(string busName)
        {
            if (busName == DBusName)
                return Task.FromResult(true);
            return NameHasOwnerAsync(busName);
        }

        public Task<uint> GetUnixUserAsync(string name)
        {
            return bus.GetConnectionUnixUserAsync(name);
        }

        public Task<RequestNameReply> RequestNameAsync(string name)
        {
            return RequestNameAsync(name, NameFlag.None);
        }

        public Task<RequestNameReply> RequestNameAsync(string name, NameFlag flags)
        {
            return bus.RequestNameAsync(name, flags);
        }

        public Task<ReleaseNameReply> ReleaseNameAsync(string name)
        {
            return bus.ReleaseNameAsync(name);
        }

        public Task<bool> NameHasOwnerAsync(string name)
        {
            return bus.NameHasOwnerAsync(name);
        }

        public Task<StartReply> StartServiceByNameAsync(string name)
        {
            return StartServiceByNameAsync(name, 0);
        }

        public Task<StartReply> StartServiceByNameAsync(string name, uint flags)
        {
            return bus.StartServiceByNameAsync(name, flags);
        }

        internal protected override Task AddMatchAsync(string rule)
        {
            return bus.AddMatchAsync(rule);
        }

        internal protected override Task RemoveMatchAsync(string rule)
        {
            return bus.RemoveMatchAsync(rule);
        }

        public Task<string> GetIdAsync()
        {
            return bus.GetIdAsync();
        }

        public string UniqueName
        {
            get
            {
                return uniqueName;
            }
            set
            {
                if (uniqueName != null)
                    throw new Exception("Unique name can only be set once");
                uniqueName = value;
            }
        }
    }
}
