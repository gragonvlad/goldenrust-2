using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Network;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("RSM", "RustServerManager", "1.0.2")]
    [Description("Allows RSM to have more control over the server.")]
    public class RSM : RustPlugin
    {
        private void Init()
        {
            cmd.AddConsoleCommand("joinlist", this, nameof(GetJoiningPlayers));
            cmd.AddConsoleCommand("queuelist", this, nameof(GetQueuedPlayers));
            cmd.AddConsoleCommand("removequeue", this, nameof(RemovePlayerFromQueue));
            cmd.AddConsoleCommand("removejoining", this, nameof(RemovePlayerFromJoining));
            cmd.AddConsoleCommand("loadedplugins", this, nameof(ListPlugins));
        }        

        private void RemovePlayerFromQueue(ConsoleSystem.Arg arg)
        {
            if (arg.IsAdmin == false)
            {             
                return;
            }

            if (arg.Args == null || arg.Args?.Length < 1)
            {
                Puts($"removequeue steamID reason");
                return;
            }

            var targetID = 0ul;
            if (ulong.TryParse(arg.Args[0], out targetID) == false)
            {
                Puts($"removequeue steamID");
                return;
            }

            var targetReason = arg.Args[1];

            var connection = ServerMgr.Instance.connectionQueue.queue.FirstOrDefault(x => x.userid == targetID);
            if (connection == null)
            {
                Puts($"Player {targetID} does not exists in the queue!");
                return;
            }

            Net.sv.Kick(connection, targetReason);
            Puts($"Kicked {connection.username}[{connection.userid}] from queue. Reason: {targetReason}");
        }

        private void RemovePlayerFromJoining(ConsoleSystem.Arg arg)
        {
            if (arg.IsAdmin == false)
            {
                return;
            }

            if (arg.Args == null || arg.Args?.Length < 1)
            {
                Puts($"removejoining steamID reason");
                return;
            }

            var targetID = 0ul;
            if (ulong.TryParse(arg.Args[0], out targetID) == false)
            {
                Puts($"removejoining steamID");
                return;
            }

            var targetReason = arg.Args[1];

            var connection = ServerMgr.Instance.connectionQueue.joining.FirstOrDefault(x => x.userid == targetID);
            if (connection == null)
            {
                Puts($"Player {targetID} does not exists in the queue!");
                return;
            }

            Net.sv.Kick(connection, targetReason);
            Puts($"Kicked {connection.username}[{connection.userid}] from joining. Reason: {targetReason}");
        }

        private void GetJoiningPlayers(ConsoleSystem.Arg arg)
        {
            if (arg.IsAdmin == false)
            {
                return;
            }

            var players = ServerMgr.Instance.connectionQueue.joining;
            var list = new List<PlayerInfo>();

            foreach (var player in players)
            {
                list.Add(new PlayerInfo
                {
                    authLevel = player.authLevel,
                    userid = player.userid,
                    ownerid = player.ownerid,
                    username = player.username,
                    os = player.os,
                    connectionTime = player.connectionTime,
                    ipaddress = player.ipaddress
                });
            }            

           // Debug.Log(JsonConvert.SerializeObject(list));
            arg.ReplyWith(JsonConvert.SerializeObject(list));
        }

        private void ListPlugins(ConsoleSystem.Arg arg)
        {
            if (arg.IsAdmin == false)
            {
                return;
            }

            var plugs = Interface.Oxide.RootPluginManager.GetPlugins();
            var list = new List<PluginInfo>();

            foreach (var aplugin in plugs)
            {
                if (aplugin?.Config == null) continue;
                var enumerator = aplugin.Config.GetEnumerator();
                var aconfig = new Dictionary<string, object>();
                while (enumerator.MoveNext())
                {
                    aconfig.Add(enumerator.Current.Key, enumerator.Current.Value);
                }

                list.Add(new PluginInfo
                {
                    Name = aplugin.Name,
                    Title = aplugin.Title,
                    Description = aplugin.Description,
                    Author = aplugin.Author,
                    Version = aplugin.Version.ToString(),
                    HasMessages = aplugin.HasMessages,
                    Filename = aplugin.Filename,
                    IsCorePlugin = aplugin.IsCorePlugin,
                    IsLoaded = aplugin.IsLoaded,
                    HasConfig = aplugin.HasConfig,
                    TotalHookTime = aplugin.TotalHookTime,
                   // Config = aconfig
                });
            }

            arg.ReplyWith(JsonConvert.SerializeObject(list));
        }

        private void GetQueuedPlayers(ConsoleSystem.Arg arg)
        {
            if (arg.IsAdmin == false)
            {
                return;
            }

            var players = ServerMgr.Instance.connectionQueue.queue;
            var list = new List<PlayerInfo>();

            foreach (var player in players)
            {
                list.Add(new PlayerInfo
                {
                    authLevel = player.authLevel,
                    userid = player.userid,
                    ownerid = player.ownerid,
                    username = player.username,
                    os = player.os,
                    connectionTime = player.connectionTime,
                    ipaddress = player.ipaddress
                });
            }

            //Debug.Log(JsonConvert.SerializeObject(list));
            arg.ReplyWith(JsonConvert.SerializeObject(list));
        }

        private class PlayerInfo
        {
            public uint authLevel;
            public ulong userid;
            public ulong ownerid;
            public string username;
            public string os;
            public double connectionTime;
            public string ipaddress;
        }

        private class PluginInfo
        {
            public string Name;
            public string Title;
            public string Description;
            public string Author;
            public string Version;
            public bool HasMessages;
            public string Filename;
            public bool IsCorePlugin;
            public bool IsLoaded;
            public bool HasConfig;
            public double TotalHookTime;
            public object Config;
        }
    }
}