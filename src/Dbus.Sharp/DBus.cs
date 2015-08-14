// Copyright 2006 Alp Toker <alp@atoker.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using System.Collections.Generic;
using DBus;
using System.Threading.Tasks;

namespace org.freedesktop.DBus
{
    [Flags]
    public enum NameFlag : uint
    {
        None = 0,
        AllowReplacement = 0x1,
        ReplaceExisting = 0x2,
        DoNotQueue = 0x4,
    }

    public enum RequestNameReply : uint
    {
        PrimaryOwner = 1,
        InQueue,
        Exists,
        AlreadyOwner,
    }

    public enum ReleaseNameReply : uint
    {
        Released = 1,
        NonExistent,
        NotOwner,
    }

    public enum StartReply : uint
    {
        // The service was successfully started.
        Success = 1,
        // A connection already owns the given name.
        AlreadyRunning,
    }

    public delegate void NameOwnerChangedHandler(string name, string old_owner, string new_owner);
    public delegate void NameAcquiredHandler(string name);
    public delegate void NameLostHandler(string name);

    [Interface("org.freedesktop.DBus.Peer")]
    public interface Peer
    {
        Task PingAsync();
        [return: Argument("machine_uuid")]
        Task<string> GetMachineIdAsync();
    }

    [Interface("org.freedesktop.DBus.Introspectable")]
    public interface Introspectable
    {
        [return: Argument("data")]
        Task<string> IntrospectAsync();
    }

    [Interface("org.freedesktop.DBus.Properties")]
    public interface Properties
    {
        [return: Argument("value")]
        Task<object> GetAsync(string interfaceName, string propname);
        Task SetAsync(string interfaceName, string propname, object value);
        [return: Argument("props")]
        Task<IDictionary<string, object>> GetAllAsync(string interfaceName);
    }

    [Interface("org.freedesktop.DBus")]
    public interface IBus
    {
        Task<RequestNameReply> RequestNameAsync(string name, NameFlag flags);
        Task<ReleaseNameReply> ReleaseNameAsync(string name);
        Task<string> HelloAsync();
        Task<string[]> ListNamesAsync();
        Task<string[]> ListActivatableNamesAsync();
        Task<bool> NameHasOwnerAsync(string name);
        event NameOwnerChangedHandler NameOwnerChanged;
        event NameLostHandler NameLost;
        event NameAcquiredHandler NameAcquired;
        Task<StartReply> StartServiceByNameAsync(string name, uint flags);
        Task UpdateActivationEnvironmentAsync(IDictionary<string, string> environment);
        Task<string> GetNameOwnerAsync(string name);
        Task<uint> GetConnectionUnixUserAsync(string connection_name);
        Task AddMatchAsync(string rule);
        Task RemoveMatchAsync(string rule);
        Task<string> GetIdAsync();

        //undocumented in spec
        Task<string[]> ListQueuedOwnersAsync(string name);
        Task<uint> GetConnectionUnixProcessIDAsync(string connection_name);
        Task<byte[]> GetConnectionSELinuxSecurityContextAsync(string connection_name);
        Task ReloadConfigAsync();
    }
}
