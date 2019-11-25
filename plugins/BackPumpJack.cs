using Newtonsoft.Json;
using Oxide.Core;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Back Pump Jack", "Arainrr", "1.2.1")]
    [Description("Obtain oil crater using survey charge.")]
    internal class BackPumpJack : RustPlugin
    {
        private List<OilInfo> oils = new List<OilInfo>();
        private ItemDefinition oil;
        private bool changed;

        private void Init()
        {
            foreach (var x in config.list)
            {
                if (!permission.PermissionExists(x.Per, this))
                    permission.RegisterPermission(x.Per, this);
            }
        }

        private void Unload() => SaveData();

        private void OnNewSave(string filename) => ClearData();

        private void OnServerSave()
        {
            if (changed)
            {
                changed = false;
                SaveData();
            }
        }

        private void OnServerInitialized()
        {
            LoadData();
            oil = ItemManager.itemList.Find(x => x.shortname == "crude.oil");
            foreach (var quarry in UnityEngine.Object.FindObjectsOfType<MiningQuarry>())
            {
                foreach (var x in oils)
                {
                    if (Vector3.Distance(x.pos, quarry.transform.position) < 2f)
                    {
                        AddResource(x);
                    }
                }
            }
            DeleteInvalidData();
        }

        private void DeleteInvalidData()
        {
            for (int i = 0; i < oils.Count(); i++)
            {
                var list = new List<MiningQuarry>();
                Vis.Entities(oils[i].pos, 1f, list);
                if (list.Count() <= 0)
                    oils.Remove(oils[i]);
            }
            SaveData();
        }

        private OilConfigInfo HasPermission(BasePlayer player)
        {
            OilConfigInfo per = new OilConfigInfo();
            int i = 0;
            foreach (var x in config.list)
            {
                if (permission.UserHasPermission(player.UserIDString, x.Per))
                {
                    if (x.Priority >= i)
                    {
                        i = x.Priority;
                        per = x;
                    }
                }
            }
            return per;
        }

        private Dictionary<BaseEntity, OilConfigInfo> wait = new Dictionary<BaseEntity, OilConfigInfo>();

        private void OnExplosiveThrown(BasePlayer player, BaseEntity entity)
        {
            if (!(entity is SurveyCharge)) return;
            OilConfigInfo info = HasPermission(player);
            if (info == null || info.Chance <= 0) return;
            wait.Add(entity, info);
        }

        private HashSet<SurveyCrater> readed = new HashSet<SurveyCrater>();

        private void OnEntityKill(BaseEntity entity)
        {
            if (!(entity is SurveyCharge)) return;
            OilConfigInfo info = new OilConfigInfo();
            if (wait.TryGetValue(entity, out info))
            {
                Vector3 Pos = entity.transform.position;
                NextTick(() =>
                {
                    var list = new List<SurveyCrater>();
                    Vis.Entities(Pos, 1f, list);
                    if (list.Count() > 0)
                    {
                        foreach (var survey in list)
                        {
                            if (readed.Contains(survey)) continue;
                            if (UnityEngine.Random.Range(0, 100) < info.Chance)
                            {
                                survey.Kill();

                                SurveyCrater oilEntity = GameManager.server.CreateEntity("assets/prefabs/tools/surveycharge/survey_crater_oil.prefab", survey.transform.position) as SurveyCrater;
                                if (oilEntity == null || oil == null) continue;
                                oilEntity.Spawn();

                                var res = ResourceDepositManager.GetOrCreate(oilEntity.transform.position);
                                if (res == null) continue;
                                res._resources.Clear();
                                float pm = UnityEngine.Random.Range(info.PMMin, info.PMMax);
                                float workNeeded = 45f / pm;
                                int amount = UnityEngine.Random.Range(50000, 100000);
                                res.Add(oil, 1, amount, workNeeded, ResourceDepositManager.ResourceDeposit.surveySpawnType.OIL, true);
                                oils.Add(new OilInfo(Pos, amount, workNeeded));
                                changed = true;
                                readed.Add(oilEntity);
                            }
                            if (!survey.IsDestroyed)
                                readed.Add(survey);
                        }
                    }
                });
                wait.Remove(entity);
            }
        }

        private void AddResource(OilInfo info)
        {
            var res = ResourceDepositManager.GetOrCreate(info.pos);
            if (res == null || oil == null) return;
            res._resources.Clear();
            res.Add(oil, 1, info.amount, info.workNeeded, ResourceDepositManager.ResourceDeposit.surveySpawnType.OIL, true);
        }

        private class OilInfo
        {
            public Vector3 pos;
            public int amount;
            public float workNeeded;

            public OilInfo(Vector3 p, int a, float w)
            {
                pos = p;
                amount = a;
                workNeeded = w;
            }
        }

        #region DataFile

        private void LoadData()
        {
            try
            {
                oils = Interface.Oxide.DataFileSystem.ReadObject<List<OilInfo>>(this.Name);
            }
            catch
            {
                ClearData();
            }
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(this.Name, oils);
        }

        private void ClearData()
        {
            oils = new List<OilInfo>();
            SaveData();
        }

        #endregion DataFile

        #region Configuration

        private ConfigFile config;

        private class ConfigFile
        {
            [JsonProperty(PropertyName = "List")]
            public List<OilConfigInfo> list;

            public static ConfigFile DefaultConfig()
            {
                return new ConfigFile
                {
                    list = new List<OilConfigInfo>
                    {
                        new OilConfigInfo
                        {
                            Per = "backpumpjack.use",
                            Priority = 0,
                            Chance = 20f,
                            PMMin = 5f,
                            PMMax = 10f
                        },
                        new OilConfigInfo
                        {
                            Per = "backpumpjack.vip",
                            Priority = 1,
                            Chance = 40f,
                            PMMin = 10f,
                            PMMax = 20f
                        }
                    }
                };
            }
        }

        private class OilConfigInfo
        {
            [JsonProperty(PropertyName = "Permission")]
            public string Per;

            [JsonProperty(PropertyName = "Priority of permission")]
            public int Priority;

            [JsonProperty(PropertyName = "Oil crater chance")]
            public float Chance;

            [JsonProperty(PropertyName = "Minimum PM size")]
            public float PMMin;

            [JsonProperty(PropertyName = "Maximum PM size")]
            public float PMMax;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigFile>();
                if (config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                PrintError("The configuration file is corrupted");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            config = ConfigFile.DefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        #endregion Configuration
    }
}