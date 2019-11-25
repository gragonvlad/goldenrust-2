using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Ext.Discord;
using Oxide.Ext.Discord.Attributes;
using Oxide.Ext.Discord.DiscordObjects;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Rustcord", "Kirollos & OuTSMoKE", "1.8.1")]
    [Description("Complete game server monitoring through discord.")]
    internal class Rustcord : RustPlugin
    {
        [PluginReference] Plugin PrivateMessages, BetterChatMute, Clans, AdminChat, DiscordAuth;
        [DiscordClient] private DiscordClient _client;

        private Settings _settings;

        private class Settings
        {
            public string Apikey { get; set; }
            public bool AutoReloadPlugin { get; set; }
            public int AutoReloadTime { get; set; }
            private string _Botid { get; set; }
            public string Botid(string id = null)
            {
                if (id != null)
                {
                    _Botid = id;
                }
                return _Botid;
            }
            public string Commandprefix { get; set; }
            public List<Channel> Channels { get; set; }
            public Dictionary<string, List<string>> Commandroles { get; set; }

            public class Channel
            {
                public string Channelid { get; set; }
                public List<string> perms { get; set; }
            }
            public List<string> FilterWords;
            public string FilteredWord;
            public List<string> LogExcludeGroups;
            public List<string> LogExcludePerms;
        }

        //CONFIG FILE
        private Settings GetDefaultSettings()
        {
            return new Settings
            {
                Apikey = "BotToken",
                AutoReloadPlugin = true,
                AutoReloadTime = 501,
                Commandprefix = "!",
                Channels = new List<Settings.Channel>
                    {
                        new Settings.Channel
                            {
                                Channelid = string.Empty,
                                perms = new List<string>()
                                {
                                    "cmd_allow",
                                    "cmd_players",
                                    "cmd_kick",
                                    "cmd_com",
                                    "cmd_mute",
                                    "cmd_unmute",
                                    "msg_join",
                                    "msg_joinlog",
                                    "msg_quit",
                                    "death_pvp",
                                    "msg_chat",
                                    "game_report",
                                    "game_bug",
                                    "msg_serverinit"
                                }
                            }
                    },
                Commandroles = new Dictionary<string, List<string>>
                    {
                        {
                            "command", new List<string>()
                                {
                                    "rolename1",
                                    "rolename2"
                                }
                        }
                    },
                FilterWords = new List<string>
                    {
                        "badword1",
                        "badword2"
                    },
                FilteredWord = "<censored>",
                LogExcludeGroups = new List<string>
                    {
                        "default"
                    },
                LogExcludePerms = new List<string>
                    {
                        "example.permission"
                    }
            };
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Attempting to create default config...");
            Config.Clear();
            Config.WriteObject(GetDefaultSettings(), true);
            Config.Save();
        }
        //END CONFIG FILE

        private void OnServerInitialized()
        {
            var reloadtime = _settings.AutoReloadTime;

            if (_client != null)
            {
                _settings?.Channels.Where(x => x.perms.Contains("msg_serverinit")).ToList().ForEach(ch => {
                    GetChannel(_client, ch.Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnInitMsg"));
                    });
                });
            }
            permission.RegisterPermission("rustcord.hidejoinquit", this);
            permission.RegisterPermission("rustcord.hidechat", this);

            if (_settings.AutoReloadPlugin && _settings.AutoReloadTime > 59)
            {
                timer.Every(reloadtime, () => Reload());
            }
        }

        private void Loaded()
        {
            _settings = Config.ReadObject<Settings>();

            if (string.IsNullOrEmpty(_settings.Apikey) || _settings.Apikey == null || _settings.Apikey == "BotToken")
            {
                PrintError("API key is empty or invalid!");
                return;
            }
            try
            {
                Oxide.Ext.Discord.Discord.CreateClient(this, _settings.Apikey);
            }
            catch (Exception e)
            {
                PrintError($"Rustcord failed to create client! Exception message: {e.Message}");
            }
        }

        private void Reload()
        {
            rust.RunServerCommand("oxide.reload Rustcord");
        }

        void Discord_Ready(Oxide.Ext.Discord.DiscordEvents.Ready rdy)
        {
            timer.Once(1f, () => {
                Puts("Connection established to " + _client.DiscordServer.name);
                _settings?.Channels.Where(x => x.perms.Contains("msg_plugininit")).ToList().ForEach(ch => {
                    GetChannel(_client, ch.Channelid, (Channel c) => {
                        c.CreateMessage(_client, "Rustcord Initialized!");
                    });
                });
            });
            _settings.Botid(rdy.User.id);
        }

        private void Unload()
        {
            Oxide.Ext.Discord.Discord.CloseClient(_client);
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                { "Discord_PlayersResponse", ":mag_right: Connected Players [{count}/{maxplayers}]: {playerslist}" },
                { "RUST_OnInitMsg", ":vertical_traffic_light: Server is back online! Players may now re-join. :vertical_traffic_light:" },
                { "RUST_OnPlayerGesture", ":speech_left: {playername}: {gesture}"},
                { "RUST_OnPlayerChat", ":speech_left: {playername}: {message}"},
                { "RUST_OnPlayerJoin", ":white_check_mark: {playername} has connected!" },
                { "RUST_OnPlayerJoinAdminLog", ":clipboard: {playername} has connected! (IP: {playerip}    SteamID: {playersteamid})" },
                { "RUST_OnPlayerQuit", ":x: {playername} has disconnected! ({reason})" },
                { "RUST_OnPlayerReport", ":warning: {playername}: {message}"},
                { "RUST_OnPlayerBug", ":beetle: {playername}: {message}"},
                { "RUST_OnPlayerPM", "[PM] {sender}  :incoming_envelope: {target}: {message}"},
                { "RUST_OnPlayerMute", "[MUTE] :zipper_mouth: {muter} has permanently muted {target}. Reason: {reason}"},
                { "RUST_OnPlayerTimedMute", "[MUTE] :hourglass_flowing_sand: {muter} has been temporarily muted {target} for {time}. Reason: {reason}"},
                { "RUST_OnPlayerUnMute", "[MUTE] :loudspeaker: {unmuter} has unmuted {target}."},
                { "RUST_OnPlayerMuteExpire", "[MUTE] :hourglass: {target}'s temporary mute has expired."},
                { "RUST_OnPlaneSpawn", ":airplane: Cargo Plane has spawned."},
                { "RUST_OnBradleySpawn", ":trolleybus: Bradley APC has spawned."},
                { "RUST_OnShipSpawn", ":ship: Cargo Ship has spawned."},
                { "RUST_OnHeliSpawn", ":helicopter: Patrol Helicopter has spawned."},
                { "RUST_OnChinookSpawn", ":helicopter: Chinook Helicopter has spawned."},
                { "RUST_OnClanCreate", ":family_mwgb: Clan [{clantag}] has been created."},
                { "RUST_OnClanDestroy", ":family_mwgb: Clan [{clantag}] has been disbanded."},
                { "RUST_OnClanChat", ":speech_left: [CLANS] {playername}: {message}"},
                { "RUST_OnGroupCreated", ":desktop: Group {groupname} has been created."},
                { "RUST_OnGroupDeleted", ":desktop: Group {groupname} has been deleted."},
                { "RUST_OnUserGroupAdded", ":desktop: {playername} ({steamid}) has been added to group: {group}."},
                { "RUST_OnUserGroupRemoved", ":desktop: {playername} ({steamid}) has been removed from group: {group}."},
                { "RUST_OnUserPermissionGranted", ":desktop: {playername} ({steamid}) has been granted permission: {permission}."},
                { "RUST_OnGroupPermissionGranted", ":desktop: Group {group} has been granted permission: {permission}."},
                { "RUST_OnUserPermissionRevoked", ":desktop: {playername} ({steamid}) has been revoked permission: {permission}."},
                { "RUST_OnGroupPermissionRevoked", ":desktop: Group {group} has been revoked permission: {permission}."},
                { "RUST_OnPlayerKicked", ":desktop: {playername} has been kicked for: {reason}"},
                { "RUST_OnPlayerBanned", ":desktop: {playername} ({steamid}/{ip}) has been banned for: {reason}"}, //only works with vanilla/native system atm
				{ "RUST_OnPlayerUnBanned", ":desktop: {playername} ({steamid}/{ip}) has been unbanned."}, //only works with vanilla/native system atm
				{ "RUST_OnPlayerNameChange", ":desktop: {oldname} ({steamid}) is now playing as {newname}."},
                { "RUST_OnF1ItemSpawn", ":desktop: {name}: {givemessage}."},
                { "PLUGIN_DiscordAuth_Auth", ":lock: {gamename} has linked to Discord account {discordname}."},
                { "PLUGIN_DiscordAuth_Deauth", ":unlock: {gamename} has been unlinked from Discord account {discordname}."},
                { "PLUGIN_SignArtist", "{player} posted an image to a sign:"}
            }, this);
        }
        private void OnPlayerChat(ConsoleSystem.Arg arg)
        {
            if (_client == null) return;
            var player = arg.Player();
            var message = arg.GetString(0);
            if (player == null || message == null) return;
            if (permission.UserHasPermission(player.UserIDString, "rustcord.hidechat")) return;
            if (BetterChatMute?.Call<bool>("API_IsMuted", player.IPlayer) ?? false) return;
            for (int i = _settings.FilterWords.Count - 1; i >= 0; i--)
            {
                while (message.Contains(" " + _settings.FilterWords[i] + " ") || message.Contains(_settings.FilterWords[i]))
                    message = message.Replace(_settings.FilterWords[i], _settings.FilteredWord);
            }

            _settings.Channels.Where(x => x.perms.Contains("msg_chat")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    if (!(player != null && player.IsValid())) return;
                    chan.CreateMessage(_client, Translate("RUST_OnPlayerChat", new Dictionary<string, string> {
                        { "playername", player.displayName },
                        { "message", message },
                        { "playersteamid", player.UserIDString }

                    }));
                });
            });
        }

        private void OnPlayerInit(BasePlayer player)
        {
            if (_client == null) return;
            if (player == null) return;
            _settings.Channels.Where(x => x.perms.Contains("msg_joinlog")).ToList().ForEach(ch => {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    if (!(player != null && player.IsValid())) return;
                    // Admin
                    chan.CreateMessage(_client, Translate("RUST_OnPlayerJoinAdminLog", new Dictionary<string, string>
                    {
                        { "playername", player.displayName },
                        { "playerip",  player.net.connection.ipaddress.Substring(0, player.net.connection.ipaddress.IndexOf(":"))},
                        { "playersteamid", player.UserIDString }
                    }));
                });
            });

            if (permission.UserHasPermission(player.UserIDString, "rustcord.hidejoinquit")) return;

            _settings.Channels.Where(x => x.perms.Contains("msg_join")).ToList().ForEach(ch => {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    if (!(player != null && player.IsValid())) return;
                    chan.CreateMessage(_client, Translate("RUST_OnPlayerJoin", new Dictionary<string, string>
                    {
                        { "playername", player.displayName }
                    }));
                });
            });
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (_client == null) return;
            if (player == null || string.IsNullOrEmpty(reason)) return;
            if (permission.UserHasPermission(player.UserIDString, "rustcord.hidejoinquit"))
                return;
            _settings.Channels.Where(x => x.perms.Contains("msg_quit")).ToList().ForEach(ch => {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnPlayerQuit", new Dictionary<string, string>
                    {
                        { "playername", player.displayName },
                        { "reason", reason }
                    }));
                });
            });
        }

        private void OnDeathNotice(Dictionary<string, object> data, string message)
        {
            if (_client == null) return;
            if (data["VictimEntityType"] == null || data["KillerEntityType"] == null) return;
            int victimType = (int)data["VictimEntityType"];
            int killerType = (int)data["KillerEntityType"];

            var _DeathNotes = plugins.Find("DeathNotes");

            if (_DeathNotes != null)
                if ((victimType == 5 && (killerType == 5 || killerType == 6 || killerType == 7 || killerType == 8 || killerType == 9 || killerType == 10 || killerType == 11 || killerType == 12 || killerType == 14 || killerType == 15)))
                {
                    message = (string)_DeathNotes.Call("StripRichText", message);
                    _settings.Channels.Where(x => x.perms.Contains("death_pvp")).ToList().ForEach(ch =>
                    {
                        GetChannel(_client, ch.Channelid, chan =>
                        {
                            chan.CreateMessage(_client, ":skull_crossbones: " + message);
                        });
                    });
                }
                else if ((victimType == 2 && killerType == 5) || (victimType == 5 && killerType == 2))
                {
                    message = (string)_DeathNotes.Call("StripRichText", message);
                    _settings.Channels.Where(x => x.perms.Contains("death_animal")).ToList().ForEach(ch =>
                    {
                        GetChannel(_client, ch.Channelid, chan =>
                        {
                            chan.CreateMessage(_client, ":skull_crossbones: " + message);
                        });
                    });
                }
                else if ((victimType == 5 && (killerType == 0 || killerType == 1)) || ((victimType == 0 || victimType == 1) && (killerType == 5)))
                {
                    message = (string)_DeathNotes.Call("StripRichText", message);
                    _settings.Channels.Where(x => x.perms.Contains("death_vehicle")).ToList().ForEach(ch =>
                    {
                        GetChannel(_client, ch.Channelid, chan =>
                        {
                            chan.CreateMessage(_client, ":skull_crossbones: " + message);
                        });
                    });
                }
                else if ((victimType == 5 && (killerType == 3 || killerType == 4)) || ((victimType == 3 || victimType == 4) && (killerType == 5)))
                {
                    message = (string)_DeathNotes.Call("StripRichText", message);
                    _settings.Channels.Where(x => x.perms.Contains("death_npc")).ToList().ForEach(ch =>
                    {
                        GetChannel(_client, ch.Channelid, chan =>
                        {
                            chan.CreateMessage(_client, ":skull_crossbones: " + message);
                        });
                    });
                }
        }

        private void Discord_MessageCreate(Message message)
        {
            Settings.Channel channelidx = FindChannelById(message.channel_id);
            if (channelidx == null)
                return;

            if (message.author.id == _settings.Botid()) return;
            if (message.content[0] == _settings.Commandprefix[0])
            {
                if (!channelidx.perms.Contains("cmd_allow"))
                    return;
                string cmd;
                string msg;
                try
                {
                    cmd = message.content.Split(' ')[0].ToLower();
                    if (string.IsNullOrEmpty(cmd.Trim()))
                        cmd = message.content.Trim().ToLower();
                }
                catch
                {
                    cmd = message.content.Trim().ToLower();
                }

                cmd = cmd.Remove(0, 1);

                msg = message.content.Remove(0, 1 + cmd.Length).Trim();
                cmd = cmd.Trim();
                cmd = cmd.ToLower();

                if (!channelidx.perms.Contains("cmd_" + cmd))
                    return;
                if (!_settings.Commandroles.ContainsKey(cmd))
                {
                    DiscordToGameCmd(cmd, msg, message.author, message.channel_id);
                    return;
                }
                var roles = _settings.Commandroles[cmd];
                if (roles.Count == 0)
                {
                    DiscordToGameCmd(cmd, msg, message.author, message.channel_id);
                    return;
                }

                foreach (var roleid in message.member.roles)
                {
                    var rolename = GetRoleNameById(roleid);
                    if (roles.Contains(rolename))
                    {
                        DiscordToGameCmd(cmd, msg, message.author, message.channel_id);
                        break;
                    }
                }
            }
            else
            {
                if (!channelidx.perms.Contains("msg_chat")) return;
                string nickname = message.member?.nick ?? "";
                if (nickname.Length == 0)
                    nickname = message.author.username;
                PrintToChat("[DISCORD] " + nickname + ": " + message.content);
                Puts("[DISCORD] " + nickname + ": " + message.content);
            }
        }

        private void DiscordToGameCmd(string command, string param, User author, string channelid)
        {
            switch (command)
            {
                case "players":
                    {
                        string listStr = string.Empty;
                        var pList = BasePlayer.activePlayerList;
                        int i = 0;
                        foreach (var player in pList)
                        {
                            listStr += player.displayName + "[" + i++ + "]";
                            if (i != pList.Count)
                                listStr += ", ";
                        }
                        GetChannel(_client, channelid, chan =>
                        {
                            // Connected Players [{count}/{maxplayers}]: {playerslist}
                            chan.CreateMessage(_client, Translate("Discord_PlayersResponse", new Dictionary<string, string>
                        {
                            { "count", Convert.ToString(BasePlayer.activePlayerList.Count) },
                            { "maxplayers", Convert.ToString(ConVar.Server.maxplayers) },
                            { "playerslist", listStr }
                        }));
                        });
                        break;
                    }
                case "kick":
                    {
                        if (String.IsNullOrEmpty(param))
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !kick <steam id> <reason>");
                            });
                            return;
                        }
                        string[] _param = param.Split(' ');
                        if (_param.Count() < 2)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !kick <steam id> <reason>");
                            });
                            return;
                        }
                        BasePlayer plr = BasePlayer.Find(_param[0]);
                        if (plr == null)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Error: player not found");
                            });
                            return;
                        }
                        plr.Kick(param.Remove(0, _param[0].Length + 1));
                        break;
                    }
                case "ban":
                    {
                        if (string.IsNullOrEmpty(param))
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !ban <name/id> <reason>");
                            });
                            return;
                        }
                        string[] _param = param.Split(' ');
                        if (_param.Count() < 2)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !ban <name/id> <reason>");
                            });
                            return;
                        }
                        var plr = covalence.Players.FindPlayer(_param[0]);
                        if (plr == null)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Error: player not found");
                            });
                            return;
                        }
                        plr.Ban(param.Remove(0, _param[0].Length + 1));
                        break;
                    }
                case "unban":
                    {
                        if (string.IsNullOrEmpty(param))
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !unban <name/id>");
                            });
                            return;
                        }
                        string[] _param = param.Split(' ');
                        var plr = covalence.Players.FindPlayer(_param[0]);
                        if (plr == null)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Error: player not found");
                            });
                            return;
                        }
                        plr.Unban();
                        break;
                    }
                case "com":
                    {
                        if (String.IsNullOrEmpty(param))
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !com <command>");
                            });
                            return;
                        }
                        string[] _param = param.Split(' ');
                        if (_param.Count() > 1)
                        {
                            string[] args = new string[_param.Length - 1];
                            Array.Copy(_param, 1, args, 0, args.Length);
                            this.Server.Command(_param[0], args);
                        }
                        else
                        {
                            this.Server.Command(param);
                        }
                        break;
                    }
                case "mute":
                    {
                        if (BetterChatMute == null)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "This command requires the Better Chat Mute plugin.");
                                return;
                            });
                        }
                        if (string.IsNullOrEmpty(param))
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !mute <playername/steamid> <time (optional)> <reason (optional)>");
                            });
                            return;
                        }
                        string[] _param = param.Split(' ');
                        if (_param.Length >= 1)
                        {
                            this.Server.Command($"mute {string.Join(" ", _param)}");
                            return;
                        }
                        break;
                    }
                case "unmute":
                    {
                        if (BetterChatMute == null)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "This command requires the Better Chat Mute plugin.");
                                return;
                            });
                        }
                        if (String.IsNullOrEmpty(param))
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !unmute <playername/steamid>");
                            });
                            return;
                        }
                        string[] _param = param.Split(' ');
                        if (_param.Length == 1)
                        {
                            this.Server.Command($"unmute {string.Join(" ", _param)}");
                            return;
                        }
                        if (_param.Length > 1)
                        {
                            GetChannel(_client, channelid, chan =>
                            {
                                chan.CreateMessage(_client, "Syntax: !unmute <playername/steamid>");
                            });
                            return;
                        }
                        break;
                    }
            }

        }

        //GAME COMMANDS

        [ChatCommand("report")] // /report [message]
        void cmdReport(BasePlayer player, string command, string[] args)
        {
            if (args.Length < 1)
            {
                SendReply(player, "Syntax: /report [message]");
                return;
            }

            string message = "";
            foreach (string s in args)
                message += (s + " ");

            _settings.Channels.Where(x => x.perms.Contains("game_report")).ToList().ForEach(ch => {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnPlayerReport", new Dictionary<string, string>
                    {
                        { "playername", player.displayName },
                        { "message", message }
                    }));
                });
            });
            SendReply(player, "Your report has been submitted to Discord.");

        }

        [ChatCommand("bug")] // /bug [message]
        void cmdBug(BasePlayer player, string command, string[] args)
        {
            if (args.Length < 1)
            {
                SendReply(player, "Syntax: /bug [message]");
                return;
            }

            string message = "";
            foreach (string s in args)
                message += (s + " ");

            _settings.Channels.Where(x => x.perms.Contains("game_bug")).ToList().ForEach(ch => {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnPlayerBug", new Dictionary<string, string>
                    {
                        { "playername", player.displayName },
                        { "message", message }
                    }));
                });
            });
            SendReply(player, "Your bug report has been submitted to Discord.");

        }
        //NPC VEHICLE SPAWN LOGGING

        private void OnEntitySpawned(BaseEntity Entity)
        {
            if (Entity == null) return;
            if (Entity is BaseHelicopter)
            {
                _settings.Channels.Where(x => x.perms.Contains("msg_helispawn")).ToList().ForEach(ch =>
                {
                    GetChannel(_client, ch.Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnHeliSpawn"));
                    });
                });
            }
            if (Entity is CargoPlane)
            {
                _settings?.Channels.Where(x => x.perms.Contains("msg_planespawn")).ToList().ForEach(ch => {
                    GetChannel(_client, ch.Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnPlaneSpawn"));
                    });
                });
            }
            if (Entity is CargoShip)
            {
                _settings.Channels.Where(x => x.perms.Contains("msg_shipspawn")).ToList().ForEach(ch =>
                {
                    GetChannel(_client, ch.Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnShipSpawn"));
                    });
                });
            }
            if (Entity is CH47Helicopter)
            {
                _settings.Channels.Where(x => x.perms.Contains("msg_chinookspawn")).ToList().ForEach(ch =>
                {
                    GetChannel(_client, ch.Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnChinookSpawn"));
                    });
                });
            }
            if (Entity is BradleyAPC)
            {
                _settings.Channels.Where(x => x.perms.Contains("msg_bradleyspawn")).ToList().ForEach(ch =>
                {
                    GetChannel(_client, ch.Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnBradleySpawn"));
                    });
                });
            }


        }

        private string Translate(string msg, Dictionary<string, string> parameters = null)
        {
            if (string.IsNullOrEmpty(msg))
                return string.Empty;

            msg = lang.GetMessage(msg, this);

            if (parameters != null)
            {
                foreach (var lekey in parameters)
                {
                    if (msg.Contains("{" + lekey.Key + "}"))
                        msg = msg.Replace("{" + lekey.Key + "}", lekey.Value);
                }
            }

            return msg;
        }

        private Settings.Channel FindChannelById(string id)
        {
            foreach (var ch in _settings.Channels)
            {
                if (ch.Channelid == id)
                    return ch;
            }
            return null;
        }

        private void GetChannel(DiscordClient c, string chan_id, Action<Channel> cb)
        {
            Channel foundchan = c.DiscordServer.channels.FirstOrDefault(x => x.id == chan_id);
            if(foundchan == null) return;
            if(foundchan.id != chan_id) return;
            cb?.Invoke(foundchan);
        }

        private string GetRoleNameById(string id)
        {
            foreach (var r in _client.DiscordServer.roles)
            {
                if (r.id == id)
                    return r.name;
            }
            return "";
        }

        private IPlayer FindPlayer(string nameorId)
        {
            foreach (var player in covalence.Players.Connected)
            {
                if (player.Id == nameorId)
                    return player;

                if (player.Name == nameorId)
                    return player;
            }

            return null;
        }

        private User FindUserByID(string Id)
        {
            foreach (var member in _client.DiscordServer.members)
            {
                if (member.user.id == Id)
                    return member.user;
            }

            return null;
        }

        private BasePlayer FindPlayerByID(string Id)
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player.UserIDString == Id)
                    return player;
            }

            return null;
        }

        //CONSOLE LOGGING 

        void OnGroupCreated(string name)
        {
            _settings.Channels.Where(x => x.perms.Contains("log_groups")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnGroupCreated", new Dictionary<string, string>
                    {
                        { "groupname", name }
                    }));
                });
            });
        }

        void OnGroupDeleted(string name)
        {
            _settings.Channels.Where(x => x.perms.Contains("log_groups")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnGroupDeleted", new Dictionary<string, string>
                    {
                        { "groupname", name }
                    }));
                });
            });
        }



        void OnUserGroupAdded(string id, string groupName)
        {
            if (_settings.LogExcludeGroups.Contains(groupName))
            {
                return;
            }
            var player = covalence.Players.FindPlayerById(id);
            if (player == null) return;
            _settings.Channels.Where(x => x.perms.Contains("log_groups")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnUserGroupAdded", new Dictionary<string, string>
                    {
                        { "playername", player.Name },
                        { "steamid", id },
                        { "group", groupName }
                    }));
                });
            });
        }

        void OnUserGroupRemoved(string id, string groupName)
        {
            if (_settings.LogExcludeGroups.Contains(groupName)) return;
            var player = covalence.Players.FindPlayerById(id);
            if (player == null) return;
            _settings.Channels.Where(x => x.perms.Contains("log_groups")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnUserGroupRemoved", new Dictionary<string, string>
                    {
                        { "playername", player.Name },
                        { "steamid", id },
                        { "group", groupName }
                    }));
                });
            });
        }

        void OnUserPermissionGranted(string id, string permName)
        {
            if (_settings.LogExcludePerms.Contains(permName)) return;
            var player = covalence.Players.FindPlayerById(id);
            if (player == null) return;
            _settings.Channels.Where(x => x.perms.Contains("log_perms")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnUserPermissionGranted", new Dictionary<string, string>
                    {
                        { "playername", player.Name },
                        { "steamid", id },
                        { "permission", permName }
                    }));
                });
            });
        }

        void OnGroupPermissionGranted(string name, string perm)
        {
            if (_settings.LogExcludePerms.Contains(perm)) return;
            _settings.Channels.Where(x => x.perms.Contains("log_perms")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnGroupPermissionGranted", new Dictionary<string, string>
                    {
                        { "group",name },
                        { "permission", perm }
                    }));
                });
            });
        }

        void OnUserPermissionRevoked(string id, string permName)
        {
            if (_settings.LogExcludePerms.Contains(permName)) return;
            var player = covalence.Players.FindPlayerById(id);
            if (player == null) return;
            _settings.Channels.Where(x => x.perms.Contains("log_perms")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnUserPermissionRevoked", new Dictionary<string, string>
                    {
                        { "playername", player.Name },
                        { "steamid", id },
                        { "permission", permName }
                    }));
                });
            });
        }

        void OnGroupPermissionRevoked(string name, string perm)
        {
            if (_settings.LogExcludePerms.Contains(perm)) return;
            _settings.Channels.Where(x => x.perms.Contains("log_perms")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnGroupPermissionRevoked", new Dictionary<string, string>
                    {
                        { "group", name },
                        { "permission", perm }
                    }));
                });
            });
        }

        void OnUserKicked(IPlayer player, string reason)
        {
            _settings.Channels.Where(x => x.perms.Contains("log_kicks")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnPlayerKicked", new Dictionary<string, string>
                    {
                        { "playername", player.Name },
                        { "reason", reason }
                    }));
                });
            });
        }

        void OnUserBanned(string name, string bannedId, string address, string reason)
        {
            _settings.Channels.Where(x => x.perms.Contains("log_bans")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnPlayerBanned", new Dictionary<string, string>
                    {
                        { "playername", name },
                        { "steamid", bannedId },
                        { "ip", address },
                        { "reason", reason }
                    }));
                });
            });
        }

        private void OnUserUnbanned(string name, string id, string ip)
        {
            _settings.Channels.Where(x => x.perms.Contains("log_bans")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnPlayerUnBanned", new Dictionary<string, string>
                    {
                        { "playername", name },
                        { "steamid", id },
                        { "ip", ip }
                    }));
                });
            });
        }

        void OnUserNameUpdated(string id, string oldName, string newName) //TESTING FUNCTION
        {
            if ((oldName == newName) || (oldName == "Unnamed")) return;
            _settings.Channels.Where(x => x.perms.Contains("log_namechange")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnPlayerNameChange", new Dictionary<string, string>
                    {
                        { "oldname", oldName },
                        { "newname", newName },
                        { "steamid", id }
                    }));
                });
            });
        }

        private object OnServerMessage(string message, string name)
        {
            if (message.Contains("gave") && name == "SERVER")
            {
                _settings.Channels.Where(x => x.perms.Contains("log_admingive")).ToList().ForEach(ch =>
                {
                    GetChannel(_client, ch.Channelid, chan =>
                    {
                        chan.CreateMessage(_client, Translate("RUST_OnF1ItemSpawn", new Dictionary<string, string>
                        {
                            { "name", name },
                            { "givemessage", message }
                        }));
                    });
                });
            }

            return null;
        }

        private void OnServerCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            var emote = arg.GetString(0);

            if (arg.cmd.Name == "gesture")
            {
                if (_emotes.ContainsKey(emote))
                {
                    var emoji = _emotes[emote];
                    _settings.Channels.Where(x => x.perms.Contains("msg_gestures")).ToList().ForEach(ch =>
                    {
                        GetChannel(_client, ch.Channelid, chan =>
                        {
                            chan.CreateMessage(_client, Translate("RUST_OnPlayerGesture", new Dictionary<string, string>
                        {
                            {"playername", player.displayName},
                            {"gesture", emoji}
                        }));
                        });
                    });
                }
            }
        }
        private readonly Dictionary<string, string> _emotes = new Dictionary<string, string>
        {
            ["wave"] = ":wave:",
            ["shrug"] = ":shrug:",
            ["victory"] = ":trophy:",
            ["thumbsup"] = ":thumbsup:",
            ["chicken"] = ":chicken:",
            ["hurry"] = ":runner:",
            ["whoa"] = ":flag_white:"
        };

        //           ======================================================================
        //           ||                      EXTERNAL PLUGINS SUPPORT                    ||
        //           ======================================================================

        //Better Chat Mute

        private void OnBetterChatMuted(IPlayer target, IPlayer player, string reason)
        {
            _settings.Channels.Where(x => x.perms.Contains("msg_mute")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnPlayerMute", new Dictionary<string, string>
                    {
                        { "target", target.Name },
                        { "reason", reason },
                        { "muter", player.Name }
                    }));
                });
            });
        }

        private void OnBetterChatTimeMuted(IPlayer target, IPlayer player, TimeSpan time, string reason)
        {
            _settings.Channels.Where(x => x.perms.Contains("msg_mute")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnPlayerTimedMute", new Dictionary<string, string>
                    {
                        { "target", target.Name },
                        { "reason", reason },
                        { "muter", player.Name },
                        { "time", FormatTime((TimeSpan) time) }
                    }));
                });
            });
        }

        private void OnBetterChatUnmuted(IPlayer target, IPlayer player)
        {
            _settings.Channels.Where(x => x.perms.Contains("msg_mute")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnPlayerUnMute", new Dictionary<string, string>
                    {
                        { "target", target.Name },
                        { "unmuter", player.Name }
                    }));
                });
            });
        }

        private void OnBetterChatMuteExpired(IPlayer target)
        {
            _settings.Channels.Where(x => x.perms.Contains("msg_mute")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnPlayerMuteExpire", new Dictionary<string, string>
                    {
                        { "target", target.Name }
                    }));
                });
            });
        }

        private static string FormatTime(TimeSpan time)
        {
            var values = new List<string>();

            if (time.Days != 0)
                values.Add($"{time.Days} day(s)");

            if (time.Hours != 0)
                values.Add($"{time.Hours} hour(s)");

            if (time.Minutes != 0)
                values.Add($"{time.Minutes} minute(s)");

            if (time.Seconds != 0)
                values.Add($"{time.Seconds} second(s)");

            return values.ToSentence();
        }

        //Clans
        void OnClanCreate(string tag, string ownerID)
        {
            _settings.Channels.Where(x => x.perms.Contains("log_clans")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnClanCreate", new Dictionary<string, string>
                    {
                        { "clantag", tag }
                    }));
                });
            });
        }

        void OnClanChat(IPlayer player, string message) => ClanChatProcess(player.Name, message);

        // Clans Reborn
        void OnClanChat(BasePlayer player, string message, string tag) => ClanChatProcess(player.displayName, message);

        void ClanChatProcess(string playerName, string message)
        {
            _settings.Channels.Where(x => x.perms.Contains("log_clanchat")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnClanChat", new Dictionary<string, string>
                            {
                                { "playername", playerName },
                                { "message", message }
                            }));
                });
            });
        }

        //ClansReborn

        //PrivateMessages
        [HookMethod("OnPMProcessed")]
        void OnPMProcessed(IPlayer sender, IPlayer target, string message)
        {
            _settings.Channels.Where(x => x.perms.Contains("msg_pm")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate("RUST_OnPlayerPM", new Dictionary<string, string>
                    {
                        { "sender", sender.Name },
                        { "target", target.Name },
                        { "message", message }
                    }));
                });
            });
        }

        //SignArtist
        private void OnImagePost(BasePlayer player, string image)
        {
            _settings.Channels.Where(x => x.perms.Contains("plugin_signartist")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, SignArtistEmbed(Translate("PLUGIN_SignArtist", new Dictionary<string, string>
                    {
                        { "player", player.displayName }
                    }), image));
                });
            });
        }

        private Embed SignArtistEmbed(string text, string image)
        {
            Embed embed = new Embed
            {
                title = text,
                color = 52326,
                image = new Embed.Image
                {
                    url = image
                }
            };

            return embed;
        }
        //DiscordAuth
        private void OnAuthenticate(string steamId, string discordId) => ProcessDiscordAuth("PLUGIN_DiscordAuth_Auth", steamId, discordId);

        private void OnDeauthenticate(string steamId, string discordId) => ProcessDiscordAuth("PLUGIN_DiscordAuth_Deauth", steamId, discordId);

        private void ProcessDiscordAuth(string key, string steamId, string discordId)
        {
            var player = covalence.Players.FindPlayerById(steamId);
            var user = FindUserByID(discordId);

            if (player == null || user == null)
                return;

            _settings.Channels.Where(x => x.perms.Contains("plugin_discordauth")).ToList().ForEach(ch =>
            {
                GetChannel(_client, ch.Channelid, chan =>
                {
                    chan.CreateMessage(_client, Translate(key, new Dictionary<string, string>
                    {
                        { "gamename", player.Name },
                        { "discordname", user.username + "#" + user.discriminator }
                    }));
                });
            });
        }
    }
}