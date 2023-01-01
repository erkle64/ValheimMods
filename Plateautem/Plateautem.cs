using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Plateautem
{
    public class Plateautem : MonoBehaviour, Interactable, Hoverable
    {
        private static TerrainOp flattenTerrainOp;
        private EffectList flattenPlaceEffect = null;

        [SerializeField] private CircleProjector circleProjector = null;
        [SerializeField] private GameObject enabledEffect = null;
        [SerializeField] private MeshRenderer model = null;
        private ZNetView netView = null;

        [SerializeField] private Transform lightTransform = null;
        [SerializeField] private Transform flareTransform = null;
        [SerializeField] private Transform droneAudioSourceTransform = null;
        [SerializeField] private AudioSource droneAudioSource = null;
        private float targetDroneAudioVolume = 0.0f;

        private struct FuelItem
        {
            public string prefabName;
            public string displayName;
            public float fuelValue;
        }

        private static List<FuelItem> fuelItems = new List<FuelItem>();

        private const string stoneItemPrefabName = "Stone";
        private const string stoneItemDisplayName = "$item_stone";

        private static string[] lowerToolPrefabNames = new string[] {
            "PickaxeAntler", "PickaxeBronze", "PickaxeIron", "PickaxeBlackMetal"
        };
        private static string[] lowerToolDisplayNames = new string[] {
            "$item_pickaxe_antler", "$item_pickaxe_bronze", "$item_pickaxe_iron", "$item_pickaxe_blackmetal"
        };

        private static string[] raiseToolPrefabNames = new string[] { "Hoe" };
        private static string[] raiseToolDisplayNames = new string[] { "$item_hoe" };

        private const float placementSpacing = 2.6f;
        private const float circlePadding = 1.0f;

        private int currentFuelItemIndex = 0;
        private float lastUseTime;
        private float previousScanTime = 0.0f;
        private float previousScanTime2 = 0.0f;
        private float previousScanProgress = 0.0f;
        private float targetScanProgress = 0.0f;

        private static ConfigEntry<string> configFuelItems;
        private static ConfigEntry<float> configFuelPerScan;
        private static ConfigEntry<float> configFuelPerRaise;
        private static ConfigEntry<float> configFuelPerLower;
        private static ConfigEntry<int> configMaximumFuel;
        private static ConfigEntry<float> configStonePerRaise;
        private static ConfigEntry<float> configStonePerLower;
        private static ConfigEntry<int> configMaximumStone;
        private static ConfigEntry<float> configDefaultFlatteningRadius;
        private static ConfigEntry<float> configMaximumFlatteningRadius;
        private static ConfigEntry<float> configMinFlatteningTime;
        private static ConfigEntry<float> configMaxFlatteningTime;
        private static ConfigEntry<float> configScanningTime;
        private static ConfigEntry<bool> configDoPainting;
        private static ConfigEntry<bool> configRequireLowerTool;
        private static ConfigEntry<bool> configRequireRaiseTool;
        private static ConfigEntry<float>[] configLowerToolBonus;
        private static ConfigEntry<float>[] configRaiseToolBonus;
        private static ConfigEntry<KeyboardShortcut> configIncreaseRadiusKey;
        private static ConfigEntry<KeyboardShortcut> configDecreaseRadiusKey;
        private static ConfigEntry<KeyboardShortcut> configResetScanKey;
        private static ConfigEntry<KeyboardShortcut> configEjectFuelKey;
        private static ConfigEntry<KeyboardShortcut> configEjectStoneKey;
        private static ConfigEntry<KeyboardShortcut> configEjectToolsKey;
        private static ConfigEntry<bool> configShowMainKeys;
        private static ConfigEntry<bool> configShowExtraKeys;
        private static ConfigEntry<bool> configShowFillBars;
        private static ConfigEntry<bool> configShowFillNumbers;
        private static ConfigEntry<bool> configShowTools;
        private static ConfigEntry<bool> configShowSelection;

        public const string msgNoFuel = "$piece_plateautem_noFuel";
        public const string msgHold = "$piece_plateautem_hold";
        public const string msgAll = "$piece_plateautem_all";
        public const string msgFuel = "$piece_plateautem_fuel";
        public const string msgTools = "$piece_plateautem_tools";
        public const string msgRadius = "$piece_plateautem_radius";
        public const string msgResetScan = "$piece_plateautem_reset";
        public const string msgSelectFuel = "$piece_plateautem_selectFuel";
        public const string msgEject = "$piece_plateautem_eject";
        public const string msgEjectFuel = "$piece_plateautem_ejectFuel";
        public const string msgEjectStone = "$piece_plateautem_ejectStone";
        public const string msgSelectMode = "$piece_plateautem_selectMode";

        private const string zdonCurrentRadius = "current_radius";
        private int zdoidCurrentRadius;
        private float currentRadius
        {
            get
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return configDefaultFlatteningRadius.Value;
                return netView.GetZDO().GetFloat(zdoidCurrentRadius, configDefaultFlatteningRadius.Value);
            }
            set
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return;
                netView.GetZDO().Set(zdoidCurrentRadius, value);
            }
        }

        private const string zdonFuelStored = "fuel_stored";
        private int zdoidFuelStored;
        private float currentFuelStored
        {
            get
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return 0;
                return netView.GetZDO().GetFloat(zdoidFuelStored, 0);
            }
            set
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return;
                netView.GetZDO().Set(zdoidFuelStored, value);
            }
        }

        private const string zdonStoneStored = "stone_stored";
        private int zdoidStoneStored;
        private float currentStoneStored
        {
            get
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return 0;
                return netView.GetZDO().GetFloat(zdoidStoneStored, 0);
            }
            set
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return;
                netView.GetZDO().Set(zdoidStoneStored, value);
            }
        }

        private const string zdonScanProgress = "scan_progress";
        private int zdoidScanProgress;
        private float currentScanProgress
        {
            get
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return 0;
                return netView.GetZDO().GetFloat(zdoidScanProgress, 0.0f);
            }
            set
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return;
                netView.GetZDO().Set(zdoidScanProgress, value);
            }
        }


        private const string zdonScanIndex = "scan_index";
        private int zdoidScanIndex;
        private int currentScanIndex
        {
            get
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return 0;
                return netView.GetZDO().GetInt(zdoidScanIndex, 0);
            }
            set
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return;
                netView.GetZDO().Set(zdoidScanIndex, value);
            }
        }

        private const string zdonScanSpeed = "scan_speed";
        private int zdoidScanSpeed;
        private float currentScanSpeed
        {
            get
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return 0;
                return netView.GetZDO().GetFloat(zdoidScanSpeed, (configScanningTime.Value <= float.Epsilon) ? 1.0f : 1.0f / configScanningTime.Value);
            }
            set
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return;
                netView.GetZDO().Set(zdoidScanSpeed, value);
            }
        }

        private const string zdonLowerToolIndex = "lower_tool_index";
        private int zdoidLowerToolIndex;
        private int currentLowerToolIndex
        {
            get
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return -1;
                return netView.GetZDO().GetInt(zdoidLowerToolIndex, -1);
            }
            set
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return;
                netView.GetZDO().Set(zdoidLowerToolIndex, value);
            }
        }

        private const string zdonRaiseToolIndex = "raise_item_index";
        private int zdoidRaiseToolIndex;
        private int currentRaiseToolIndex
        {
            get
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return -1;
                return netView.GetZDO().GetInt(zdoidRaiseToolIndex, -1);
            }
            set
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return;
                netView.GetZDO().Set(zdoidRaiseToolIndex, value);
            }
        }

        private string CurrentFuelItemName 
        {
            get
            {
                if (currentFuelItemIndex < 0) return stoneItemDisplayName;

                if (fuelItems == null || fuelItems.Count == 0) return "";

                return GetFuelItemDisplayName(currentFuelItemIndex);
            }
        }

        private string CurrentLowerToolName
        {
            get
            {
                if (currentLowerToolIndex < 0) return "";
                return lowerToolDisplayNames[currentLowerToolIndex];
            }
        }

        private string CurrentRaiseToolName
        {
            get
            {
                if (currentRaiseToolIndex < 0) return "";
                return raiseToolDisplayNames[currentRaiseToolIndex];
            }
        }

        private static CustomRPC rpcLevelTerrain;
        public static void RegisterRPCs()
        {
            rpcLevelTerrain = NetworkManager.Instance.AddRPC("LevelTerrain", RPCS_LevelTerrain, null);
        }

        public static void LoadConfig(BaseUnityPlugin plugin)
        {
            configFuelItems = plugin.Config.Bind("Server", "Fuel Item List", defaultValue: "Wood=1,Coal=2.5,Resin=5", new ConfigDescription("Prefab name and fuel value list.", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configFuelPerScan = plugin.Config.Bind("Server", "Fuel Per Scan", defaultValue: 0.01f, new ConfigDescription("Amount of fuel to use for each scan action.", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configFuelPerRaise = plugin.Config.Bind("Server", "Fuel Per Raise", defaultValue: 0.01f, new ConfigDescription("Amount of fuel to use for raising terrain.", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configFuelPerLower = plugin.Config.Bind("Server", "Fuel Per Lower", defaultValue: 0.01f, new ConfigDescription("Amount of fuel to use for lowering terrain.", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configMaximumFuel = plugin.Config.Bind("Server", "Maximum Fuel", defaultValue: 250, new ConfigDescription("Maximum amount of fuel stored in the totem.", new AcceptableValueRange<int>(1, 10000), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configStonePerRaise = plugin.Config.Bind("Server", "Stone Per Raise", defaultValue: 0.05f, new ConfigDescription("Amount of stone to use for raising terrain.", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configStonePerLower = plugin.Config.Bind("Server", "Stone Per Lower", defaultValue: 0.05f, new ConfigDescription("Amount of stone gained from lowering terrain.", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configMaximumStone = plugin.Config.Bind("Server", "Maximum Stone", defaultValue: 250, new ConfigDescription("Maximum amount of stone stored in the totem.", new AcceptableValueRange<int>(1, 10000), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configDefaultFlatteningRadius = plugin.Config.Bind("Server", "Default Flattening Radius", defaultValue: 10.0f, new ConfigDescription("Default radius of the area to be flattened.", new AcceptableValueRange<float>(2.0f, 100.0f), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configMaximumFlatteningRadius = plugin.Config.Bind("Server", "Maximum Flattening Radius", defaultValue: 40.0f, new ConfigDescription("Maximum radius of the area to be flattened.", new AcceptableValueRange<float>(2.0f, 100.0f), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configMinFlatteningTime = plugin.Config.Bind("Server", "Min Flattening Time", defaultValue: 0.2f, new ConfigDescription("Time taken for a flattening action with maximum bonus.", new AcceptableValueRange<float>(0.1f, 60.0f), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configMaxFlatteningTime = plugin.Config.Bind("Server", "Max Flattening Time", defaultValue: 1.0f, new ConfigDescription("Time taken for a flattening action with no bonus.", new AcceptableValueRange<float>(0.1f, 60.0f), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configScanningTime = plugin.Config.Bind("Server", "Scanning Time", defaultValue: 0.2f, new ConfigDescription("Time taken for a scanning action.", new AcceptableValueRange<float>(0.1f, 60.0f), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configDoPainting = plugin.Config.Bind("Server", "Do Painting", defaultValue: true, new ConfigDescription("Paint dirt onto terrain.", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configRequireLowerTool = plugin.Config.Bind("Server", "Require Lower Tool", defaultValue: true, new ConfigDescription("Require pickaxe to lower terrain.", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configRequireRaiseTool = plugin.Config.Bind("Server", "Require Raise Tool", defaultValue: true, new ConfigDescription("Require hoe to raise terrain.", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));

            configLowerToolBonus = new ConfigEntry<float>[lowerToolPrefabNames.Length];
            for (int i = 0; i < lowerToolPrefabNames.Length; i++)
            {
                configLowerToolBonus[i] = plugin.Config.Bind("Server", $"{lowerToolPrefabNames[i]} Bonus", defaultValue: i/(float)(lowerToolPrefabNames.Length > 1 ? lowerToolPrefabNames.Length - 1 : 1), new ConfigDescription($"Bonus applied for {lowerToolPrefabNames[i]}. 0 = Max Flattening Time. 1 = Min Flattening Time.", new AcceptableValueRange<float>(0.0f, 1.0f), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            }

            configRaiseToolBonus = new ConfigEntry<float>[raiseToolPrefabNames.Length];
            for (int i = 0; i < raiseToolPrefabNames.Length; i++)
            {
                configRaiseToolBonus[i] = plugin.Config.Bind("Server", $"{raiseToolPrefabNames[i]} Bonus", defaultValue: i / (float)(raiseToolPrefabNames.Length > 1 ? lowerToolPrefabNames.Length - 1 : 1), new ConfigDescription($"Bonus applied for {raiseToolPrefabNames[i]}. 0 = Max Flattening Time. 1 = Min Flattening Time.", new AcceptableValueRange<float>(0.0f, 1.0f), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            }

            configIncreaseRadiusKey = plugin.Config.Bind("Input", "Increase flattening radius", new KeyboardShortcut(KeyCode.KeypadPlus));
            configDecreaseRadiusKey = plugin.Config.Bind("Input", "Decrease flattening radius", new KeyboardShortcut(KeyCode.KeypadMinus));
            configResetScanKey = plugin.Config.Bind("Input", "Reset scan position", new KeyboardShortcut(KeyCode.KeypadEnter));
            configEjectFuelKey = plugin.Config.Bind("Input", "Eject fuel", new KeyboardShortcut(KeyCode.KeypadDivide));
            configEjectStoneKey = plugin.Config.Bind("Input", "Eject stone", new KeyboardShortcut(KeyCode.KeypadMultiply));
            configEjectToolsKey = plugin.Config.Bind("Input", "Eject tools", new KeyboardShortcut(KeyCode.KeypadPeriod));

            configShowMainKeys = plugin.Config.Bind("Client", "Show Main Keys", defaultValue: true, new ConfigDescription("Show main keys in hover text.", null, new ConfigurationManagerAttributes { IsAdminOnly = false }));
            configShowExtraKeys = plugin.Config.Bind("Client", "Show Extra Keys", defaultValue: true, new ConfigDescription("Show extra keys in hover text.", null, new ConfigurationManagerAttributes { IsAdminOnly = false }));
            configShowFillBars = plugin.Config.Bind("Client", "Show Fill Bars", defaultValue: true, new ConfigDescription("Show fill bars in hover text.", null, new ConfigurationManagerAttributes { IsAdminOnly = false }));
            configShowFillNumbers = plugin.Config.Bind("Client", "Show Fill Numbers", defaultValue: true, new ConfigDescription("Show fill numbers in hover text.", null, new ConfigurationManagerAttributes { IsAdminOnly = false }));
            configShowTools = plugin.Config.Bind("Client", "Show Tools", defaultValue: true, new ConfigDescription("Show tools in hover text.", null, new ConfigurationManagerAttributes { IsAdminOnly = false }));
            configShowSelection = plugin.Config.Bind("Client", "Show Selection", defaultValue: true, new ConfigDescription("Show selection in hover text.", null, new ConfigurationManagerAttributes { IsAdminOnly = false }));

            configDefaultFlatteningRadius.SettingChanged += OnConfigChanged;
            configMaximumFlatteningRadius.SettingChanged += OnConfigChanged;
            configFuelItems.SettingChanged += OnFuelItemsChanged;

            UpdateConfig();
        }

        public static void OnConfigChanged(object sender, System.EventArgs eventArgs) => UpdateConfig();
        public static void OnFuelItemsChanged(object sender, System.EventArgs eventArgs) => LoadFuelItems();

        public static void UpdateConfig()
        {
            foreach (var plateautem in FindObjectsOfType<Plateautem>())
            {
                plateautem.SettingsUpdated();
            }
        }

        public static void LoadFuelItems()
        {
            fuelItems.Clear();

            var fuelItemRegex = new Regex(@"\s*([^=,\s]+)(?:\s*\=\s*(\d+(?:\.\d+)?))?\s*(?:,|$)");
            var fuelItemMatches = fuelItemRegex.Matches(configFuelItems.Value);
            if (fuelItemMatches.Count > 0)
            {
                for (int i = 0; i < fuelItemMatches.Count; ++i)
                {
                    var match = fuelItemMatches[i];
                    if (match.Groups.Count >= 2)
                    {
                        string fuelItemPrefabName = match.Groups[1].Value;
                        float fuelItemValue = 1.0f;
                        if (match.Groups.Count >= 3 && !string.IsNullOrWhiteSpace(match.Groups[2].Value))
                        {
                            fuelItemValue = float.Parse(match.Groups[2].Value);
                        }
                        fuelItems.Add(new FuelItem
                        {
                            prefabName = fuelItemPrefabName,
                            displayName = null,
                            fuelValue = fuelItemValue
                        });
                        Jotunn.Logger.LogInfo($"Adding fuel item {fuelItemPrefabName} with value {fuelItemValue} to Plateautem.");
                    }
                }
            }

            if (fuelItems.Count == 0)
            {
                fuelItems.Add(new FuelItem
                {
                    prefabName = "Wood",
                    displayName = "$item_wood",
                    fuelValue = 1.0f
                });
            }
        }

        private void Awake()
        {
            netView = GetComponent<ZNetView>();

            zdoidCurrentRadius = zdonCurrentRadius.GetStableHashCode();
            zdoidFuelStored = zdonFuelStored.GetStableHashCode();
            zdoidStoneStored = zdonStoneStored.GetStableHashCode();
            zdoidScanProgress = zdonScanProgress.GetStableHashCode();
            zdoidScanIndex = zdonScanIndex.GetStableHashCode();
            zdoidScanSpeed = zdonScanSpeed.GetStableHashCode();
            zdoidLowerToolIndex = zdonLowerToolIndex.GetStableHashCode();
            zdoidRaiseToolIndex = zdonRaiseToolIndex.GetStableHashCode();

            if (flattenTerrainOp == null || !flattenTerrainOp)
            {
                var flattenGO = PrefabManager.Instance.GetPrefab("plateautem_flatten_op");
                if (flattenGO == null)
                {
                    flattenGO = PrefabManager.Instance.CreateClonedPrefab("plateautem_flatten_op", "mud_road_v2");
                    flattenTerrainOp = flattenGO.GetComponent<TerrainOp>();

                    flattenTerrainOp.m_settings.m_levelOffset = 0.0f;

                    flattenTerrainOp.m_settings.m_level = true;
                    flattenTerrainOp.m_settings.m_levelRadius = 4.0f;
                    flattenTerrainOp.m_settings.m_square = false;

                    flattenTerrainOp.m_settings.m_raise = false;
                    flattenTerrainOp.m_settings.m_raiseRadius = 3.0f;
                    flattenTerrainOp.m_settings.m_raisePower = 3.0f;
                    flattenTerrainOp.m_settings.m_raiseDelta = 0.0f;

                    flattenTerrainOp.m_settings.m_smooth = false;
                    flattenTerrainOp.m_settings.m_smoothRadius = 3.0f;
                    flattenTerrainOp.m_settings.m_smoothPower = 3.0f;

                    flattenTerrainOp.m_settings.m_paintCleared = configDoPainting.Value;
                    flattenTerrainOp.m_settings.m_paintHeightCheck = false;
                    flattenTerrainOp.m_settings.m_paintType = TerrainModifier.PaintType.Dirt;
                    flattenTerrainOp.m_settings.m_paintRadius = 2.5f;
                }
                else
                {
                    flattenTerrainOp = flattenGO.GetComponent<TerrainOp>();
                }
            }

            flattenPlaceEffect = new EffectList
            {
                m_effectPrefabs = new EffectList.EffectData[]
                {
                    new EffectList.EffectData { m_prefab = PrefabManager.Instance.GetPrefab("vfx_Place_mud_road") },
                    new EffectList.EffectData { m_prefab = PrefabManager.Instance.GetPrefab("sfx_build_hoe") },
                }
            };

            WearNTear wearNTear = GetComponent<WearNTear>();
            wearNTear.m_onDestroyed = (System.Action)System.Delegate.Combine(wearNTear.m_onDestroyed, new System.Action(OnDestroyed));

            if (netView != null)
            {
                netView.Register<int, int>("AddFuelItem", RPC_AddFuelItem);
                netView.Register<ZPackage>("AddLowerTool", RPC_AddLowerTool);
                netView.Register<ZPackage>("AddRaiseTool", RPC_AddRaiseTool);
                netView.Register<int>("AddStone", RPC_AddStone);
                netView.Register<float>("ChangeRadius", RPC_ChangeRadius);
                netView.Register("ResetScan", RPC_ResetScan);
                netView.Register("EjectFuel", RPC_EjectFuel);
                netView.Register("EjectStone", RPC_EjectStone);
                netView.Register("EjectTools", RPC_EjectTools);
            }

            UpdateCircle(currentRadius);
        }

        private void OnDestroyed()
        {
            EjectFuel(false);
            EjectStone(false);
            EjectTools(true, true);
        }

        public void RPC_AddFuelItem(long sender, int fuelItemIndex, int fuelItemsToAdd)
        {
            if (!netView.IsOwner()) return;

            SetFuelItemStorage(fuelItemIndex, GetFuelItemStorage(fuelItemIndex) + fuelItemsToAdd);
        }

        public void RPC_AddLowerTool(long sender, ZPackage package)
        {
            if (!netView.IsOwner()) return;

            EjectTools(true, false);

            currentLowerToolIndex = package.ReadInt();
            ReadItemToZDO(1, package, netView.GetZDO());
        }

        public void RPC_AddRaiseTool(long sender, ZPackage package)
        {
            if (!netView.IsOwner()) return;

            EjectTools(false, true);

            currentRaiseToolIndex = package.ReadInt();
            ReadItemToZDO(2, package, netView.GetZDO());
        }

        public void RPC_AddStone(long sender, int stoneToAdd)
        {
            if (!netView.IsOwner()) return;

            currentStoneStored += stoneToAdd;
        }

        public void RPC_ChangeRadius(long sender, float delta)
        {
            if (!netView.IsOwner()) return;

            currentRadius = Mathf.Clamp(currentRadius + delta, 1.0f, configMaximumFlatteningRadius.Value);
        }

        public void RPC_ResetScan(long sender)
        {
            if (!netView.IsOwner()) return;

            currentScanProgress = 0.0f;
            currentScanIndex = 0;
            currentScanSpeed = 1.0f / configScanningTime.Value;
        }

        public void RPC_EjectFuel(long sender)
        {
            if (!netView.IsOwner()) return;

            EjectFuel(true);
        }

        public void RPC_EjectStone(long sender)
        {
            if (!netView.IsOwner()) return;

            EjectStone(true);
        }

        public void RPC_EjectTools(long sender)
        {
            if (!netView.IsOwner()) return;

            EjectTools(true, true);
        }

        private static IEnumerator RPCS_LevelTerrain(long sender, ZPackage package)
        {
            var position = package.ReadVector3();

            while (true)
            {
                var zoneLoaded = false;
                var zoneId = ZoneSystem.instance.GetZone(position);
                for (int y = zoneId.y - 1; y <= zoneId.y + 1; y++)
                {
                    for (int x = zoneId.x - 1; x <= zoneId.x + 1; x++)
                    {
                        if (ZoneSystem.instance.PokeLocalZone(zoneId))
                        {
                            zoneLoaded = true;
                            break;
                        }
                    }
                }

                if (!zoneLoaded)
                {
                    Instantiate(flattenTerrainOp.gameObject, position, Quaternion.identity);
                    yield break;
                }

                yield return null;
            }
        }

        private void Update()
        {
            if (!netView || !netView.IsValid()) return;

            UpdateCircle(currentRadius);

            if (!netView.IsOwner())
            {
                UpdateOrb(false);
                return;
            }

            bool doLower = !configRequireLowerTool.Value || currentLowerToolIndex >= 0;
            bool doRaise = !configRequireRaiseTool.Value || currentRaiseToolIndex >= 0;
            if (doLower || doRaise)
            {
                int scanPointCount = CountPointsInSpiral(currentRadius + placementSpacing, placementSpacing);
                if (currentScanProgress > scanPointCount)
                {
                    currentScanProgress = 0.0f;
                    currentScanIndex = 0;
                    currentScanSpeed = 1.0f / configScanningTime.Value;
                }

                if (currentScanProgress >= currentScanIndex)
                {
                    float scanAngle, scanRadius;
                    PolarPointOnSpiral(currentScanProgress, placementSpacing, out scanAngle, out scanRadius);
                    var radius = Mathf.Min(scanRadius, currentRadius);
                    var scanOffset = new Vector3(Mathf.Sin(scanAngle) * radius, 0.0f, Mathf.Cos(scanAngle) * radius);
                    var scanPosition = transform.position + scanOffset;
                    float totalRaise = 0.0f;
                    float totalLower = 0.0f;
                    float fuelRequired = 0.0f;
                    float stoneRequired = 0.0f;
                    float triggerVolume = 0.0f;
                    if (!IsInsideNoBuildLocation(scanPosition, 4.0f) && IsInLoadedArea(scanPosition, 5.0f))
                    {
                        const float sampleSpacing = 0.5f;
                        const float sampleSpacingSqr = sampleSpacing * sampleSpacing;
                        const float cefgw = 0.1f;
                        foreach (var groundPoint in EachGroundPointOnSpiral(scanPosition, 2.0f, sampleSpacing))
                        {
                            if (groundPoint.y > scanPosition.y + cefgw)
                            {
                                var volume = (groundPoint.y - scanPosition.y) * sampleSpacingSqr;
                                totalLower += volume;
                                fuelRequired += volume * configFuelPerLower.Value;
                                stoneRequired -= volume * configStonePerLower.Value;
                                triggerVolume += volume * Mathf.Pow(Mathf.Clamp01(1.0f - groundPoint.w / 2.0f), 0.3f);
                            }
                            else if (groundPoint.y < scanPosition.y - cefgw)
                            {
                                float volume = (scanPosition.y - groundPoint.y) * sampleSpacingSqr;
                                totalRaise += volume;
                                fuelRequired += volume * configFuelPerRaise.Value;
                                stoneRequired += volume * configStonePerRaise.Value;
                                triggerVolume += volume * Mathf.Pow(Mathf.Clamp01(1.0f - groundPoint.w / 2.0f), 0.3f);
                            }
                        }
                    }

                    float difference = totalRaise - totalLower;
                    bool doFlatten = triggerVolume > 0.75f && (doLower || difference > 0) && (doRaise || difference < 0);
                    if (doFlatten && stoneRequired > 0 && currentStoneStored < stoneRequired) doFlatten = false;
                    if (doFlatten && stoneRequired < 0 && (configMaximumStone.Value - currentStoneStored) < -stoneRequired) doFlatten = false;

                    float nextScanSpeed = 1.0f / configScanningTime.Value;
                    if (doFlatten)
                    {
                        fuelRequired += configFuelPerScan.Value;
                    }
                    else
                    {
                        fuelRequired = configFuelPerScan.Value;
                        stoneRequired = 0.0f;
                    }

                    bool freeby = triggerVolume < 0.1f;
                    var fuelStored = GetTotalFuelStored();
                    if (fuelStored >= fuelRequired)
                    {
                        ConsumeFuel(fuelRequired);
                        currentStoneStored -= stoneRequired;

                        if (doFlatten)
                        {
                            float bonus = 0.0f;
                            if (difference < 0.0f && currentLowerToolIndex >= 0)
                            {
                                bonus = configLowerToolBonus[currentLowerToolIndex].Value;
                            }
                            else if (difference > 0.0f && currentRaiseToolIndex >= 0)
                            {
                                bonus = configRaiseToolBonus[currentRaiseToolIndex].Value;
                            }
                            nextScanSpeed = 1.0f / (freeby ? configScanningTime.Value : Mathf.Lerp(configMaxFlatteningTime.Value, configMinFlatteningTime.Value, bonus));

                            Instantiate(flattenTerrainOp.gameObject, scanPosition, Quaternion.identity);

                            if (!freeby) flattenPlaceEffect?.Create(scanPosition, Quaternion.identity, transform);
                        }

                        targetDroneAudioVolume = 1.0f;

                        currentScanIndex++;

                        if (currentScanIndex < scanPointCount)
                        {
                            currentScanProgress += Time.deltaTime * currentScanSpeed;
                        }
                        else
                        {
                            currentScanIndex = 0;
                            currentScanProgress = 0.0f;
                        }
                    }
                    else
                    {
                        targetDroneAudioVolume = 0.0f;
                    }

                    currentScanSpeed = nextScanSpeed;
                }
                else
                {
                    currentScanProgress += Time.deltaTime * currentScanSpeed;
                }
            }

            UpdateOrb(true);
        }

        private void UpdateOrb(bool isOwner)
        {
            float scanProgress = currentScanProgress;
            if (!isOwner)
            {
                if (previousScanTime == 0.0f) previousScanTime = Time.time;
                if (scanProgress > targetScanProgress)
                {
                    previousScanTime2 = previousScanTime;
                    previousScanTime = Time.time;
                    previousScanProgress = targetScanProgress;
                    targetScanProgress = scanProgress;
                }
                else if (scanProgress < previousScanProgress)
                {
                    previousScanTime2 = previousScanTime;
                    previousScanTime = Time.time;
                    targetScanProgress = previousScanProgress = scanProgress;
                }
                var scanSpeed = (targetScanProgress - previousScanProgress) / Mathf.Max(0.001f, previousScanTime - previousScanTime2);
                scanProgress = Mathf.Lerp(previousScanProgress, targetScanProgress, Mathf.Clamp01((Time.time - previousScanTime) * scanSpeed));
            }

            float orbAngle, orbRadius;
            PolarPointOnSpiral(scanProgress, placementSpacing, out orbAngle, out orbRadius);
            orbRadius = Mathf.Min(orbRadius, currentRadius);
            var orbOffset = new Vector3(Mathf.Sin(orbAngle) * orbRadius, 0.5f, Mathf.Cos(orbAngle) * orbRadius);
            var orbPosition = transform.position + orbOffset;

            if (lightTransform != null) lightTransform.position = orbPosition;
            if (flareTransform != null) flareTransform.position = orbPosition;
            if (droneAudioSourceTransform != null) droneAudioSourceTransform.position = orbPosition;

            if (droneAudioSource != null)
            {
                if (!droneAudioSource.isPlaying)
                {
                    droneAudioSource.gameObject.SetActive(true);
                    droneAudioSource.enabled = true;
                    droneAudioSource.loop = true;
                    droneAudioSource.Play();
                }
                droneAudioSource.volume = Mathf.MoveTowards(droneAudioSource.volume, targetDroneAudioVolume, Time.deltaTime);
            }
        }

        private string GetFuelItemDisplayName(int index)
        {
            if (index == -1) return stoneItemDisplayName;

            var fuelItem = fuelItems[index];
            if (fuelItem.displayName == null)
            {
                var fuelItemPrefab = ObjectDB.instance.GetItemPrefab(fuelItem.prefabName);
                var fuelItemDrop = fuelItemPrefab?.GetComponent<ItemDrop>();
                if (fuelItemDrop != null)
                {
                    fuelItem.displayName = fuelItemDrop.m_itemData.m_shared.m_name;
                    fuelItems[index] = fuelItem;
                }
            }

            return fuelItem.displayName;
        }

        private static IEnumerable<Vector4> EachGroundPointOnSpiral(Vector3 origin, float radius, float sampleSpacing = 0.5f)
        {
            float sampleHeight;
            foreach (var offset in EachPointOnSpiral(radius, sampleSpacing))
            {
                var samplePosition = new Vector3(origin.x + offset.x, origin.y, origin.z + offset.y);
                if (ZoneSystem.instance.GetGroundHeight(samplePosition, out sampleHeight))
                {
                    yield return new Vector4(samplePosition.x, sampleHeight, samplePosition.z, offset.w);
                }
            }
        }

        public static IEnumerable<Vector4> EachPointOnSpiral(float radius, float sampleSpacing)
        {
            float sampleAngle, sampleRadius;
            int count = CountPointsInSpiral(radius, sampleSpacing);
            for (int i = 0; i < count; i++)
            {
                PolarPointOnSpiral(i, sampleSpacing, out sampleAngle, out sampleRadius);
                yield return new Vector4(Mathf.Sin(sampleAngle) * sampleRadius, Mathf.Cos(sampleAngle) * sampleRadius, sampleAngle, sampleRadius);
            }
        }

        public static IEnumerable<Vector2> EachPointOnSpiralPolar(float radius, float sampleSpacing)
        {
            float sampleAngle, sampleRadius;
            int count = CountPointsInSpiral(radius, sampleSpacing);
            for (int i = 0; i < count; i++)
            {
                PolarPointOnSpiral(i, sampleSpacing, out sampleAngle, out sampleRadius);
                yield return new Vector2(sampleAngle, sampleRadius);
            }
        }

        public static void PolarPointOnSpiral(float t, float spacing, out float angle, out float radius)
        {
            angle = Mathf.Sqrt(t) * 3.542f;
            radius = angle * spacing / (Mathf.PI * 2.0f);
        }

        public static int CountPointsInSpiral(float radius, float spacing)
        {
            var scaledRadius = radius / spacing;
            return Mathf.CeilToInt(scaledRadius * scaledRadius * 3.146755f);
        }

        internal void BuildPrefab(PrivateArea privateArea, AssetBundle assetBundle)
        {
            circleProjector = privateArea.m_areaMarker;
            circleProjector.gameObject.SetActive(value: true);

            enabledEffect = privateArea.m_enabledEffect;
            model = privateArea.m_model;

            var materials = model.materials;
            var glowColor = new Color(0.3f, 0.3f, 1.0f);
            foreach (Material material in materials)
            {
                if (material.name.StartsWith("Guardstone_OdenGlow_mat"))
                {
                    material.SetColor("_EmissionColor", glowColor);
                }
            }

            var sparcsGameObject = enabledEffect.transform.Find("sparcs").gameObject;
            var sparcs = sparcsGameObject.GetComponent<ParticleSystem>();

            var sparcsShape = sparcs.shape;
            sparcsShape.scale = new Vector3(8.0f, 0.5f, 8.0f);

            var sparcsMain = sparcs.main;
            sparcsMain.startColor = new ParticleSystem.MinMaxGradient(glowColor, glowColor * 0.15f);

            var lightGO = enabledEffect.transform.Find("Point light").gameObject;
            lightTransform = lightGO.transform;
            Light light = lightGO.GetComponent<Light>();
            light.color = new Color(0.0f, 0.0f, 0.8f, 0.4f);
            light.intensity = 3.0f;
            light.range = 9.0f;

            circleProjector.m_radius = currentRadius + circlePadding;
            circleProjector.m_nrOfSegments = Mathf.CeilToInt(circleProjector.m_radius * 4.0f);

            var flareGameObject = enabledEffect.transform.Find("flare").gameObject;
            flareTransform = flareGameObject.transform;
            var flare = flareGameObject.GetComponent<ParticleSystem>();
            var flareMain = flare.main;
            flareMain.startColor = new ParticleSystem.MinMaxGradient(glowColor);
            flareMain.startSize = new ParticleSystem.MinMaxCurve(1.0f);

            var droneAudioSourceGO = Instantiate(assetBundle.LoadAsset<GameObject>("DroneAudioSource"), Vector3.zero, Quaternion.identity, transform);
            droneAudioSourceTransform = droneAudioSourceGO.transform;
            droneAudioSource = droneAudioSourceGO.GetComponent<AudioSource>();
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return false;
            if (Player.m_localPlayer.InPlaceMode()) return false;

            if (hold)
            {
                if (Time.time - lastUseTime < 1.0f) return false;
                lastUseTime = Time.time;

                if (currentFuelItemIndex == -1) return TakeStoneFromUser(user, true);
                else return TakeFuelItemFromUser(user, currentFuelItemIndex, true);
            }

            lastUseTime = Time.time;

            if (currentFuelItemIndex == -1) return TakeStoneFromUser(user, false);
            else return TakeFuelItemFromUser(user, currentFuelItemIndex, false);
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return false;
            if (Player.m_localPlayer.InPlaceMode()) return false;
            
            if (item == null) return false;

            var itemDisplayName = item.m_shared.m_name;

            for (int fuelItemIndex = -1; fuelItemIndex < fuelItems.Count; fuelItemIndex++)
            {
                if (itemDisplayName == GetFuelItemDisplayName(fuelItemIndex))
                {
                    currentFuelItemIndex = fuelItemIndex;
                    user.Message(MessageHud.MessageType.Center, Localization.instance.Localize(msgSelectMode, itemDisplayName));
                    return true;
                }
            }

            var inventory = user.GetInventory();
            if (inventory == null) return false;

            for (int lowerToolIndex = 0; lowerToolIndex < lowerToolDisplayNames.Length; lowerToolIndex++)
            {
                if (itemDisplayName == lowerToolDisplayNames[lowerToolIndex])
                {
                    var package = new ZPackage();
                    package.Write(lowerToolIndex);
                    WriteItem(package, item);
                    netView.InvokeRPC("AddLowerTool", package);

                    user.UnequipItem(item);
                    inventory.RemoveOneItem(item);

                    return true;
                }
            }

            for (int raiseToolIndex = 0; raiseToolIndex < raiseToolDisplayNames.Length; raiseToolIndex++)
            {
                if (itemDisplayName == raiseToolDisplayNames[raiseToolIndex])
                {
                    var package = new ZPackage();
                    package.Write(raiseToolIndex);
                    WriteItem(package, item);
                    netView.InvokeRPC("AddRaiseTool", package);

                    user.UnequipItem(item);
                    inventory.RemoveOneItem(item);

                    return true;
                }
            }

            return false;
        }

        public string GetHoverName() => "$piece_plateautem";

        public string GetHoverText()
        {
            var totalFuelStored = GetTotalFuelStored();

            var stringBuilder = new StringBuilder();
            if (configShowTools.Value) stringBuilder.Append($"{msgTools}: {GetToolNamesText(" + ")}\n");
            if (configShowFillBars.Value) stringBuilder.Append($"[<color=orange>{TextProgressBar(totalFuelStored / configMaximumFuel.Value, 12)}</color>]\n");
            if (configShowFillNumbers.Value) stringBuilder.Append($"{msgFuel}: {totalFuelStored:0.0}/{configMaximumFuel.Value}\n");
            if (configShowFillBars.Value) stringBuilder.Append($"[<color=grey>{TextProgressBar(currentStoneStored / configMaximumStone.Value, 12)}</color>]\n");
            if (configShowFillNumbers.Value) stringBuilder.Append($"{stoneItemDisplayName}: {currentStoneStored:0.0}/{configMaximumStone.Value}\n");

            if (configShowSelection.Value)
            {
                stringBuilder.Append($" {(currentFuelItemIndex == -1 ? '●' : '○')} {stoneItemDisplayName}: {Mathf.FloorToInt(currentStoneStored)}\n");
                if (fuelItems != null)
                {
                    for (int fuelItemIndex = 0; fuelItemIndex < fuelItems.Count; fuelItemIndex++)
                    {
                        stringBuilder.Append($" {(currentFuelItemIndex == fuelItemIndex ? '●' : '○')} {GetFuelItemDisplayName(fuelItemIndex)}: {GetFuelItemStorage(fuelItemIndex)}\n");
                    }
                }
            }

            if (configShowMainKeys.Value)
            {
                stringBuilder.Append($"[<color=yellow><b>1-8</b></color>] {msgSelectFuel}\n");
                stringBuilder.Append($"[<color=yellow><b>$KEY_Use</b></color>] $piece_smelter_add {CurrentFuelItemName}\n");
                stringBuilder.Append($"[{msgHold} <color=yellow><b>$KEY_Use</b></color>] $piece_smelter_add {msgAll} {CurrentFuelItemName}\n");
                stringBuilder.Append($"[<color=yellow>{configIncreaseRadiusKey.Value}</color>/<color=yellow>{configDecreaseRadiusKey.Value}</color>] {msgRadius}: {Mathf.RoundToInt(currentRadius)}\n");
            }

            if (configShowExtraKeys.Value)
            {
                stringBuilder.Append($"[<color=yellow>{configEjectFuelKey.Value}</color>] {msgEjectFuel}\n");
                stringBuilder.Append($"[<color=yellow>{configEjectStoneKey.Value}</color>] {msgEjectStone}\n");

                if (currentLowerToolIndex >= 0 || currentRaiseToolIndex >= 0)
                {
                    stringBuilder.Append($"[<color=yellow>{configEjectToolsKey.Value}</color>] {msgEject} {GetToolNamesText(" + ")}\n");
                }

                stringBuilder.Append($"[<color=yellow>{configResetScanKey.Value}</color>] {msgResetScan}\n");
            }

            HoverUpdate();

            return Localization.instance.Localize(stringBuilder.ToString());
        }

        private string GetToolNamesText(string separator)
        {
            if (currentLowerToolIndex >= 0)
            {
                if (currentRaiseToolIndex >= 0)
                {
                    return lowerToolDisplayNames[currentLowerToolIndex]+separator+raiseToolDisplayNames[currentRaiseToolIndex];
                }
                else
                {
                    return lowerToolDisplayNames[currentLowerToolIndex];
                }
            }
            else if (currentRaiseToolIndex >= 0)
            {
                return raiseToolDisplayNames[currentRaiseToolIndex];
            }

            return "$piece_smelter_empty";
        }

        private void HoverUpdate()
        {
            if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return;

            if (configIncreaseRadiusKey.Value.IsDown()) netView.InvokeRPC("ChangeRadius", 1.0f);
            if (configDecreaseRadiusKey.Value.IsDown()) netView.InvokeRPC("ChangeRadius", -1.0f);
            if (configResetScanKey.Value.IsDown()) netView.InvokeRPC("ResetScan");
            if (configEjectFuelKey.Value.IsDown()) netView.InvokeRPC("EjectFuel");
            if (configEjectStoneKey.Value.IsDown()) netView.InvokeRPC("EjectStone");
            if (configEjectToolsKey.Value.IsDown()) netView.InvokeRPC("EjectTools");
        }

        private static void WriteItem(ZPackage package, ItemDrop.ItemData item)
        {
            package.Write(item.m_durability);
            package.Write(item.m_stack);
            package.Write(item.m_quality);
            package.Write(item.m_variant);
            package.Write(item.m_crafterID);
            package.Write(item.m_crafterName);
            package.Write(item.m_customData.Count);
            foreach (var kv in item.m_customData)
            {
                package.Write(kv.Key);
                package.Write(kv.Value);
            }
        }

        private static void ReadItem(ZPackage package, ItemDrop.ItemData item)
        {
            item.m_durability = package.ReadSingle();
            item.m_stack = package.ReadInt();
            item.m_quality = package.ReadInt();
            item.m_variant = package.ReadInt();
            item.m_crafterID = package.ReadLong();
            item.m_crafterName = package.ReadString();
            var customDataCount = package.ReadInt();
            item.m_customData.Clear();
            for (int i = 0; i < customDataCount; i++)
            {
                var key = package.ReadString();
                var value = package.ReadString();
                item.m_customData[key] = value;
            }
        }

        private static void ReadItemToZDO(int index, ZPackage package, ZDO zdo)
        {
            int dataCount;
            string indexString = index.ToString();
            zdo.Set(indexString + "_durability", package.ReadSingle());
            zdo.Set(indexString + "_stack", package.ReadInt());
            zdo.Set(indexString + "_quality", package.ReadInt());
            zdo.Set(indexString + "_variant", package.ReadInt());
            zdo.Set(indexString + "_crafterID", package.ReadLong());
            zdo.Set(indexString + "_crafterName", package.ReadString());
            zdo.Set(indexString + "_dataCount", dataCount = package.ReadInt());
            int dataIndex = 0;
            for (int i = 0; i < dataCount; i++)
            {
                zdo.Set(string.Format("{0}_data_{1}", index, dataIndex), package.ReadString());
                zdo.Set(string.Format("{0}_data__{1}", index, dataIndex++), package.ReadString());
            }
        }

        private void EjectFuel(bool clearStorage)
        {
            int totalDropped = 0;

            if (fuelItems != null)
            {
                for (int fuelItemIndex = 0; fuelItemIndex < fuelItems.Count; ++fuelItemIndex)
                {
                    var fuelItem = fuelItems[fuelItemIndex];
                    var itemDrop = ObjectDB.instance?.GetItemPrefab(fuelItem.prefabName)?.GetComponent<ItemDrop>();
                    if (itemDrop != null)
                    {
                        var maxStackSize = itemDrop.m_itemData.m_shared.m_maxStackSize;
                        if (maxStackSize > 0)
                        {
                            var amountToDrop = GetFuelItemStorage(fuelItemIndex);
                            if (clearStorage) SetFuelItemStorage(fuelItemIndex, 0);
                            while (amountToDrop > 0)
                            {
                                int stackSize = Mathf.Min(amountToDrop, maxStackSize);
                                amountToDrop -= stackSize;

                                var position = transform.position + Vector3.up * 1.2f + Random.insideUnitSphere * 0.25f;
                                var rotation = Quaternion.AngleAxis(Random.Range(0.0f, 360.0f), Vector3.up);

                                var droppedItem = Instantiate(itemDrop.gameObject, position, rotation).GetComponent<ItemDrop>();
                                if (droppedItem != null)
                                {
                                    droppedItem.m_itemData.m_stack = stackSize;
                                    totalDropped += stackSize;
                                }
                            }
                        }
                    }
                }
            }

            if (totalDropped == 0 && clearStorage) currentFuelStored = 0.0f;
        }

        private void EjectStone(bool clearStorage)
        {
            int totalDropped = 0;

            var stoneItemDrop = ObjectDB.instance?.GetItemPrefab(stoneItemPrefabName)?.GetComponent<ItemDrop>();
            if (stoneItemDrop != null)
            {
                var maxStackSize = stoneItemDrop.m_itemData.m_shared.m_maxStackSize;
                if (maxStackSize > 0)
                {
                    var amountToDrop = Mathf.FloorToInt(currentStoneStored);
                    if (clearStorage) currentStoneStored -= amountToDrop;
                    while (amountToDrop > 0)
                    {
                        int stackSize = Mathf.Min(amountToDrop, maxStackSize);
                        amountToDrop -= stackSize;

                        var position = transform.position + Vector3.up * 1.2f + Random.insideUnitSphere * 0.25f;
                        var rotation = Quaternion.AngleAxis(Random.Range(0.0f, 360.0f), Vector3.up);

                        var droppedItem = Instantiate(stoneItemDrop.gameObject, position, rotation).GetComponent<ItemDrop>();
                        if (droppedItem != null)
                        {
                            droppedItem.m_itemData.m_stack = stackSize;
                            totalDropped += stackSize;
                        }
                    }
                }
            }

            if (totalDropped == 0 && clearStorage) currentStoneStored = 0.0f;
        }

        private void EjectTools(bool ejectLowerTool, bool ejectRaiseTool)
        {
            if (ejectLowerTool && currentLowerToolIndex >= 0)
            {
                var itemDrop = ObjectDB.instance?.GetItemPrefab(lowerToolPrefabNames[currentLowerToolIndex])?.GetComponent<ItemDrop>();
                if (itemDrop != null)
                {
                    var position = transform.position + Vector3.up * 1.2f + Random.insideUnitSphere * 0.25f;
                    var rotation = Quaternion.AngleAxis(Random.Range(0.0f, 360.0f), Vector3.up);
                    var droppedItem = Instantiate(itemDrop.gameObject, position, rotation).GetComponent<ItemDrop>();
                    if (droppedItem != null)
                    {
                        ItemDrop.LoadFromZDO(droppedItem.m_itemData, netView.GetZDO());
                    }
                }

                currentLowerToolIndex = -1;
            }

            if (ejectRaiseTool && currentRaiseToolIndex >= 0)
            {
                var itemDrop = ObjectDB.instance?.GetItemPrefab(raiseToolPrefabNames[currentRaiseToolIndex])?.GetComponent<ItemDrop>();
                if (itemDrop != null)
                {
                    var position = transform.position + Vector3.up * 1.2f + Random.insideUnitSphere * 0.25f;
                    var rotation = Quaternion.AngleAxis(Random.Range(0.0f, 360.0f), Vector3.up);
                    var droppedItem = Instantiate(itemDrop.gameObject, position, rotation).GetComponent<ItemDrop>();
                    if (droppedItem != null)
                    {
                        ItemDrop.LoadFromZDO(droppedItem.m_itemData, netView.GetZDO());
                    }
                }

                currentRaiseToolIndex = -1;
            }
        }

        private void UpdateCircle(float radius)
        {
            if (circleProjector != null)
            {
                if (circleProjector.m_radius != radius + circlePadding)
                {
                    circleProjector.m_radius = radius + circlePadding;
                    circleProjector.m_nrOfSegments = Mathf.CeilToInt(circleProjector.m_radius * 4.0f);
                }
            }
        }

        private void SettingsUpdated()
        {
            if (currentRadius > configMaximumFlatteningRadius.Value) currentRadius = configMaximumFlatteningRadius.Value;

            UpdateCircle(currentRadius);
        }

        private string TextProgressBar(float fraction, int length)
        {
            var stringBuilder = new StringBuilder();
            int halfSegmentCount = Mathf.RoundToInt(fraction * length * 2);
            for (int i = 0; i < length; ++i)
            {
                if (halfSegmentCount >= 2)
                {
                    halfSegmentCount -= 2;
                    stringBuilder.Append("█");
                }
                else if (halfSegmentCount >= 1)
                {
                    halfSegmentCount -= 1;
                    stringBuilder.Append("▌");
                }
                else
                {
                    stringBuilder.Append("─");
                }
            }

            return stringBuilder.ToString();
        }

        private bool TakeFuelItemFromUser(Humanoid user, int fuelItemIndex, bool takeAll)
        {
            var inventory = user.GetInventory();
            if (inventory == null) return false;

            var fuelItemDisplayName = GetFuelItemDisplayName(fuelItemIndex);
            var fuelPerItem = fuelItems[fuelItemIndex].fuelValue;
            var playerFuelCount = inventory.CountItems(fuelItemDisplayName);
            if (playerFuelCount <= 0)
            {
                user.Message(MessageHud.MessageType.Center, Localization.instance.Localize(msgNoFuel, fuelItemDisplayName));
                return false;
            }

            var fuelStored = GetTotalFuelStored();
            var spaceRemaining = Mathf.FloorToInt((configMaximumFuel.Value - fuelStored) / fuelPerItem);
            if (spaceRemaining <= 0)
            {
                user.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_cantaddmore", fuelItemDisplayName));
                return false;
            }

            var fuelItemsToAdd = takeAll ? Mathf.Min(spaceRemaining, playerFuelCount) : 1;
            netView.InvokeRPC("AddFuelItem", fuelItemIndex, fuelItemsToAdd);
            inventory.RemoveItem(fuelItemDisplayName, fuelItemsToAdd);

            user.Message(MessageHud.MessageType.Center, Localization.instance.Localize($"$msg_added {fuelItemsToAdd} {fuelItemDisplayName}"));

            return true;
        }

        private bool TakeStoneFromUser(Humanoid user, bool takeAll)
        {
            var inventory = user.GetInventory();
            if (inventory == null) return false;

            var playerFuelCount = inventory.CountItems(stoneItemDisplayName);
            if (playerFuelCount <= 0)
            {
                user.Message(MessageHud.MessageType.Center, Localization.instance.Localize(msgNoFuel, stoneItemDisplayName));
                return false;
            }

            var spaceRemaining = Mathf.FloorToInt(configMaximumStone.Value - currentStoneStored);
            if (spaceRemaining <= 0)
            {
                user.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_cantaddmore", stoneItemDisplayName));
                return false;
            }

            var stoneToAdd = takeAll ? Mathf.Min(spaceRemaining, playerFuelCount) : 1;
            netView.InvokeRPC("AddStone", stoneToAdd);
            inventory.RemoveItem(stoneItemDisplayName, stoneToAdd);

            user.Message(MessageHud.MessageType.Center, Localization.instance.Localize($"$msg_added {stoneToAdd} {stoneItemDisplayName}"));

            return true;
        }

        private int GetFuelItemStorage(int index, int defaultAmount = 0)
        {
            if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return 0;
            return netView.GetZDO().GetInt($"fuel_storage_{index}", defaultAmount);
        }

        private void SetFuelItemStorage(int index, int amount)
        {
            if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return;
            netView.GetZDO().Set($"fuel_storage_{index}", amount);
        }

        private float GetTotalFuelStored()
        {
            if (fuelItems == null) return currentFuelStored;

            float totalFuel = currentFuelStored;
            for (int fuelItemIndex = 0; fuelItemIndex < fuelItems.Count; fuelItemIndex++)
            {
                totalFuel += GetFuelItemStorage(fuelItemIndex) * fuelItems[fuelItemIndex].fuelValue;
            }

            return totalFuel;
        }

        private void ConsumeFuel(float amount)
        {
            float fuelStored = currentFuelStored;
            while (amount > 0.0f)
            {
                if (fuelStored >= amount)
                {
                    fuelStored -= amount;
                    break;
                }

                amount -= fuelStored;
                fuelStored = 0.0f;

                for (int fuelItemIndex = 0; fuelItemIndex < fuelItems.Count; fuelItemIndex++)
                {
                    FuelItem fuelItem = fuelItems[fuelItemIndex];
                    var fuelItemAmount = GetFuelItemStorage(fuelItemIndex);
                    if (fuelItemAmount > 0)
                    {
                        fuelItemAmount--;
                        SetFuelItemStorage(fuelItemIndex, fuelItemAmount);

                        fuelStored += fuelItem.fuelValue;
                        break;
                    }
                }
            }

            currentFuelStored = fuelStored;
        }

        public static bool IsInsideNoBuildLocation(Vector3 point, float radius)
        {
            foreach (Location allLocation in Location.m_allLocations)
            {
                if (allLocation.m_noBuild && allLocation.IsInside(point, radius))
                    return true;
            }
            return false;
        }

        public static bool IsInLoadedArea(Vector3 point, float radius, bool checkSelf = true)
        {
            var fromId = ZoneSystem.instance.GetZone(point - new Vector3(radius, 0.0f, radius));
            var toId = ZoneSystem.instance.GetZone(point + new Vector3(radius, 0.0f, radius));

            if (checkSelf)
            {
                var refPos = ZNet.instance.GetReferencePosition();
                var refCenterZone = ZoneSystem.instance.GetZone(refPos);
                bool inActiveArea = true;
                for (int y = fromId.y; y <= toId.y; ++y)
                {
                    for (int x = fromId.x; x <= toId.x; ++x)
                    {
                        if (!ZNetScene.instance.InActiveArea(new Vector2i(x, y), refCenterZone))
                        {
                            inActiveArea = false;
                            break;
                        }
                    }
                }

                if (inActiveArea) return true;
            }

            foreach (var peer in ZNet.instance.GetConnectedPeers())
            {
                var refPos = peer.GetRefPos();
                var refCenterZone = ZoneSystem.instance.GetZone(refPos);
                bool inActiveArea = true;
                for (int y = fromId.y; y <= toId.y; ++y)
                {
                    for (int x = fromId.x; x <= toId.x; ++x)
                    {
                        if (!ZNetScene.instance.InActiveArea(new Vector2i(x, y), refCenterZone))
                        {
                            inActiveArea = false;
                            break;
                        }
                    }
                }

                if (inActiveArea) return true;
            }

            return false;
        }


        [HarmonyPatch(typeof(TerrainComp))]
        public static class Patch
        {
            [HarmonyPrefix]
            [HarmonyPatch("LevelTerrain")]
            public static bool LevelTerrain(TerrainComp __instance, Vector3 worldPos, float radius, bool square)
            {
                int x1;
                int y1;
                __instance.m_hmap.WorldToVertex(worldPos, out x1, out y1);
                Vector3 vector3 = worldPos - __instance.transform.position;
                float f = radius / __instance.m_hmap.m_scale;
                int num1 = Mathf.CeilToInt(f);
                int num2 = __instance.m_width + 1;
                Vector2 a = new Vector2(x1, y1);
                for (int y2 = y1 - num1; y2 <= y1 + num1; ++y2)
                {
                    for (int x2 = x1 - num1; x2 <= x1 + num1; ++x2)
                    {
                        float distance = Vector2.Distance(a, new Vector2(x2, y2));
                        if ((square || distance <= f) && (x2 >= 0 && y2 >= 0) && (x2 < num2 && y2 < num2))
                        {
                            float df = 1.0f - distance / f;
                            float height = __instance.m_hmap.GetHeight(x2, y2);
                            float num3 = (vector3.y - height) * (df > 0.5f ? 1.0f : df / 0.5f);
                            int index = y2 * num2 + x2;
                            float num4 = num3 + __instance.m_smoothDelta[index];
                            __instance.m_smoothDelta[index] = 0.0f;
                            __instance.m_levelDelta[index] += num4;
                            __instance.m_modifiedHeight[index] = true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
