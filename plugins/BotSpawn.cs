using Facepunch;
using System;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

//Added named skull to corpse harvesting. 
namespace Oxide.Plugins
{
    [Info("BotSpawn", "Steenamaroo+Wasdik", "1.9.0", ResourceId = 2580)]

    [Description("Spawn tailored AI with kits at monuments, custom locations, or randomly.")]

    class BotSpawn : RustPlugin
    {
        [PluginReference]
        Plugin Vanish, Kits;

        int no_of_AI;
        static BotSpawn botSpawn;
        System.Single currentTime;
        const string permAllowed = "botspawn.allowed";
        static System.Random random = new System.Random();
        public List<ulong> coolDownPlayers = new List<ulong>();
        public Dictionary<ulong, Timer> weaponCheck = new Dictionary<ulong, Timer>();
        public Dictionary<string, List<Vector3>> spawnLists = new Dictionary<string, List<Vector3>>();
        bool HasPermission(string id, string perm) => permission.UserHasPermission(id, perm);

        public Timer aridTimer, temperateTimer, tundraTimer, arcticTimer;

        bool IsBiome(string name) => name == "BiomeArid" || name == "BiomeTemperate" || name == "BiomeTundra" || name == "BiomeArctic";

        #region Data  
        class StoredData
        {
            public Dictionary<string, DataProfile> DataProfiles = new Dictionary<string, DataProfile>();
            public Dictionary<string, ProfileRelocation> MigrationDataDoNotEdit = new Dictionary<string, ProfileRelocation>();
        }

        StoredData storedData;
        #endregion
        void OnServerInitialized()
        {
            botSpawn = this;
            FindMonuments();
        }

        void Init()
        {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            };
            var filter = RustExtension.Filter.ToList();//Thanks Fuji. :)
            filter.Add("cover points");
            filter.Add("resulted in a conflict");
            RustExtension.Filter = filter.ToArray();
            no_of_AI = 0;
            LoadConfigVariables();
        }

        void Loaded()
        {
            ConVar.AI.npc_families_no_hurt = false;
            spawnLists.Add("BiomeArid", new List<Vector3>());
            spawnLists.Add("BiomeTemperate", new List<Vector3>());
            spawnLists.Add("BiomeTundra", new List<Vector3>());
            spawnLists.Add("BiomeArctic", new List<Vector3>());

            lang.RegisterMessages(Messages, this);
            permission.RegisterPermission(permAllowed, this);

            storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("BotSpawn");
            SaveData();
        }

        void Unload()
        {
            var filter = RustExtension.Filter.ToList();
            filter.Remove("cover points");
            filter.Remove("resulted in a conflict");
            RustExtension.Filter = filter.ToArray();
            Wipe();
        }

        bool IsAuth(BasePlayer player) => !(player.net.connection != null && player.net.connection.authLevel < 2);

        void UpdateRecords(NPCPlayerApex player)
        {
            if (NPCPlayers.ContainsKey(player.userID))
                NPCPlayers.Remove(player.userID);

            if (weaponCheck.ContainsKey(player.userID))
            {
                weaponCheck[player.userID].Destroy();
                weaponCheck.Remove(player.userID);
            }
        }

        void Wipe()
        {
            foreach (var bot in NPCPlayers.Where(bot => bot.Value != null))
                bot.Value.Kill();
        }

        public static string Get(ulong v) => Facepunch.RandomUsernames.Get((int)(v % 2147483647uL));

        #region BiomeSpawnsSetup
        void GenerateSpawnPoints(string name, int number, Timer myTimer, int biomeNo)
        {
            int getBiomeAttempts = 0;
            var spawnlist = spawnLists[name];
            myTimer = timer.Repeat(0.1f, 0, () =>
            {
                int halfish = Convert.ToInt16((ConVar.Server.worldsize / 2) / 1.1f);
                int x = random.Next(-halfish, halfish);
                int z = random.Next(-halfish, halfish);
                Vector3 randomSpot = new Vector3(x, 0, z);
                bool finished = true;

                if (spawnlist.Count < number + 10)
                {
                    getBiomeAttempts++;
                    if (getBiomeAttempts > 200 && spawnlist.Count == 0)
                    {
                        PrintWarning(lang.GetMessage("noSpawn", this), name);
                        myTimer.Destroy();
                        return;
                    }

                    finished = false;
                    x = random.Next(-halfish, halfish);
                    z = random.Next(-halfish, halfish);
                    if (TerrainMeta.BiomeMap.GetBiome(randomSpot, biomeNo) > 0.5f)
                    {
                        var point = CalculateGroundPos(new Vector3(randomSpot.x, 200, randomSpot.z));
                        if (point != Vector3.zero)
                            spawnlist.Add(CalculateGroundPos(new Vector3(randomSpot.x, 200, randomSpot.z)));
                    }
                }
                if (finished)
                {
                    int i = 0;
                    timer.Repeat(2, number, () =>
                    {
                        SpawnBots(name, AllProfiles[name], "biome", null, spawnlist[i]);
                        i++;
                    });
                    myTimer.Destroy();
                }
            });
        }

        public static Vector3 CalculateGroundPos(Vector3 pos)
        {
            pos.y = TerrainMeta.HeightMap.GetHeight(pos);
            NavMeshHit navMeshHit;

            if (!NavMesh.SamplePosition(pos, out navMeshHit, 2, 1))
                pos = Vector3.zero;
            else if (Physics.RaycastAll(navMeshHit.position + new Vector3(0, 100, 0), Vector3.down, 99f, 1235288065).Any())
                pos = Vector3.zero;
            else
                pos = navMeshHit.position;
            return pos;

        }

        Vector3 TryGetSpawn(Vector3 pos, int radius)
        {
            int attempts = 0;
            var spawnPoint = Vector3.zero;
            Vector2 rand;
            Vector3 attempt;

            while (attempts < 200 && spawnPoint == Vector3.zero)
            {
                attempts++;
                rand = UnityEngine.Random.insideUnitCircle * radius;
                pos += new Vector3(rand.x, 0, rand.y);
                attempt = CalculateGroundPos(pos);
                if (attempt != Vector3.zero)
                    spawnPoint = attempt;
            }
            return spawnPoint;
        }
        #endregion

        #region BotSetup
        void AttackPlayer(Vector3 location, string name, DataProfile profile, string group)
        {
            timer.Repeat(1f, profile.Bots, () => SpawnBots(name, profile, "Attack", group, location));
        }

        void SpawnBots(string name, DataProfile zone, string type, string group, Vector3 location)
        {
            var pos = new Vector3(zone.LocationX, zone.LocationY, zone.LocationZ);
            var finalPoint = Vector3.zero;
            if (location != Vector3.zero)
                pos = location;

            var randomTerrainPoint = TryGetSpawn(pos, zone.Radius);
            if (randomTerrainPoint == Vector3.zero)
                return;
            finalPoint = randomTerrainPoint + new Vector3(0, 0.5f, 0);

            if (zone.Chute)
                finalPoint = (type == "AirDrop") ? pos - new Vector3(0, 40, 0) : new Vector3(randomTerrainPoint.x, 200, randomTerrainPoint.z);

            NPCPlayer entity = (NPCPlayer)InstantiateSci(finalPoint, Quaternion.Euler(0, 0, 0), zone.Murderer);
            var npc = entity.GetComponent<NPCPlayerApex>();

            var bData = npc.gameObject.AddComponent<BotData>();
            bData.monumentName = name;
            npc.Spawn();

            if (!NPCPlayers.ContainsKey(npc.userID))
                NPCPlayers.Add(npc.userID, npc);
            else
            {
                npc.Kill();
                PrintWarning(lang.GetMessage("dupID", this));
                return;
            }
            npc.AiContext.Human.NextToolSwitchTime = Time.realtimeSinceStartup * 10;
            npc.AiContext.Human.NextWeaponSwitchTime = Time.realtimeSinceStartup * 10;
            npc.CommunicationRadius = 0;

            no_of_AI++;

            bData.group = group ?? null;

            bData.spawnPoint = randomTerrainPoint;
			// Wasdik Start
            //bData.accuracy = zone.Bot_Accuracy;
			bData.accuracy = random.Next(zone.Bot_Accuracy);
			// Wasdik End
            bData.damage = zone.Bot_Damage;
            bData.respawn = true;
            bData.roamRange = zone.Roam_Range;
            bData.dropweapon = zone.Weapon_Drop_Chance;
            bData.keepAttire = zone.Keep_Default_Loadout;
            bData.peaceKeeper = zone.Peace_Keeper;
            bData.chute = zone.Chute;
            bData.peaceKeeper_CoolDown = zone.Peace_Keeper_Cool_Down;
            bData.profile = zone;

            npc.startHealth = zone.BotHealth;
            npc.InitializeHealth(zone.BotHealth, zone.BotHealth);

            bData.biome = (type == "biome");
            if (zone.Chute)
                AddChute(npc, finalPoint);

            int kitRnd;
            kitRnd = random.Next(zone.Kit.Count);
			// Wasdik Start
			int botLevel = bData.accuracy;
			string clanTeg = "";
			switch (botLevel) 
			{
				case 0:
					clanTeg = "[Private]";
					break;
				case 1:
					clanTeg = "[Corporal]";
					break;
				case 2:
					clanTeg = "[Sergeant]";
					break;
				case 3:
					clanTeg = "[Master Sergeant]";
					break;
				case 4:
					clanTeg = "[Second Lieutenant]";
					break;
				case 5:
					clanTeg = "[First Lieutenant]";
					break;
				case 6:
					clanTeg = "[Captain]";
					break;
				case 7:
					clanTeg = "[Major]";
					break;
				case 8:
					clanTeg = "[Colonel]";
					break;
				case 9:
					clanTeg = "[General]";
					break;
				case 10:
					clanTeg = "[SEAL]";
					break;	
			}
			// Wasdik End
            if (zone.BotNames.Count == zone.Kit.Count && zone.Kit.Count != 0)
                SetName(zone, npc, kitRnd);
            else
                SetName(zone, npc, random.Next(zone.BotNames.Count), clanTeg); // Wasdik: , clanTeg

			Puts("---"+name+"--- "+npc.displayName);
			
            GiveKit(npc, zone, kitRnd);

            SortWeapons(npc);

            int suicInt = random.Next(zone.Suicide_Timer, zone.Suicide_Timer + 10);//slightly randomise suicide de-spawn time
            if (type == "AirDrop" || type == "Attack")
            {
                bData.respawn = false;
                RunSuicide(npc, suicInt);
            }

            if (zone.Disable_Radio)
                npc.RadioEffect = new GameObjectRef();

            npc.Stats.AggressionRange = zone.Sci_Aggro_Range;
            npc.Stats.DeaggroRange = zone.Sci_DeAggro_Range;
        }

        BaseEntity InstantiateSci(Vector3 position, Quaternion rotation, bool murd)//Spawn population spam fix - credit Fujikura 
        {
            string type = (murd) ? "murderer" : "scientist";
            string prefabname = $"assets/prefabs/npc/{type}/{type}.prefab";
			// Wasdik Start
			//string prefabname = $"assets/rust.ai/agents/zombie/zombie.prefab";
			//string prefabname = $"assets/prefabs/npc/murderer/murderer.prefab";
			//string prefabname = $"assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
			// Wasdik End

            var prefab = GameManager.server.FindPrefab(prefabname);
            GameObject gameObject = Instantiate.GameObject(prefab, position, rotation);
            gameObject.name = prefabname;
            SceneManager.MoveGameObjectToScene(gameObject, Rust.Server.EntityScene);
            if (gameObject.GetComponent<Spawnable>())
                UnityEngine.Object.Destroy(gameObject.GetComponent<Spawnable>());
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
            BaseEntity component = gameObject.GetComponent<BaseEntity>();
            return component;
        }

        void AddChute(NPCPlayerApex npc, Vector3 newPos)
        {
            float wind = random.Next(0, 25) / 10f;
            float fall = random.Next(40, 80) / 10f;
            var rb = npc.gameObject.GetComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = false;
            rb.drag = 0f;
            npc.gameObject.layer = 0;//prevent_build layer fix
            var fwd = npc.transform.forward;
            rb.velocity = new Vector3(fwd.x * wind, 0, fwd.z * wind) - new Vector3(0, fall, 0);

            var col = npc.gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(1, 1f, 1);  //feet above ground

            var Chute = GameManager.server.CreateEntity("assets/prefabs/misc/parachute/parachute.prefab", newPos, Quaternion.Euler(0, 0, 0));
            Chute.gameObject.Identity();
            Chute.SetParent(npc, "parachute");
            Chute.Spawn();
        }

        void SetName(DataProfile zone, NPCPlayerApex npc, int number, string teg="") // Wasdik: , string teg=""
        {
            if (zone.BotNames.Count == 0 || zone.BotNames.Count <= number || zone.BotNames[number] == String.Empty)
            {
                npc.displayName = Get(npc.userID);
                npc.displayName = char.ToUpper(npc.displayName[0]) + npc.displayName.Substring(1);
            }
            else
                npc.displayName = zone.BotNames[number];

            if (zone.BotNamePrefix != String.Empty)
                npc.displayName = zone.BotNamePrefix + " " + npc.displayName;
			
			// Wasdik Start
			if (teg != String.Empty)
                npc.displayName = teg + " " + npc.displayName;
			// Wasdik End
        }

        void GiveKit(NPCPlayerApex npc, DataProfile zone, int kitRnd)
        {
            var bData = npc.GetComponent<BotData>();
            string type = (zone.Murderer) ? "Murderer" : "Scientist";
            if (zone.Kit.Count != 0 && zone.Kit[kitRnd] != null)
            {
                object checkKit = (Kits.CallHook("GetKitInfo", zone.Kit[kitRnd], true));
                if (checkKit == null)
                {
                    PrintWarning($"Kit {zone.Kit[kitRnd]} does not exist - Spawning default {type}.");
                }
                else
                {
                    bool weaponInBelt = false;
                    JObject kitContents = checkKit as JObject;
                    if (kitContents != null)
                    {
                        JArray items = kitContents["items"] as JArray;
                        foreach (var weap in items)
                        {
                            JObject item = weap as JObject;
                            if (item["container"].ToString() == "belt")
                                weaponInBelt = true;//doesn't actually check for weapons - just any item.
                        }
                    }
                    if (!weaponInBelt)
                    {
                        PrintWarning($"Kit {zone.Kit[kitRnd]} has no items in belt - Spawning default {type}.");
                    }
                    else
                    {
                        if (bData.keepAttire == false)
                            npc.inventory.Strip();
                        Kits.Call($"GiveKit", npc, zone.Kit[kitRnd], true);
                        if (!(KitList.ContainsKey(npc.userID)))
                        {
                            KitList.Add(npc.userID, new KitData
                            {
                                Kit = zone.Kit[kitRnd],
                                Wipe_Belt = zone.Wipe_Belt,
                                Wipe_Clothing = zone.Wipe_Clothing,
                                Allow_Rust_Loot = zone.Allow_Rust_Loot,
                            });
                        }
                    }
                }
            }
            else
            {
                if (!KitList.ContainsKey(npc.userID))
                {
                    KitList.Add(npc.userID, new KitData
                    {
                        Kit = String.Empty,
                        Wipe_Belt = zone.Wipe_Belt,
                        Wipe_Clothing = zone.Wipe_Clothing,
                        Allow_Rust_Loot = zone.Allow_Rust_Loot,
                    });
                }
            }
        }

        void SortWeapons(NPCPlayerApex npc)
        {
            var bData = npc.GetComponent<BotData>();
            foreach (Item item in npc.inventory.containerBelt.itemList)//store organised weapons lists
            {
                var held = item.GetHeldEntity();
                if (held as HeldEntity != null)
                {
                    if (held.name.Contains("bow") || held.name.Contains("launcher"))
                        continue;
                    if (held as BaseMelee != null || held as TorchWeapon != null)
                        bData.MeleeWeapons.Add(item);
                    else
                    {
                        if (held as BaseProjectile != null)
                        {
                            bData.AllProjectiles.Add(item);
                            if (held.name.Contains("m92") || held.name.Contains("pistol") || held.name.Contains("python") || held.name.Contains("waterpipe"))
                                bData.CloseRangeWeapons.Add(item);
                            else if (held.name.Contains("bolt"))
                                bData.LongRangeWeapons.Add(item);
                            else
                                bData.MediumRangeWeapons.Add(item);
                        }
                    }
                }
            }
            weaponCheck.Add(npc.userID, timer.Repeat(2.99f, 0, () => SelectWeapon(npc)));
        }

        void RunSuicide(NPCPlayerApex npc, int suicInt)
        {
            if (!NPCPlayers.ContainsKey(npc.userID))
                return;
            timer.Once(suicInt, () =>
            {
                if (npc == null)
                    return;
                if (npc.AttackTarget != null && Vector3.Distance(npc.transform.position, npc.AttackTarget.transform.position) < 10 && npc.GetNavAgent.isOnNavMesh)
                {
                    var position = npc.AttackTarget.transform.position;
                    npc.svActiveItemID = 0;
                    npc.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                    npc.inventory.UpdatedVisibleHolsteredItems();
                    timer.Repeat(0.05f, 100, () =>
                    {
                        if (npc == null)
                            return;
                        npc.SetDestination(position);
                    });
                }
                timer.Once(4, () =>
                {
                    if (npc == null)
                        return;
                    Effect.server.Run("assets/prefabs/weapons/rocketlauncher/effects/rocket_explosion.prefab", npc.transform.position);
                    HitInfo nullHit = new HitInfo();
                    nullHit.damageTypes.Add(Rust.DamageType.Explosion, 10000);
                    npc.IsInvinsible = false;
                    npc.Die(nullHit);
                }
                );
            });
        }
        #endregion
        static BasePlayer FindPlayerByName(string name)
        {
            BasePlayer result = null;
            foreach (BasePlayer current in BasePlayer.activePlayerList)
            {
                if (current.displayName.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || current.UserIDString.Contains(name, CompareOptions.OrdinalIgnoreCase)
                    || current.displayName.Contains(name, CompareOptions.OrdinalIgnoreCase))
                    result = current;
            }
            return result;
        }
        #region Hooks  

        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var botMelee = info?.Initiator as BaseMelee;
            bool melee = false;
            if (botMelee != null)
            {
                melee = true;
                info.Initiator = botMelee.GetOwnerPlayer();
            }
            NPCPlayerApex bot = entity as NPCPlayerApex;
            var attackNPC = info?.Initiator as NPCPlayerApex;
            var attackPlayer = info?.Initiator as BasePlayer;
            BotData bData;

            if (bot?.userID != null)
            {
                if (!NPCPlayers.ContainsKey(bot.userID))
                    return null;
                bData = bot.GetComponent<BotData>();
                if (info.Initiator?.ToString() == null && configData.Global.Pve_Safe)
                    info.damageTypes.ScaleAll(0);
                if (attackPlayer != null && attackNPC == null)//bots wont retaliate to vanished players  
                {
                    var canNetwork = Vanish?.Call("IsInvisible", info.Initiator);
                    if ((canNetwork is bool) && (bool)canNetwork)
                    {
                        info.Initiator = null;
                        return true;
                    }

                    if (bData.peaceKeeper)//prevent melee farming with peacekeeper on
                    {
                        var heldMelee = info.Weapon as BaseMelee;
                        var heldTorchWeapon = info.Weapon as TorchWeapon;
                        if (heldMelee != null || heldTorchWeapon != null)
                            info.damageTypes.ScaleAll(0);
                    }
                }
                bData.goingHome = false;
            }

            if (attackNPC?.userID != null && entity is BasePlayer)//add in bot accuracy
            {
                if (!NPCPlayers.ContainsKey(attackNPC.userID))
                    return null;

                bData = attackNPC.GetComponent<BotData>();
                int rand = random.Next(1, 100);
                float distance = (Vector3.Distance(info.Initiator.transform.position, entity.transform.position));

                var newAccuracy = (bData.accuracy * 10f);
                var newDamage = (bData.damage);
                if (distance > 100f)
                {
                    newAccuracy = ((bData.accuracy * 10f) / (distance / 100f));
                    newDamage = bData.damage / (distance / 100f);
                }
                if (!melee && newAccuracy < rand)//scale bot attack damage
                    return true;
                info.damageTypes.ScaleAll(newDamage);
            }
            return null;
        }

        void OnEntityKill(BaseNetworkable entity)
        {
            NPCPlayerApex npc = entity as NPCPlayerApex;
            if (npc?.userID != null && NPCPlayers.ContainsKey(npc.userID))
            {
                var bData = npc.GetComponent<BotData>();
                Item activeItem = npc.GetActiveItem();

                int chance = random.Next(0, 100);
                if (bData.dropweapon > chance && activeItem != null)
                {
                    using (TimeWarning timeWarning = TimeWarning.New("PlayerBelt.DropActive", 0.1f))
                    {
                        activeItem.Drop(npc.eyes.position, new Vector3(), new Quaternion());
                        npc.svActiveItemID = 0;
                        npc.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                        KitRemoveList.Add(npc.userID, activeItem.info.name);
                    }
                }
                if (AllProfiles[bData.monumentName].Disable_Radio == true)
                    npc.DeathEffect = new GameObjectRef();//kill radio effects

                DeadNPCPlayerIds.Add(npc.userID);
                no_of_AI--;
                if (bData.respawn == false)
                {
                    UnityEngine.Object.Destroy(npc.GetComponent<BotData>());
                    UpdateRecords(npc);
                    return;
                }
                if (bData.biome && spawnLists.ContainsKey(bData.monumentName))
                {
                    List<Vector3> spawnList = new List<Vector3>();
                    spawnList = spawnLists[bData.monumentName];
                    int spawnPos = random.Next(spawnList.Count);
                    timer.Once(AllProfiles[bData.monumentName].Respawn_Timer, () =>
                    {
                        if (AllProfiles.ContainsKey(bData.monumentName))
                            SpawnBots(bData.monumentName, AllProfiles[bData.monumentName], "biome", null, spawnList[spawnPos]);
                    });
                    UpdateRecords(npc);
                    return;
                }
                foreach (var profile in AllProfiles)
                {
                    timer.Once(profile.Value.Respawn_Timer, () =>
                    {
                        if (profile.Key == bData.monumentName)
                            SpawnBots(profile.Key, profile.Value, null, null, new Vector3());

                    });
                }
                UnityEngine.Object.Destroy(npc.GetComponent<BotData>());
                UpdateRecords(npc);
            }
        }

        void OnPlayerDie(BasePlayer player) => OnEntityKill(player);

        public static readonly FieldInfo AllScientists = typeof(Scientist).GetField("AllScientists", (BindingFlags.Static | BindingFlags.NonPublic)); //NRE AskQuestion workaround

        void OnEntitySpawned(BaseEntity entity) // handles smoke signals, backpacks, corpses(applying kit)
        {
            NPCPlayerCorpse corpse = entity as NPCPlayerCorpse;
            Scientist sci = entity as Scientist;

            if (sci != null)
                AllScientists.SetValue(sci, new HashSet<Scientist>());//NRE AskQuestion workaround

            var KitDetails = new KitData();
            if (entity == null)
                return;

            var pos = entity.transform.position;

            if (corpse != null)
            {
                corpse.ResetRemovalTime(configData.Global.Corpse_Duration);   

                if (KitList.ContainsKey(corpse.playerSteamID))
                {
                    KitDetails = KitList[corpse.playerSteamID];
                    NextTick(() =>
                    {
                        if (corpse == null)
                            return;

                        List<Item> toDestroy = new List<Item>();
                        foreach (var item in corpse.containers[0].itemList)
                        {
                            if (item.ToString().ToLower().Contains("keycard") && configData.Global.Remove_KeyCard) 
                                toDestroy.Add(item);
                        }

                        foreach (var item in toDestroy)
                            item.RemoveFromContainer(); 

                        if (!KitDetails.Allow_Rust_Loot)
                        {
                            corpse.containers[0].Clear();
                            corpse.containers[1].Clear();
                            corpse.containers[2].Clear();
                        }

                        //Skull
                        Item playerSkull = ItemManager.CreateByName("skull.human", 1);
                        playerSkull.name = string.Concat($"Skull of {corpse.playerName}");
                        ItemAmount SkullInfo = new ItemAmount()
                        {
                            itemDef = playerSkull.info,
                            amount = 1,
                            startAmount = 1
                        };
                        var dispenser = corpse.GetComponent<ResourceDispenser>();
                        if (dispenser != null)
                        {
                            dispenser.containedItems.Add(SkullInfo);
                            dispenser.Initialize();
                        }
                        //EndSkull

                        if (KitDetails.Kit != String.Empty)
                        {
                            var tempbody = GameManager.server.CreateEntity("assets/prefabs/player/player.prefab", (corpse.transform.position - new Vector3(0, -100, 0)), corpse.transform.rotation).ToPlayer();
                            tempbody.Spawn();

                            Kits?.Call($"GiveKit", tempbody, KitDetails.Kit, true);
                            var source = new ItemContainer[] { tempbody.inventory.containerMain, tempbody.inventory.containerWear, tempbody.inventory.containerBelt };

                            for (int i = 0; i < (int)source.Length; i++) 
                            {
                                Item[] array = source[i].itemList.ToArray();
                                for (int j = 0; j < (int)array.Length; j++)
                                {
                                    Item item = array[j];
                                    if (!item.MoveToContainer(corpse.containers[i], -1, true))
                                        item.Remove(0f);
                                }
                            }
                            tempbody.Kill();
                        }
                        if (KitList[corpse.playerSteamID].Wipe_Belt)
                            corpse.containers[2].Clear();
                        else
                        if (KitRemoveList.ContainsKey(corpse.playerSteamID))
                        {
                            foreach (var thing in corpse.containers[2].itemList)//If weapon drop is enabled, this removes the weapon from the corpse's inventory.
                            {
                                if (KitRemoveList[corpse.playerSteamID] == thing.info.name)
                                {
                                    thing.Remove();
                                    KitRemoveList.Remove(corpse.playerSteamID);
                                    break;
                                }
                            }
                        }

                        if (KitList[corpse.playerSteamID].Wipe_Clothing)
                            corpse.containers[1].Clear();

                        KitList.Remove(corpse.playerSteamID);
                    });
                }
            }

            var container = entity as DroppedItemContainer;
            if (container != null)
            {
                NextTick(() =>
                {
                    if (container == null || container.IsDestroyed)
                        return;

                    ulong ownerID = container.playerSteamID;
                    if (ownerID == 0) return;
                    if (configData.Global.Remove_BackPacks)
                    {
                        if (DeadNPCPlayerIds.Contains(ownerID))
                        {
                            entity.Kill();
                            DeadNPCPlayerIds.Remove(ownerID);
                            return;
                        }
                    }

                });
            }

            if (entity.name.Contains("grenade.supplysignal.deployed"))
                timer.Once(2.3f, () =>
                {
                    if (entity != null)
                        SmokeGrenades.Add(new Vector3(entity.transform.position.x, 0, entity.transform.position.z));
                });

            if (!(entity.name.Contains("supply_drop")))
                return;

            Vector3 dropLocation = new Vector3(entity.transform.position.x, 0, entity.transform.position.z);

            if (!(configData.Global.Supply_Enabled))
            {
                foreach (var location in SmokeGrenades.Where(location => Vector3.Distance(location, dropLocation) < 35f))
                {
                    SmokeGrenades.Remove(location);
                    return;
                }
            }
            if (AllProfiles.ContainsKey("AirDrop") && AllProfiles["AirDrop"].AutoSpawn == true)
            {
                var profile = AllProfiles["AirDrop"];
                timer.Repeat(0.1f, profile.Bots, () =>
                {
                    profile.LocationX = entity.transform.position.x;
                    profile.LocationY = entity.transform.position.y;
                    profile.LocationZ = entity.transform.position.z;
                    SpawnBots("AirDrop", profile, "AirDrop", null, new Vector3());
                });
            }
        }
        #endregion

        #region WeaponSwitching
        void SelectWeapon(NPCPlayerApex npcPlayer)
        {
            if (npcPlayer == null) return;
            if (npcPlayer.svActiveItemID == 0)
                return;

            var bData = npcPlayer.GetComponent<BotData>();
            if (bData == null) return;

            List<int> weapons = new List<int>();//check all their weapons
            foreach (Item item in npcPlayer.inventory.containerBelt.itemList)
            {
                var held = item.GetHeldEntity();
                if (held is BaseProjectile || held is BaseMelee || held is TorchWeapon)
                    weapons.Add(Convert.ToInt16(item.position));
            }

            if (weapons.Count == 0)
            {
                PrintWarning(lang.GetMessage("noWeapon", this), bData.monumentName);
                return;
            }

            var victim = npcPlayer.AttackTarget;
            var active = npcPlayer.GetActiveItem();
            HeldEntity heldEntity1 = null;

            if (active != null)
                heldEntity1 = active.GetHeldEntity() as HeldEntity;

            if (npcPlayer.AttackTarget == null)
            {
                if (active != null) SetRange(npcPlayer);
                currentTime = TOD_Sky.Instance.Cycle.Hour;
                uint UID = 0;
                if (currentTime > 20 || currentTime < 8)
                {
                    if (active != null && !(active.ToString().Contains("flashlight")))
                    {
                        List<Item> lightList = new List<Item>();
                        if (active != null && active.contents != null)
                            lightList = active.contents.itemList;

                        foreach (var mod in lightList.Where(mod => mod.info.shortname.Contains("flashlight")))
                            return;
                        foreach (Item item in npcPlayer.inventory.containerBelt.itemList)
                        {
                            if (item.ToString().Contains("flashlight"))
                            {
                                if (heldEntity1 != null)
                                    heldEntity1.SetHeld(false);
                                UID = item.uid;
                                ChangeWeapon(npcPlayer, item);
                                break;
                            }
                            if (item.contents != null)
                                foreach (var mod in item.contents.itemList)
                                {
                                    if (mod.info.shortname.Contains("flashlight"))
                                    {
                                        UID = item.uid;
                                        ChangeWeapon(npcPlayer, item);
                                        break;
                                    }
                                }
                        }
                    }
                }
                else
                {
                    if (active != null && active.ToString().Contains("flashlight"))
                    {
                        foreach (Item item in npcPlayer.inventory.containerBelt.itemList)//pick one at random to start with
                        {
                            if (item.position == weapons[random.Next(weapons.Count)])
                            {
                                if (heldEntity1 != null)
                                    heldEntity1.SetHeld(false);
                                ChangeWeapon(npcPlayer, item);
                            }
                        }
                    }
                }
            }
            else
            {
                if (active != null) SetRange(npcPlayer);
                if (heldEntity1 == null)
                    bData.currentWeaponRange = 0;

                float distance = Vector3.Distance(npcPlayer.transform.position, victim.transform.position);
                int newCurrentRange = 0;
                int noOfAvailableWeapons = 0;
                int selectedWeapon;
                Item chosenWeapon = null;
                HeldEntity held = null;


                List<Item> rangeToUse = new List<Item>();
                if (distance < 2f && bData.MeleeWeapons.Count != 0)
                {
                    bData.enemyDistance = 1;
                    rangeToUse = bData.MeleeWeapons;
                    newCurrentRange = 1;
                }
                else if (distance > 1f && distance < 20f)
                {
                    if (bData.CloseRangeWeapons.Count > 0)
                    {
                        bData.enemyDistance = 2;
                        rangeToUse = bData.CloseRangeWeapons;
                        newCurrentRange = 2;
                    }
                    else
                    {
                        rangeToUse = bData.MediumRangeWeapons;
                        newCurrentRange = 3;
                    }
                }
                else if (distance > 19f && distance < 40f && bData.MediumRangeWeapons.Count > 0)
                {
                    bData.enemyDistance = 3;
                    rangeToUse = bData.MediumRangeWeapons;
                    newCurrentRange = 3;
                }
                else if (distance > 39)
                {
                    if (bData.LongRangeWeapons.Count > 0)
                    {
                        bData.enemyDistance = 4;
                        rangeToUse = bData.LongRangeWeapons;
                        newCurrentRange = 4;
                    }
                    else
                    {
                        rangeToUse = bData.MediumRangeWeapons;
                        newCurrentRange = 3;
                    }
                }

                if (rangeToUse.Count > 0)
                {
                    selectedWeapon = random.Next(rangeToUse.Count);
                    chosenWeapon = rangeToUse[selectedWeapon];
                }

                if (chosenWeapon == null)                                               //if no weapon suited to range, pick any random bullet weapon
                {                                                                       //prevents sticking with melee @>2m when no pistol is available
                    bData.enemyDistance = 5;
                    if (heldEntity1 != null && bData.AllProjectiles.Contains(active))   //prevents choosing a random weapon if the existing one is fine
                        return;
                    foreach (var weap in bData.AllProjectiles)
                    {
                        noOfAvailableWeapons++;
                    }
                    if (noOfAvailableWeapons > 0)
                    {
                        selectedWeapon = random.Next(bData.AllProjectiles.Count);
                        chosenWeapon = bData.AllProjectiles[selectedWeapon];
                        newCurrentRange = 5;
                    }
                }
                if (chosenWeapon == null) return;
                if (newCurrentRange == bData.currentWeaponRange) return;
                bData.currentWeaponRange = newCurrentRange;
                held = chosenWeapon.GetHeldEntity() as HeldEntity;
                if (heldEntity1 != null && heldEntity1.name == held.name)
                    return;
                if (heldEntity1 != null && heldEntity1.name != held.name)
                    heldEntity1.SetHeld(false);
                ChangeWeapon(npcPlayer, chosenWeapon);
            }
        }

        void ChangeWeapon(NPCPlayerApex npc, Item item)
        {
            Item activeItem1 = npc.GetActiveItem();
            npc.svActiveItemID = 0U;
            if (activeItem1 != null)
            {
                HeldEntity heldEntity = activeItem1.GetHeldEntity() as HeldEntity;
                if (heldEntity != null)
                    heldEntity.SetHeld(false);
            }
            npc.svActiveItemID = item.uid;
            npc.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
            Item activeItem2 = npc.GetActiveItem();
            if (activeItem2 != null)
            {
                HeldEntity heldEntity = activeItem2.GetHeldEntity() as HeldEntity;
                if (heldEntity != null)
                    heldEntity.SetHeld(true);
            }
            npc.inventory.UpdatedVisibleHolsteredItems();
            npc.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
            SetRange(npc);
        }

        void SetRange(NPCPlayerApex npcPlayer)
        {
            if (npcPlayer != null && npcPlayer is NPCMurderer)
            {
                var attackEntity = npcPlayer.GetHeldEntity();
                var bData = npcPlayer.GetComponent<BotData>();
                if (bData == null || attackEntity == null)
                    return;
                var held = attackEntity as AttackEntity;
                if (held == null)
                    return;
                if (bData.currentWeaponRange == 0 || bData.currentWeaponRange == 1)
                    held.effectiveRange = 2;
                else if (bData.currentWeaponRange == 2 || bData.currentWeaponRange == 3)
                    held.effectiveRange = 100;
                else if (bData.currentWeaponRange == 5)
                    held.effectiveRange = 200;
            }
        }
        #endregion

        #region behaviour hooks
        object OnNpcPlayerResume(NPCPlayerApex player)
        {
            var bData = player.GetComponent<BotData>();
            return (bData != null && bData.inAir) ? true : (object)null;
        }

        object OnNpcDestinationSet(NPCPlayerApex player)
        {
            var bData = player.GetComponent<BotData>();
            return (bData != null && bData.goingHome) ? true : (object)null;
        }
        #endregion

        #region OnNpcHooks
        object OnNpcPlayerTarget(NPCPlayerApex npcPlayer, BaseEntity entity)
        {
            if (npcPlayer == null || entity == null)
                return null;
            bool vicIsMine = false;
            bool attackerIsMine = NPCPlayers.ContainsKey(npcPlayer.userID);
            var conf = configData.Global;

            NPCPlayer botVictim = entity as NPCPlayer;
            if (botVictim != null)
            {
                vicIsMine = NPCPlayers.ContainsKey(botVictim.userID);
                if (npcPlayer == botVictim)
                    return null;

                if (vicIsMine && !attackerIsMine && !conf.NPCs_Attack_BotSpawn)//stop oustideNPCs attacking BotSpawn bots
                    return true;

                if (!attackerIsMine)
                    return null;

                if (npcPlayer.GetType() == botVictim.GetType())
                    return true;

                if (!vicIsMine)
                {
                    if (!conf.BotSpawn_Attacks_NPCs)//stop BotSpawn bots attacking outsideNPCs                                                                                  
                        return true;
                }
                else if (!conf.BotSpawn_Attacks_BotSpawn)//stop BotSpawn murd+sci fighting each other 
                    return true;
            }

            if (!attackerIsMine)
                return null;

            BasePlayer victim = entity as BasePlayer;

            if (victim != null)
            {
                if (!victim.userID.IsSteamId() && conf.Ignore_HumanNPC)//stops bots targeting humannpc 
                    return true;

                var bData = npcPlayer.GetComponent<BotData>();

                currentTime = TOD_Sky.Instance.Cycle.Hour;
                bData.goingHome = false;
                var active = npcPlayer.GetActiveItem();

                HeldEntity heldEntity1 = null;
                if (active != null)
                    heldEntity1 = active.GetHeldEntity() as HeldEntity;

                if (heldEntity1 == null)//freshspawn catch, pre weapon draw. 
                    return null;
                if (currentTime > 20 || currentTime < 8)
                    heldEntity1.SetLightsOn(true);
                else
                    heldEntity1.SetLightsOn(false);

                var heldWeapon = victim.GetHeldEntity() as BaseProjectile;
                var heldFlame = victim.GetHeldEntity() as FlameThrower;

                if (bData.peaceKeeper)
                {
                    if (heldWeapon != null || heldFlame != null)
                        if (!AggroPlayers.Contains(victim.userID))
                            AggroPlayers.Add(victim.userID);

                    if ((heldWeapon == null && heldFlame == null) || (victim.svActiveItemID == 0u))
                    {
                        if (AggroPlayers.Contains(victim.userID) && !coolDownPlayers.Contains(victim.userID))
                        {
                            coolDownPlayers.Add(victim.userID);
                            timer.Once(bData.peaceKeeper_CoolDown, () =>
                            {
                                if (AggroPlayers.Contains(victim.userID))
                                {
                                    AggroPlayers.Remove(victim.userID);
                                    coolDownPlayers.Remove(victim.userID);
                                }
                            });
                        }
                        if (!(AggroPlayers.Contains(victim.userID)))
                            return true;
                    }
                }

                if (npcPlayer is NPCMurderer)
                {
                    var path1 = AllProfiles[bData.monumentName];
                    var distance = Vector3.Distance(npcPlayer.transform.position, victim.transform.position);

                    if (npcPlayer.lastAttacker != victim && (path1.Peace_Keeper || distance > path1.Sci_Aggro_Range))
                        return true;

                    if (npcPlayer.TimeAtDestination > 5)
                    {
                        npcPlayer.AttackTarget = null;
                        npcPlayer.lastAttacker = null;
                        npcPlayer.SetFact(NPCPlayerApex.Facts.HasEnemy, 0, true, true);
                        return true;
                    }
                    Vector3 vector3;
                    float single, single1, single2;
                    Rust.Ai.BestPlayerDirection.Evaluate(npcPlayer, victim.ServerPosition, out vector3, out single);
                    Rust.Ai.BestPlayerDistance.Evaluate(npcPlayer, victim.ServerPosition, out single1, out single2);
                    var info = new Rust.Ai.Memory.ExtendedInfo();
                    npcPlayer.AiContext.Memory.Update(victim, victim.ServerPosition, 1, vector3, single, single1, 1, true, 1f, out info);
                }
                bData.goingHome = false;

                if (victim.IsSleeping() && conf.Ignore_Sleepers)
                    return true;
            }

            return (entity.name.Contains("agents/") || (entity is HTNPlayer && conf.Ignore_HTN))
                ? true
                : (object)null;
        }

        object OnNpcTarget(BaseNpc npc, BaseEntity entity)//stops animals targeting bots
        {
            NPCPlayer npcPlayer = entity as NPCPlayer;
            return (npcPlayer != null && NPCPlayers.ContainsKey(npcPlayer.userID) && configData.Global.Animal_Safe) ? true : (object)null;
        }

        object OnNpcStopMoving(NPCPlayerApex npcPlayer)
        {
            return NPCPlayers.ContainsKey(npcPlayer.userID) ? true : (object)null;
        }
        #endregion

        private object CanBeTargeted(BaseCombatEntity player, MonoBehaviour turret)//stops autoturrets targetting bots
        {
            NPCPlayer npcPlayer = player as NPCPlayer;
            return (npcPlayer != null && NPCPlayers.ContainsKey(npcPlayer.userID) && configData.Global.Turret_Safe) ? false : (object)null; ;
        }


        private object CanBradleyApcTarget(BradleyAPC bradley, BaseEntity target)//stops bradley targeting bots
        {
            NPCPlayer npcPlayer = target as NPCPlayer;
            return (npcPlayer != null && NPCPlayers.ContainsKey(npcPlayer.userID) && configData.Global.APC_Safe) ? false : (object)null;
        }

        #region SetUpLocations
        private void FindMonuments()
        {
            AllProfiles.Clear();
            var allobjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            Vector3 pos;
            float rot;
            int miningoutpost = 0, lighthouse = 0, gasstation = 0, spermket = 0, compound = 0;

            foreach (var gobject in allobjects)
            {
                pos = gobject.transform.position;
                rot = gobject.transform.eulerAngles.y;

                if (gobject.name.Contains("autospawn/monument") && pos != new Vector3(0, 0, 0))
                {
                    if (gobject.name.Contains("airfield_1"))
                    {
                        AddProfile("Airfield", configData.Monuments.Airfield, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("compound") && compound == 0)
                    {
                        AddProfile("Compound", configData.Monuments.Compound, pos, rot);
                        compound++;
                        continue;
                    }
                    if (gobject.name.Contains("compound") && compound == 1)
                    {
                        AddProfile("Compound1", configData.Monuments.Compound1, pos, rot);
                        compound++;
                        continue;
                    }
                    if (gobject.name.Contains("compound") && compound == 2)
                    {
                        AddProfile("Compound2", configData.Monuments.Compound2, pos, rot);
                        compound++;
                        continue;
                    }
                    if (gobject.name.Contains("sphere_tank"))
                    {
                        AddProfile("Dome", configData.Monuments.Dome, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("gas_station_1") && gasstation == 0)
                    {
                        AddProfile("GasStation", configData.Monuments.GasStation, pos, rot);
                        gasstation++;
                        continue;
                    }
                    if (gobject.name.Contains("gas_station_1") && gasstation == 1)
                    {
                        AddProfile("GasStation1", configData.Monuments.GasStation1, pos, rot);
                        gasstation++;
                        continue;
                    }
                    if (gobject.name.Contains("harbor_1"))
                    {
                        AddProfile("Harbor1", configData.Monuments.Harbor1, pos, rot);
                        continue;
                    }

                    if (gobject.name.Contains("harbor_2"))
                    {
                        AddProfile("Harbor2", configData.Monuments.Harbor2, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("junkyard"))
                    {
                        AddProfile("Junkyard", configData.Monuments.Junkyard, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("launch_site"))
                    {
                        AddProfile("Launchsite", configData.Monuments.Launchsite, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("lighthouse") && lighthouse == 0)
                    {
                        AddProfile("Lighthouse", configData.Monuments.Lighthouse, pos, rot);
                        lighthouse++;
                        continue;
                    }

                    if (gobject.name.Contains("lighthouse") && lighthouse == 1)
                    {
                        AddProfile("Lighthouse1", configData.Monuments.Lighthouse1, pos, rot);
                        lighthouse++;
                        continue;
                    }

                    if (gobject.name.Contains("lighthouse") && lighthouse == 2)
                    {
                        AddProfile("Lighthouse2", configData.Monuments.Lighthouse2, pos, rot);
                        lighthouse++;
                        continue;
                    }

                    if (gobject.name.Contains("military_tunnel_1"))
                    {
                        AddProfile("MilitaryTunnel", configData.Monuments.MilitaryTunnel, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("powerplant_1"))
                    {
                        AddProfile("PowerPlant", configData.Monuments.PowerPlant, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("mining_quarry_c"))
                    {
                        AddProfile("QuarryHQM", configData.Monuments.QuarryHQM, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("mining_quarry_b"))
                    {
                        AddProfile("QuarryStone", configData.Monuments.QuarryStone, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("mining_quarry_a"))
                    {
                        AddProfile("QuarrySulphur", configData.Monuments.QuarrySulphur, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("radtown_small_3"))
                    {
                        AddProfile("SewerBranch", configData.Monuments.SewerBranch, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("satellite_dish"))
                    {
                        AddProfile("Satellite", configData.Monuments.Satellite, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("supermarket_1") && spermket == 0)
                    {
                        AddProfile("SuperMarket", configData.Monuments.SuperMarket, pos, rot);
                        spermket++;
                        continue;
                    }

                    if (gobject.name.Contains("supermarket_1") && spermket == 1)
                    {
                        AddProfile("SuperMarket1", configData.Monuments.SuperMarket1, pos, rot);
                        spermket++;
                        continue;
                    }
                    if (gobject.name.Contains("trainyard_1"))
                    {
                        AddProfile("Trainyard", configData.Monuments.Trainyard, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("warehouse") && miningoutpost == 0)
                    {
                        AddProfile("MiningOutpost", configData.Monuments.MiningOutpost, pos, rot);
                        miningoutpost++;
                        continue;
                    }

                    if (gobject.name.Contains("warehouse") && miningoutpost == 1)
                    {
                        AddProfile("MiningOutpost1", configData.Monuments.MiningOutpost1, pos, rot);
                        miningoutpost++;
                        continue;
                    }

                    if (gobject.name.Contains("warehouse") && miningoutpost == 2)
                    {
                        AddProfile("MiningOutpost2", configData.Monuments.MiningOutpost2, pos, rot);
                        miningoutpost++;
                        continue;
                    }
                    if (gobject.name.Contains("water_treatment_plant_1"))
                    {
                        AddProfile("Watertreatment", configData.Monuments.Watertreatment, pos, rot);
                        continue;
                    }

                    if (gobject.name.Contains("swamp_a"))
                    {
                        AddProfile("Swamp_A", configData.Monuments.Swamp_A, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("swamp_b"))
                    {
                        AddProfile("Swamp_B", configData.Monuments.Swamp_B, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("swamp_c"))
                    {
                        AddProfile("Abandoned_Cabins", configData.Monuments.Abandoned_Cabins, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("bandit_town"))
                    {
                        AddProfile("Bandit_Town", configData.Monuments.Bandit_Town, pos, rot);
                        continue;
                    }
                    if (gobject.name.Contains("compound") && compound > 2)
                        continue;
                    if (gobject.name.Contains("gas_station_1") && gasstation > 1)
                        continue;
                    if (gobject.name.Contains("lighthouse") && lighthouse > 2)
                        continue;
                    if (gobject.name.Contains("supermarket_1") && spermket > 1)
                        continue;
                    if (gobject.name.Contains("miningoutpost") && miningoutpost > 2)
                        continue;
                }
            }

            if (configData.Biomes.BiomeArid.AutoSpawn == true)
            {
                AddProfile("BiomeArid", configData.Biomes.BiomeArid, new Vector3(), 0f);
                GenerateSpawnPoints("BiomeArid", configData.Biomes.BiomeArid.Bots, aridTimer, 1);
            }
            if (configData.Biomes.BiomeTemperate.AutoSpawn == true)
            {
                AddProfile("BiomeTemperate", configData.Biomes.BiomeTemperate, new Vector3(), 0f);
                GenerateSpawnPoints("BiomeTemperate", configData.Biomes.BiomeTemperate.Bots, temperateTimer, 2);
            }
            if (configData.Biomes.BiomeTundra.AutoSpawn == true)
            {
                AddProfile("BiomeTundra", configData.Biomes.BiomeTundra, new Vector3(), 0f);
                GenerateSpawnPoints("BiomeTundra", configData.Biomes.BiomeTundra.Bots, tundraTimer, 4);
            }
            if (configData.Biomes.BiomeArctic.AutoSpawn == true)
            {
                AddProfile("BiomeArctic", configData.Biomes.BiomeArctic, new Vector3(), 0f);
                GenerateSpawnPoints("BiomeArctic", configData.Biomes.BiomeArctic.Bots, arcticTimer, 8);
            }

            var drop = JsonConvert.SerializeObject(configData.Monuments.AirDrop);
            DataProfile Airdrop = JsonConvert.DeserializeObject<DataProfile>(drop);
            AllProfiles.Add("AirDrop", Airdrop);

            foreach (var profile in storedData.DataProfiles)
            {

                if (!(storedData.MigrationDataDoNotEdit.ContainsKey(profile.Key)))
                    storedData.MigrationDataDoNotEdit.Add(profile.Key, new ProfileRelocation());

                if (profile.Value.Parent_Monument != String.Empty)
                {
                    var path = storedData.MigrationDataDoNotEdit[profile.Key];

                    if (AllProfiles.ContainsKey(profile.Value.Parent_Monument) && !IsBiome(profile.Value.Parent_Monument))
                    {
                        var configPath = AllProfiles[profile.Value.Parent_Monument];

                        path.ParentMonumentX = configPath.LocationX; //Incase user changed Parent after load
                        path.ParentMonumentY = configPath.LocationY;
                        path.ParentMonumentZ = configPath.LocationZ;

                        if (Mathf.Approximately(path.OldParentMonumentX, 0.0f)) //If it's a new entry, save current monument location info
                        {
                            Puts($"Saved migration data for {profile.Key}");
                            path.OldParentMonumentX = configPath.LocationX;
                            path.OldParentMonumentY = configPath.LocationY;
                            path.OldParentMonumentZ = configPath.LocationZ;
                            path.oldRotation = path.worldRotation;
                        }

                        if (!(Mathf.Approximately(path.ParentMonumentX, path.OldParentMonumentX))) //if old and new aren't equal
                        {
                            bool userChanged = false;
                            foreach (var monument in AllProfiles)
                                if (Mathf.Approximately(monument.Value.LocationX, path.OldParentMonumentX)) //but old matches some other monument, then the user must have switched Parent
                                {
                                    userChanged = true;
                                    break;
                                }

                            if (userChanged)
                            {
                                Puts($"Parent_Monument change detected - Saving {profile.Key} location relative to {profile.Value.Parent_Monument}");
                                path.OldParentMonumentX = path.ParentMonumentX;
                                path.OldParentMonumentY = path.ParentMonumentY;
                                path.OldParentMonumentZ = path.ParentMonumentZ;
                                path.oldRotation = path.worldRotation;
                            }
                            else
                            {
                                Puts($"Map seed change detected - Updating {profile.Key} location relative to new {profile.Value.Parent_Monument}");
                                Vector3 oldloc = new Vector3(profile.Value.LocationX, profile.Value.LocationY, profile.Value.LocationZ);
                                Vector3 oldMonument = new Vector3(path.OldParentMonumentX, path.OldParentMonumentY, path.OldParentMonumentZ);
                                Vector3 newMonument = new Vector3(path.ParentMonumentX, path.ParentMonumentY, path.ParentMonumentZ);
                                //Map Seed Changed  

                                var newTrans = new GameObject().transform;
                                newTrans.transform.position = oldloc;
                                newTrans.transform.RotateAround(oldMonument, Vector3.down, path.oldRotation);

                                //spin old loc around old monument until mon-rotation is 0
                                //get relationship between old location(rotated) minus monument
                                Vector3 newLocPreRot = newMonument + (newTrans.transform.position - oldMonument);               //add that difference to the new monument location

                                newTrans.transform.position = newLocPreRot;
                                newTrans.transform.RotateAround(newMonument, Vector3.down, -path.worldRotation);
                                Vector3 newLocation = newTrans.transform.position;                                              //rotate that number around the monument by new mon Rotation

                                profile.Value.LocationX = newLocation.x;
                                profile.Value.LocationY = newLocation.y;
                                profile.Value.LocationZ = newLocation.z;

                                path.oldRotation = path.worldRotation;
                                path.OldParentMonumentX = configPath.LocationX;
                                path.OldParentMonumentY = configPath.LocationY;
                                path.OldParentMonumentZ = configPath.LocationZ;
                                path.ParentMonumentX = configPath.LocationX;
                                path.ParentMonumentY = configPath.LocationY;
                                path.ParentMonumentZ = configPath.LocationZ;
                            }
                        }
                    }
                    else
                    {
                        Puts($"Parent monument {profile.Value.Parent_Monument} does not exist for custom profile {profile.Key}");
                        profile.Value.AutoSpawn = false;
                        SaveData();
                    }
                }
                SaveData();
                AllProfiles.Add(profile.Key, profile.Value);
            }

            foreach (var profile in AllProfiles)
            {
                if (IsBiome(profile.Key))
                    continue;
                if (profile.Value.Kit.Count > 0 && Kits == null)
                {
                    PrintWarning(lang.GetMessage("nokits", this), profile.Key);
                    continue;
                }

                if (profile.Value.AutoSpawn == true && profile.Value.Bots > 0 && !profile.Key.Contains("AirDrop"))
                {
                    timer.Repeat(2, profile.Value.Bots, () =>
                    {
                        if (AllProfiles.Contains(profile))
                            SpawnBots(profile.Key, profile.Value, null, null, new Vector3());
                    });
                }
            }
        }

        void AddProfile(string name, ConfigProfile monument, Vector3 pos, float rotation)//bring config data into live data
        {
            var toAdd = JsonConvert.SerializeObject(monument);
            DataProfile toAddDone = JsonConvert.DeserializeObject<DataProfile>(toAdd);
            if (AllProfiles.ContainsKey(name)) return;
            AllProfiles.Add(name, toAddDone);
            AllProfiles[name].LocationX = pos.x;
            AllProfiles[name].LocationY = pos.y;
            AllProfiles[name].LocationZ = pos.z;
            foreach (var custom in storedData.DataProfiles)
            {
                if (custom.Value.Parent_Monument == name && storedData.MigrationDataDoNotEdit.ContainsKey(custom.Key))
                {
                    var path = storedData.MigrationDataDoNotEdit[custom.Key];
                    if (Mathf.Approximately(path.oldRotation, 0))
                        path.oldRotation = rotation;

                    path.worldRotation = rotation;
                    path.ParentMonumentX = pos.x;
                    path.ParentMonumentY = pos.y;
                    path.ParentMonumentZ = pos.z;
                }
            }
            SaveData();
        }
        #endregion

        void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject("BotSpawn", storedData);
        }
        #region Commands
        [ConsoleCommand("bot.count")]
        void CmdBotCount()
        {
            string msg = (NPCPlayers.Count == 1) ? "numberOfBot" : "numberOfBots";
            PrintWarning(lang.GetMessage(msg, this), NPCPlayers.Count);
        }

        [ConsoleCommand("botspawn")]
        private void CmdBotSpawn(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length != 2) return;
            foreach (var bot in NPCPlayers.Where(bot => bot.Value.GetComponent<BotData>().monumentName == arg.Args[0]))
                foreach (var gun in bot.Value.GetComponent<BotData>().AllProjectiles.Where(gun => gun.info.shortname == arg.Args[1]))
                    ChangeWeapon(bot.Value, gun);
        }

        [ChatCommand("botspawn")]
        void Botspawn(BasePlayer player, string command, string[] args)
        {
            if (HasPermission(player.UserIDString, permAllowed) || IsAuth(player))
                if (args != null && args.Length == 1)
                {
                    if (args[0] == "list")
                    {
                        var outMsg = lang.GetMessage("ListTitle", this);

                        foreach (var profile in storedData.DataProfiles)
                        {
                            outMsg += $"\n{profile.Key}";
                        }
                        PrintToChat(player, outMsg);
                    }
                    else
                        SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("error", this));
                }
                else if (args != null && args.Length == 2)
                {
                    if (args[0] == "add")
                    {
                        var name = args[1];
                        if (AllProfiles.ContainsKey(name))
                        {
                            SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("alreadyexists", this), name);
                            return;
                        }
                        Vector3 pos = player.transform.position;

                        var customSettings = new DataProfile()
                        {
                            AutoSpawn = false,
                            BotNames = new List<string> { String.Empty },
                            LocationX = pos.x,
                            LocationY = pos.y,
                            LocationZ = pos.z,
                        };

                        storedData.DataProfiles.Add(name, customSettings);
                        SaveData();
                        SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("customsaved", this), player.transform.position);
                    }

                    else if (args[0] == "move")
                    {
                        var name = args[1];
                        if (storedData.DataProfiles.ContainsKey(name))
                        {
                            storedData.DataProfiles[name].LocationX = player.transform.position.x;
                            storedData.DataProfiles[name].LocationY = player.transform.position.y;
                            storedData.DataProfiles[name].LocationZ = player.transform.position.z;
                            SaveData();
                            SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("custommoved", this), name);
                        }
                        else
                            SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("noprofile", this));
                    }

                    else if (args[0] == "remove")
                    {
                        var name = args[1];
                        if (storedData.DataProfiles.ContainsKey(name))
                        {
                            foreach (var bot in NPCPlayers)
                            {
                                if (bot.Value == null)
                                    continue;

                                var bData = bot.Value.GetComponent<BotData>();
                                if (bData.monumentName == name)
                                    bot.Value.Kill();
                            }
                            AllProfiles.Remove(name);
                            storedData.DataProfiles.Remove(name);
                            SaveData();
                            SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("customremoved", this), name);
                        }
                        else
                            SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("noprofile", this));
                    }
                    else
                        SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("error", this));
                }
                else if (args != null && args.Length == 3)
                {
                    if (args[0] == "toplayer")
                    {
                        var name = args[1];
                        var profile = args[2].ToLower();
                        BasePlayer target = FindPlayerByName(name);
                        Vector3 location = (CalculateGroundPos(player.transform.position));
                        var found = false;
                        if (target == null)
                        {
                            SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("namenotfound", this), name);
                            return;
                        }
                        foreach (var entry in AllProfiles.Where(entry => entry.Key.ToLower() == profile))
                        {
                            AttackPlayer(location, entry.Key, entry.Value, null);
                            SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("deployed", this), entry.Key, target.displayName);
                            found = true;
                            return;
                        }
                        if (!found)
                        {
                            SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("noprofile", this));
                            return;
                        }

                    }
                    else
                        SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("error", this));
                }
                else
                    SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("error", this));
        }
        #endregion

        public List<ulong> DeadNPCPlayerIds = new List<ulong>(); //to tracebackpacks
        public Dictionary<ulong, KitData> KitList = new Dictionary<ulong, KitData>();
        public Dictionary<ulong, string> KitRemoveList = new Dictionary<ulong, string>();
        public List<Vector3> SmokeGrenades = new List<Vector3>();
        public List<ulong> AggroPlayers = new List<ulong>();

        #region BotMono
        public class KitData
        {
            public string Kit;
            public bool Wipe_Belt;
            public bool Wipe_Clothing;
            public bool Allow_Rust_Loot;
        }

        public class BotData : MonoBehaviour
        {
            public NPCPlayerApex npc;
            public DataProfile profile;
            public Vector3 spawnPoint;
            public float enemyDistance, damage;
            public List<Item> AllProjectiles = new List<Item>();
            public List<Item> MeleeWeapons = new List<Item>();
            public List<Item> CloseRangeWeapons = new List<Item>();
            public List<Item> MediumRangeWeapons = new List<Item>();
            public List<Item> LongRangeWeapons = new List<Item>();
            public int dropweapon, roamRange, accuracy, currentWeaponRange, LongRangeAttack = 120, peaceKeeper_CoolDown = 5, landingAttempts;
            public string monumentName, group; //external hook identifier 
            public bool chute, inAir, peaceKeeper, keepAttire, goingHome, biome, respawn;

            Vector3 landingDirection = new Vector3(0, 0, 0);
            int updateCounter;

            void Start()
            {
                npc = GetComponent<NPCPlayerApex>();
                if (chute)
                {
                    inAir = true;
                    npc.Stats.AggressionRange = 300f;
                    npc.utilityAiComponent.enabled = true;
                }
                float delay = random.Next(300, 1200);
                InvokeRepeating("Relocate", delay, delay);
            }

            void OnDestroy() => CancelInvoke("Relocate");

            void Relocate()
            {
                if (biome)
                {
                    var ranNum = random.Next(botSpawn.spawnLists[monumentName].Count);
                    spawnPoint = botSpawn.spawnLists[monumentName][ranNum];
                    return;
                }
                var pos = new Vector3(profile.LocationX, profile.LocationY, profile.LocationZ);
                var randomTerrainPoint = botSpawn.TryGetSpawn(pos, profile.Radius);
                if (randomTerrainPoint != new Vector3())
                    spawnPoint = randomTerrainPoint + new Vector3(0, 0.5f, 0);
            }

            void OnCollisionEnter(Collision collision)
            {
                if (!inAir)
                    return;
                var rb = npc.gameObject.GetComponent<Rigidbody>();
                var terrainDif = npc.transform.position.y - CalculateGroundPos(npc.transform.position).y;
                if (landingAttempts == 0)
                    landingDirection = npc.transform.forward;

                if (collision.collider.name.Contains("npc"))
                    return;
                if (collision.collider.name.Contains("Terrain") || landingAttempts > 5)
                {
                    NavMeshHit hit;
                    if (NavMesh.SamplePosition(npc.transform.position, out hit, 50, 1))
                    {
                        rb.isKinematic = true;
                        rb.useGravity = false;
                        npc.gameObject.layer = 17;
                        npc.ServerPosition = hit.position;
                        npc.GetNavAgent.Warp(npc.ServerPosition);
                        npc.Stats.AggressionRange = botSpawn.AllProfiles[monumentName].Sci_Aggro_Range;
                        foreach (var child in npc.children.Where(child => child.name.Contains("parachute")))
                        {
                            child.SetParent(null);
                            child.Kill();
                            break;
                        }
                        SetSpawn(npc);
                        landingAttempts = 0;
                    }
                }
                else
                {
                    landingAttempts++;
                    rb.useGravity = true;
                    rb.velocity = new Vector3(landingDirection.x * 15, 11, landingDirection.z * 15);
                    rb.drag = 1f;
                }
            }

            void SetSpawn(NPCPlayerApex bot)
            {
                inAir = false;
                spawnPoint = bot.transform.position;
                bot.SpawnPosition = bot.transform.position;
                bot.Resume();
            }

            void Update()
            {
                updateCounter++;
                if (updateCounter == 1000)
                {
                    updateCounter = 0;
                    if (inAir)
                    {
                        if (npc.AttackTarget is BasePlayer && !(npc.AttackTarget is NPCPlayer))
                            npc.SetAimDirection((npc.AttackTarget.transform.position - npc.GetPosition()).normalized);
                        goingHome = false;
                    }
                    else
                    {
                        if ((npc.GetFact(NPCPlayerApex.Facts.IsAggro)) == 0 && npc.GetNavAgent.isOnNavMesh)
                        {
                            var distance = Vector3.Distance(npc.transform.position, spawnPoint);
                            if (!goingHome && distance > roamRange)
                                goingHome = true;


                            if (goingHome && distance > 5)
                            {
                                npc.CurrentBehaviour = BaseNpc.Behaviour.Wander;
                                npc.SetFact(NPCPlayerApex.Facts.Speed, (byte)NPCPlayerApex.SpeedEnum.Walk, true, true);
                                npc.TargetSpeed = 2.4f;
                                npc.GetNavAgent.SetDestination(spawnPoint);
                                npc.Destination = spawnPoint;
                            }
                            else
                                goingHome = false;
                        }
                    }
                }
            }
        }
        #endregion

        #region Config
        private ConfigData configData;

        public Dictionary<ulong, NPCPlayerApex> NPCPlayers = new Dictionary<ulong, NPCPlayerApex>();
        public Dictionary<string, DataProfile> AllProfiles = new Dictionary<string, DataProfile>();

        public class Global
        {
            public bool NPCs_Attack_BotSpawn = true;
            public bool BotSpawn_Attacks_NPCs = true;
            public bool BotSpawn_Attacks_BotSpawn;
            public bool APC_Safe = true;
            public bool Turret_Safe = true;
            public bool Animal_Safe = true;
            public bool Supply_Enabled;
            public bool Remove_BackPacks = true;
            public bool Remove_KeyCard = true;
            public bool Ignore_HumanNPC = true;
            public bool Ignore_HTN = true;
            public bool Ignore_Sleepers = true;
            public bool Pve_Safe = true;
            public int Corpse_Duration = 60;
        }
        public class Monuments
        {
            public AirDropProfile AirDrop = new AirDropProfile { };
            public ConfigProfile Airfield = new ConfigProfile { };
            public ConfigProfile Dome = new ConfigProfile { };
            public ConfigProfile Compound = new ConfigProfile { };
            public ConfigProfile Compound1 = new ConfigProfile { };
            public ConfigProfile Compound2 = new ConfigProfile { };
            public ConfigProfile GasStation = new ConfigProfile { };
            public ConfigProfile GasStation1 = new ConfigProfile { };
            public ConfigProfile Harbor1 = new ConfigProfile { };
            public ConfigProfile Harbor2 = new ConfigProfile { };
            public ConfigProfile Junkyard = new ConfigProfile { };
            public ConfigProfile Launchsite = new ConfigProfile { };
            public ConfigProfile Lighthouse = new ConfigProfile { };
            public ConfigProfile Lighthouse1 = new ConfigProfile { };
            public ConfigProfile Lighthouse2 = new ConfigProfile { };
            public ConfigProfile MilitaryTunnel = new ConfigProfile { };
            public ConfigProfile PowerPlant = new ConfigProfile { };
            public ConfigProfile QuarrySulphur = new ConfigProfile { };
            public ConfigProfile QuarryStone = new ConfigProfile { };
            public ConfigProfile QuarryHQM = new ConfigProfile { };
            public ConfigProfile SuperMarket = new ConfigProfile { };
            public ConfigProfile SuperMarket1 = new ConfigProfile { };
            public ConfigProfile SewerBranch = new ConfigProfile { };
            public ConfigProfile Satellite = new ConfigProfile { };
            public ConfigProfile Trainyard = new ConfigProfile { };
            public ConfigProfile MiningOutpost = new ConfigProfile { };
            public ConfigProfile MiningOutpost1 = new ConfigProfile { };
            public ConfigProfile MiningOutpost2 = new ConfigProfile { };
            public ConfigProfile Watertreatment = new ConfigProfile { };
            public ConfigProfile Swamp_A = new ConfigProfile { };
            public ConfigProfile Swamp_B = new ConfigProfile { };
            public ConfigProfile Abandoned_Cabins = new ConfigProfile { };
            public ConfigProfile Bandit_Town = new ConfigProfile { };
        }

        public class Biomes
        {
            public ConfigProfile BiomeArid = new ConfigProfile { };
            public ConfigProfile BiomeTemperate = new ConfigProfile { };
            public ConfigProfile BiomeTundra = new ConfigProfile { };
            public ConfigProfile BiomeArctic = new ConfigProfile { };
        }
        public class AirDropProfile
        {
            public bool AutoSpawn;
            public bool Murderer;
            public int Bots = 5;
            public int BotHealth = 100;
            public int Radius = 100;
            public List<string> Kit = new List<string>();
            public string BotNamePrefix = String.Empty;
            public List<string> BotNames = new List<string>();
            public int Bot_Accuracy = 4;
			public int Min_Bot_Accuracy = 4;
			public int Max_Bot_Accuracy = 4;
            public float Bot_Damage = 0.4f;
            public bool Disable_Radio = true;
            public int Roam_Range = 40;
            public bool Peace_Keeper = true;
            public int Peace_Keeper_Cool_Down = 5;
            public int Weapon_Drop_Chance = 100;
            public bool Keep_Default_Loadout;
            public bool Wipe_Belt = true;
            public bool Wipe_Clothing = true;
            public bool Allow_Rust_Loot = true;
            public int Suicide_Timer = 300;
            public bool Chute;
            public int Sci_Aggro_Range = 30;
            public int Sci_DeAggro_Range = 40;
        }

        public class ConfigProfile
        {
            public bool AutoSpawn;
            public bool Murderer;
            public int Bots = 5;
            public int BotHealth = 100;
            public int Radius = 100;
            public List<string> Kit = new List<string>();
            public string BotNamePrefix = String.Empty;
            public List<string> BotNames = new List<string>();
            public int Bot_Accuracy = 4;
			public int Min_Bot_Accuracy = 4;
			public int Max_Bot_Accuracy = 4;
            public float Bot_Damage = 0.4f;
            public bool Disable_Radio = true;
            public int Roam_Range = 40;
            public bool Peace_Keeper = true;
            public int Peace_Keeper_Cool_Down = 5;
            public int Weapon_Drop_Chance = 100;
            public bool Keep_Default_Loadout;
            public bool Wipe_Belt = true;
            public bool Wipe_Clothing = true;
            public bool Allow_Rust_Loot = true;
            public int Suicide_Timer = 300;
            public bool Chute;
            public int Respawn_Timer = 60;
            public int Sci_Aggro_Range = 30;
            public int Sci_DeAggro_Range = 40;
        }

        public class DataProfile
        {
            public bool AutoSpawn;
            public bool Murderer;
            public int Bots = 5;
            public int BotHealth = 100;
            public int Radius = 100;
            public List<string> Kit = new List<string>();
            public string BotNamePrefix = String.Empty;
            public List<string> BotNames = new List<string>();
            public int Bot_Accuracy = 4;
			public int Min_Bot_Accuracy = 4;
			public int Max_Bot_Accuracy = 4;
            public float Bot_Damage = 0.4f;
            public bool Disable_Radio = true;
            public int Roam_Range = 40;
            public bool Peace_Keeper = true;
            public int Peace_Keeper_Cool_Down = 5;
            public int Weapon_Drop_Chance = 100;
            public bool Keep_Default_Loadout;
            public bool Wipe_Belt = true;
            public bool Wipe_Clothing = true;
            public bool Allow_Rust_Loot = true;
            public int Suicide_Timer = 300;
            public bool Chute;
            public int Respawn_Timer = 60;
            public float LocationX;
            public float LocationY;
            public float LocationZ;
            public string Parent_Monument = String.Empty;
            public int Sci_Aggro_Range = 30;
            public int Sci_DeAggro_Range = 40;
        }

        public class ProfileRelocation
        {
            public float OldParentMonumentX;
            public float OldParentMonumentY;
            public float OldParentMonumentZ;
            public float ParentMonumentX;
            public float ParentMonumentY;
            public float ParentMonumentZ;
            public float oldRotation;
            public float worldRotation;
        }

        class ConfigData
        {
            public Global Global = new Global();
            public Monuments Monuments = new Monuments();
            public Biomes Biomes = new Biomes();
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();
            SaveConfig(configData);
        }
        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            var config = new ConfigData();
            SaveConfig(config);
        }

        void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }
        #endregion

        #region Messages     
        Dictionary<string, string> Messages = new Dictionary<string, string>()
        {
            {"Title", "BotSpawn : " },
            {"error", "/botspawn commands are - list - add - remove - move - toplayer" },
            {"customsaved", "Custom Location Saved @ {0}" },
            {"custommoved", "Custom Location {0} has been moved to your current position." },
            {"alreadyexists", "Custom Location already exists with the name {0}." },
            {"customremoved", "Custom Location {0} Removed." },
            {"deployed", "'{0}' bots deployed to {1}." },
            {"ListTitle", "Custom Locations" },
            {"noprofile", "There is no profile by that name in config or data BotSpawn.json files." },
            {"namenotfound", "Player '{0}' was not found" },
            {"nokits", "Kits is not installed but you have declared custom kits at {0}." },
            {"noWeapon", "A bot at {0} has no weapon. Check your kits." },
            {"numberOfBot", "There is {0} spawned bot alive." },
            {"numberOfBots", "There are {0} spawned bots alive." },
            {"dupID", "Duplicate userID save attempted. Please notify author." },
            {"noSpawn", "Failed to find spawnpoints at {0}." }
        };
        #endregion

        #region ExternalHooks
        private Dictionary<string, List<ulong>> BotSpawnBots()
        {
            var BotSpawnBots = new Dictionary<string, List<ulong>>();

            foreach (var monument in AllProfiles.Where(monument => monument.Value.AutoSpawn))
                BotSpawnBots.Add(monument.Key, new List<ulong>());

            foreach (var bot in NPCPlayers)
            {
                var bData = bot.Value.GetComponent<BotData>();
                if (BotSpawnBots.ContainsKey(bData.monumentName))
                    BotSpawnBots[bData.monumentName].Add(bot.Key);
            }
            return BotSpawnBots;
        }

        private string[] AddGroupSpawn(Vector3 location, string profileName, string group)
        {
            if (location == new Vector3() || profileName == null || group == null)
                return new string[] { "error", "Null parameter" };
            string lowerProfile = profileName.ToLower();

            foreach (var entry in AllProfiles)
            {
                if (entry.Key.ToLower() == lowerProfile)
                {
                    var profile = entry.Value;
                    AttackPlayer(location, entry.Key, profile, group.ToLower());
                    return new string[] { "true", "Group successfully added" };
                }
            }
            return new string[] { "false", "Group add failed - Check profile name and try again" };
        }

        private string[] RemoveGroupSpawn(string group)
        {
            if (group == null)
                return new string[] { "error", "No group specified." };

            List<NPCPlayerApex> toDestroy = new List<NPCPlayerApex>();
            foreach (var bot in NPCPlayers)
            {
                if (bot.Value == null)
                    continue;
                var bData = bot.Value.GetComponent<BotData>();
                if (bData.group == group.ToLower())
                    toDestroy.Add(bot.Value);
            }
            if (toDestroy.Count == 0)
                return new string[] { "true", $"There are no bots belonging to {group}" };
            foreach (var killBot in toDestroy)
            {
                UpdateRecords(killBot);
                killBot.Kill();
            }
            return new string[] { "true", $"Group {group} was destroyed." };

        }

        private string[] CreateNewProfile(string name, string profile)
        {
            if (name == null)
                return new string[] { "error", "No name specified." };
            if (profile == null)
                return new string[] { "error", "No profile settings specified." };

            DataProfile newProfile = JsonConvert.DeserializeObject<DataProfile>(profile);

            if (storedData.DataProfiles.ContainsKey(name))
            {
                storedData.DataProfiles[name] = newProfile;
                AllProfiles[name] = newProfile;
                return new string[] { "true", $"Profile {name} was updated" };
            }

            storedData.DataProfiles.Add(name, newProfile);
            SaveData();
            AllProfiles.Add(name, newProfile);
            return new string[] { "true", $"New Profile {name} was created." };
        }

        private string[] ProfileExists(string name)
        {
            if (name == null)
                return new string[] { "error", "No name specified." };

            if (AllProfiles.ContainsKey(name))
                return new string[] { "true", $"{name} exists." };

            return new string[] { "false", $"{name} Does not exist." };
        }

        private string[] RemoveProfile(string name)
        {
            if (name == null)
                return new string[] { "error", "No name specified." };

            if (storedData.DataProfiles.ContainsKey(name))
            {
                foreach (var bot in NPCPlayers)
                {
                    if (bot.Value == null)
                        continue;

                    var bData = bot.Value.GetComponent<BotData>();
                    if (bData.monumentName == name)
                        bot.Value.Kill();
                }
                AllProfiles.Remove(name);
                storedData.DataProfiles.Remove(name);
                SaveData();
                return new string[] { "true", $"Profile {name} was removed." };
            }
            else
                return new string[] { "false", $"Profile {name} Does Not Exist." };
        }
        #endregion
    }
}