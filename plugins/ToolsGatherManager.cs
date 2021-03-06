﻿using Newtonsoft.Json;
using System.Collections.Generic;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Tools Gather Manager", "Wasdik", "1.0.6")]
    [Description("Adjusts the gather rate from certain tools")]
    public class ToolsGatherManager : RustPlugin
    {
		private void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
		{
			var player = entity.ToPlayer();
            if (!player || !HasPerm(player))
			{
                return;
			}
            var activeItem = player.GetActiveItem();
            if (activeItem != null)
            {
                var toolName = activeItem.info.shortname;
                if (config.tools.ContainsKey(toolName))
                {
                    item.amount = (int)(item.amount * config.tools[toolName]);

                    if (TOD_Sky.Instance.IsNight)
                    {
                        item.amount = (int)(item.amount * config.nightrate);
                    }
                    else if (TOD_Sky.Instance.IsDay)
                    {
                        item.amount = (int)(item.amount * config.dayrate);
                    }
					return;
                }
            }
		}

        private void OnDispenserBonus(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            OnDispenserGather(dispenser, player, item);
        }
		
		private void OnCollectiblePickup(Item item, BasePlayer player)
		{
            if (IsInBlackList(item)) return;
			if (TOD_Sky.Instance.IsNight)
            {
                item.amount = (int)(item.amount * config.nightrate);
            }
            else if (TOD_Sky.Instance.IsDay)
            {
                item.amount = (int)(item.amount * config.dayrate);
            }
		    return;
		}
		
		private void OnCropGather(PlantEntity plant, Item item, BasePlayer player)
		{
			if (TOD_Sky.Instance.IsNight)
            {
                item.amount = (int)(item.amount * config.nightrate);
            }
            else if (TOD_Sky.Instance.IsDay)
            {
                item.amount = (int)(item.amount * config.dayrate);
            }
		    return;
		}

        #region config

        private const string permName = "toolsgathermanager.allow";

        private void RegisterToolPermissions()
        {
            foreach (var def in ItemManager.GetItemDefinitions())
            {
                if (def.category == ItemCategory.Tool)
                {
                    permission.RegisterPermission(this.Name.ToLower() + "." + def.shortname, this);
                }
            }
        }

        private bool HasPerm(BasePlayer player)
        {
			IPlayer iplayer = BasePlayerToIPlayer(player);
            if (config.usetoolpermissions)
			{
                return HasToolPermission(player);
			}
            return (iplayer.HasPermission(permName));
        }

        private bool HasToolPermission(BasePlayer player)
        {
            var activeItem = player.GetActiveItem();
            if (activeItem?.info == null)
                return false;

            return permission.UserHasPermission(player.UserIDString, this.Name.ToLower() + "." + activeItem.info.shortname);
        }

		private IPlayer BasePlayerToIPlayer(BasePlayer player)
        {
            return covalence.Players.FindPlayerById(player.UserIDString);
        }
		
		private bool IsInBlackList(Item item)
		{
			if (item == null) return false;
			return (config.blackListItems.Contains(item.info.shortname));
		}
		
        private void Init()
        {
            permission.RegisterPermission(permName, this);
            RegisterToolPermissions();
        }

        private static Configuration config;

        public class Configuration
        {
            [JsonProperty(PropertyName = "Permissions for every Tool")]
            public bool usetoolpermissions = false;

            [JsonProperty(PropertyName = "Night Gatherrate")]
            public float nightrate = 1;

            [JsonProperty(PropertyName = "Day Gatherrate")]
            public float dayrate = 1;

            [JsonProperty(PropertyName = "Tools")]
            public Dictionary<string, float> tools;
			
			[JsonProperty(PropertyName = "BlackListItems")]
            public List<string> blackListItems;

            public static Configuration DefaultConfig()
            {
                return new Configuration
                {
                    tools = new Dictionary<string, float>()
                    {
                        ["bone.club"] = 1,
                        ["knife.bone"] = 1,
                        ["longsword"] = 1,
                        ["mace"] = 1,
                        ["machete"] = 1,
                        ["salvaged.cleaver"] = 1,
                        ["salvaged.sword"] = 1,
                        ["hatchet"] = 1,
                        ["pickaxe"] = 1,
                        ["rock"] = 1,
                        ["axe.salvaged"] = 1,
                        ["hammer.salvaged"] = 1,
                        ["icepick.salvaged"] = 1,
                        ["stonehatchet"] = 1,
                        ["stone.pickaxe"] = 1,
                    },
					blackListItems = new List<string>()
					{
						"mushroom",
					}
                };
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                PrintWarning("Creating new config file.");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig() => config = Configuration.DefaultConfig();

        protected override void SaveConfig() => Config.WriteObject(config);

        #endregion config
    }
}
