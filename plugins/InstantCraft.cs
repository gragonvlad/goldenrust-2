using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Rust;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Instant Craft", "Orange", "2.1.0")]
    [Description("Allows players to instantly craft items with features")]
    public class InstantCraft : RustPlugin
    {
        #region Vars

        private const string permUse = "instantcraft.use";
        private const string permRandom = "instantcraft.random";

        #endregion
        
        #region Oxide Hooks

        private void Init()
        {
            permission.RegisterPermission(permUse, this);
            permission.RegisterPermission(permRandom, this);
        }
        
        private object OnItemCraft(ItemCraftTask item)
        {
            return OnCraft(item);
        }

        #endregion

        #region Core

        private object OnCraft(ItemCraftTask task)
        {
            var player = task.owner;
            var target = task.blueprint.targetItem;
            var name = target.shortname;
            
            if (permission.UserHasPermission(player.UserIDString, permUse) == false)
            {
                return null;
            }
            if (IsBlocked(name))
            {
                task.cancelled = true;
                Message(player, "Blocked");
                GiveRefund(player, task.takenItems);
                return null;
            }
			
            var stacks = GetStacks(target, task.amount * task.blueprint.amountToCreate);
            var slots = FreeSlots(player);

            if (HasPlace(slots, stacks) == false)
            {
                task.cancelled = true;
                Message(player, "Slots", stacks.Count, slots);
                GiveRefund(player, task.takenItems);
                return null;
            }
          
            if (IsNormalItem(name))
            {
                Message(player, "Normal");
                return null;
            }

            GiveItem(player, target, stacks, task.skinID);
            task.cancelled = true;
            return null;
        }

        private void GiveItem(BasePlayer player, ItemDefinition item, List<int> stacks, int craftSkin)
        {

            var skin = ItemDefinition.FindSkin(item.itemid, craftSkin);

            if (skin == 0 && permission.UserHasPermission(player.UserIDString, permRandom))
            {
                skin = GetRandomSkin(item);
            }

            if (!config.split)
            {
                var final = 0;

                foreach (var i in stacks)
                {
                    final += i;
                }
                //var x = ItemManager.Create(item, final, skin);
				var x = ItemManager.CreateByName(item.shortname, final, skin);
                player.GiveItem(x);
                return;
            }
            foreach (var stack in stacks)
            {
                var x = ItemManager.Create(item, stack, skin);
                player.GiveItem(x);
            }
        }

        private int FreeSlots(BasePlayer player)
        {
            var slots = player.inventory.containerMain.capacity + player.inventory.containerBelt.capacity;
            var taken = player.inventory.containerMain.itemList.Count + player.inventory.containerBelt.itemList.Count;
            return slots - taken;
        }

        private void GiveRefund(BasePlayer player, List<Item> items)
        {
            foreach (var item in items)
            {
                player.GiveItem(item);
            }
        }

        private List<int> GetStacks(ItemDefinition item, int amount) 
        {
            var list = new List<int>();
            var maxStack = item.stackable;

            while (amount > maxStack)
            {
                amount -= maxStack;
                list.Add(maxStack);
            }
            
            list.Add(amount);
            
            return list; 
        }

        private bool IsNormalItem(string name)
        {
            return config.normal.Contains(name);
        }

        private bool IsBlocked(string name)
        {
            return config.blocked.Contains(name);
        }

        private bool HasPlace(int slots, List<int> stacks)
        {
            if (!config.checkPlace)
            {
                return true;
            }

            if (config.split && slots - stacks.Count < 0)
            {
                return false;
            }

            return slots > 0;
        }

        private ulong GetRandomSkin(ItemDefinition def)
        {
            if (def.skins.Length == 0 && def.skins2.Length == 0) {return 0;}
            var skins = new List<int> {0};
            if (def.skins != null) skins.AddRange(def.skins.Select(skin => skin.id));
            if (def.skins2 != null) skins.AddRange(def.skins2.Select(skin => skin.Id));
            var value = ItemDefinition.FindSkin(def.itemid, skins.GetRandom());
            var final = Convert.ToUInt64(value);
            return final;
        }

        #endregion

        #region Localization 1.1.1
        
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"Blocked", "Crafting of that item is blocked!"},
                {"Slots", "You don't have enough place to craft! Need {0}, have {1}!"},
                {"Normal", "Item will be crafted with normal speed."}
            }, this);
        }

        private void Message(string messageKey, params object[] args)
        {
            var message = GetMessage(messageKey, null, args);
            Puts(message);
        }
        
        private void Message(ConsoleSystem.Arg arg, string messageKey, params object[] args)
        {
            var message = GetMessage(messageKey, null, args);
            var player = arg.Player();
            if (player != null)
            {
                player.SendConsoleCommand("chat.add", (object) 0, (object) message);
            }
            else
            {
                Puts(message);
            }
        }

        private void Message(IPlayer player, string messageKey, params object[] args)
        {
            if (player == null)
            {
                return;
            }

            var message = GetMessage(messageKey, player.Id, args);
            player.Message(message);
        }

        private void Message(BasePlayer player, string messageKey, params object[] args)
        {
            if (player == null)
            {
                return;
            }

            var message = GetMessage(messageKey, player.UserIDString, args);
            player.SendConsoleCommand("chat.add", (object) 0, (object) message);
        }

        private void Broadcast(string messageKey, params object[] args)
        {
            var message = GetMessage(messageKey, null, args);
            
            foreach (var player in BasePlayer.activePlayerList)
            {
                player.SendConsoleCommand("chat.add", (object) 0, (object) message);
            }
            
            Puts(message);
        }

        private string GetMessage(string messageKey, string playerID, params object[] args)
        {
            return string.Format(lang.GetMessage(messageKey, this, playerID), args);
        }

        #endregion
        
        #region Configuration 1.1.0

        private static ConfigData config;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Check for free place")]
            public bool checkPlace;
            
            [JsonProperty(PropertyName = "Normal Speed")]
            public List<string> normal;

            [JsonProperty(PropertyName = "Blacklist")]
            public List<string> blocked;
            
            [JsonProperty(PropertyName = "Split crafted stacks")]
            public bool split;
        }

        private ConfigData GetDefaultConfig()
        {
            return new ConfigData
            {
                normal = new List<string>
                {
                    "hammer"
                },
                blocked = new List<string>
                {
                    "rock"
                },
                checkPlace = false,
                split = false
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<ConfigData>();

                if (config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                PrintError("Configuration file is corrupt! Unloading plugin...");
                Interface.Oxide.RootPluginManager.RemovePlugin(this);
                return;
            }

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        #endregion
    }
}