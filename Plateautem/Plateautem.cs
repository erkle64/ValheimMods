using BepInEx;
using BepInEx.Configuration;
using Jotunn.Configs;
using Jotunn.Managers;
using Mono.Cecil;
using System.Collections;
using System.Collections.Generic;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Plateautem
{
    public class Plateautem : MonoBehaviour, Interactable, Hoverable
    {
        [SerializeField] private TerrainOp flattenTerrainOp = null;
        [SerializeField] private EffectList flattenPlaceEffect = null;

        [SerializeField] private CircleProjector circleProjector = null;
        [SerializeField] private GameObject enabledEffect = null;
        [SerializeField] private MeshRenderer model = null;
        private ZNetView netView = null;

        [SerializeField] private Transform lightTransform = null;
        [SerializeField] private Transform flareTransform = null;
        [SerializeField] private Transform droneAudioSourceTransform = null;
        [SerializeField] private AudioSource droneAudioSource = null;
        private Vector3 targetLightPosition;
        private Vector3 currentLightPosition;
        private float lightSpeed;
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

        private const float placementSpacing = 2.0f;
        private const float placementRadiusScale = placementSpacing / (2.0f * Mathf.PI);

        private int currentFuelItemIndex = 0;

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
        private static ConfigEntry<float> configFlatteningTime;
        private static ConfigEntry<float> configScanningTime;
        private static ConfigEntry<KeyboardShortcut> configIncreaseRadiusKey;
        private static ConfigEntry<KeyboardShortcut> configDecreaseRadiusKey;
        private static ConfigEntry<KeyboardShortcut> configResetScanKey;
        private static ConfigEntry<KeyboardShortcut> configEjectFuelKey;
        private static ConfigEntry<KeyboardShortcut> configEjectStoneKey;
        private float lastUseTime;
        public const string msgNoFuel = "$piece_plateautem_noFuel";
        public const string msgHold = "$piece_plateautem_hold";
        public const string msgAll = "$piece_plateautem_all";
        public const string msgFuel = "$piece_plateautem_fuel";
        public const string msgRadius = "$piece_plateautem_radius";
        public const string msgResetScan = "$piece_plateautem_reset";
        public const string msgSelectFuel = "$piece_plateautem_selectFuel";
        public const string msgEjectFuel = "$piece_plateautem_ejectFuel";
        public const string msgEjectStone = "$piece_plateautem_ejectStone";

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
                if (netView.IsOwner())
                {
                    netView.GetZDO().Set(zdoidCurrentRadius, value);
                    UpdateCircle(value);
                }
                else
                {
                    netView.InvokeRPC("SetCurrentRadius", value);
                    UpdateCircle(value);
                }
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

        private const string zdonScanRadius = "placement_radius";
        private int zdoidScanRadius;
        private float currentScanRadius
        {
            get
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return 0;
                return netView.GetZDO().GetFloat(zdoidScanRadius, 0.0f);
            }
            set
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return;
                netView.GetZDO().Set(zdoidScanRadius, value);
            }
        }

        private const string zdonScanAngle = "scan_angle";
        private int zdoidScanAngle;
        private float currentScanAngle
        {
            get
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return 0;
                return netView.GetZDO().GetFloat(zdoidScanAngle, 0.0f);
            }
            set
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return;
                netView.GetZDO().Set(zdoidScanAngle, value);
            }
        }

        private string CurrentFuelItemName 
        {
            get
            {
                if (currentFuelItemIndex == -1) return stoneItemDisplayName;

                if (fuelItems == null || fuelItems.Count == 0) return "";

                return GetFuelItemDisplayName(currentFuelItemIndex);
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
            configFlatteningTime = plugin.Config.Bind("Server", "Flattening Time", defaultValue: 1.0f, new ConfigDescription("Time taken for a flattening action.", new AcceptableValueRange<float>(0.1f, 60.0f), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configScanningTime = plugin.Config.Bind("Server", "Scanning Time", defaultValue: 0.2f, new ConfigDescription("Time taken for a scanning action.", new AcceptableValueRange<float>(0.1f, 60.0f), new ConfigurationManagerAttributes { IsAdminOnly = true }));

            configIncreaseRadiusKey = plugin.Config.Bind("Input", "Increase flattening radius", new KeyboardShortcut(KeyCode.KeypadPlus));
            configDecreaseRadiusKey = plugin.Config.Bind("Input", "Decrease flattening radius", new KeyboardShortcut(KeyCode.KeypadMinus));
            configResetScanKey = plugin.Config.Bind("Input", "Reset scan position", new KeyboardShortcut(KeyCode.KeypadEnter));
            configEjectFuelKey = plugin.Config.Bind("Input", "Eject fuel", new KeyboardShortcut(KeyCode.KeypadDivide));
            configEjectStoneKey = plugin.Config.Bind("Input", "Eject stone", new KeyboardShortcut(KeyCode.KeypadMultiply));

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
            Jotunn.Logger.LogInfo($"configFuelItems.Value: {configFuelItems.Value} {fuelItemMatches.Count}");
            if (fuelItemMatches.Count > 0)
            {
                for (int i = 0; i < fuelItemMatches.Count; ++i)
                {
                    var match = fuelItemMatches[i];
                    Jotunn.Logger.LogInfo($"match.Groups.Count: {match.Groups.Count}");
                    if (match.Groups.Count >= 2)
                    {
                        string fuelItemPrefabName = match.Groups[1].Value;
                        Jotunn.Logger.LogInfo($"fuelItemPrefabName: {fuelItemPrefabName}");
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
            zdoidScanRadius = zdonScanRadius.GetStableHashCode();
            zdoidScanAngle = zdonScanAngle.GetStableHashCode();

            if (flattenTerrainOp == null)
            {
                TerrainOp raiseTerrainOp = null;
                var pieceTable = PieceManager.Instance.GetPieceTable("_HoePieceTable");
                foreach (var piece in pieceTable.m_pieces)
                {
                    if (piece != null && piece.name.Contains("raise"))
                    {
                        raiseTerrainOp = piece.GetComponent<TerrainOp>();
                        if (raiseTerrainOp != null)
                        {
                            Jotunn.Logger.LogInfo("Found raise TerrainOp.");

                            var flattenGO = PrefabManager.Instance.CreateClonedPrefab("plateautem_flatten_op", raiseTerrainOp.gameObject);
                            flattenTerrainOp = flattenGO.GetComponent<TerrainOp>();

                            flattenTerrainOp.m_settings.m_levelOffset = 0.0f;

                            flattenTerrainOp.m_settings.m_level = true;
                            flattenTerrainOp.m_settings.m_levelRadius = 2.5f;
                            flattenTerrainOp.m_settings.m_square = false;

                            flattenTerrainOp.m_settings.m_raise = false;
                            flattenTerrainOp.m_settings.m_raiseRadius = 2.5f;
                            flattenTerrainOp.m_settings.m_raisePower = 10.0f;
                            flattenTerrainOp.m_settings.m_raiseDelta = 0.0f;

                            flattenTerrainOp.m_settings.m_smooth = false;
                            flattenTerrainOp.m_settings.m_smoothRadius = 3.0f;
                            flattenTerrainOp.m_settings.m_smoothPower = 3.0f;

                            flattenTerrainOp.m_settings.m_paintCleared = true;
                            flattenTerrainOp.m_settings.m_paintHeightCheck = false;
                            flattenTerrainOp.m_settings.m_paintType = TerrainModifier.PaintType.Dirt;
                            flattenTerrainOp.m_settings.m_paintRadius = 2f;

                            flattenPlaceEffect = flattenGO.GetComponent<Piece>()?.m_placeEffect;

                            break;
                        }
                    }
                }
            }

            WearNTear wearNTear = GetComponent<WearNTear>();
            wearNTear.m_onDestroyed = (System.Action)System.Delegate.Combine(wearNTear.m_onDestroyed, new System.Action(OnDestroyed));

            netView.Register<float>("SetCurrentRadius", RPC_SetCurrentRadius);

            Invoke("AttemptLevelling", configScanningTime.Value);

            currentLightPosition = targetLightPosition = transform.position + Vector3.up * 1.5f;

            UpdateCircle(currentRadius);
        }

        private void OnDestroyed()
        {
            EjectFuel(false);
            EjectStone(false);

            CancelInvoke("AttemptLevelling");
        }

        private void Update()
        {
            currentLightPosition = Vector3.MoveTowards(currentLightPosition, targetLightPosition, lightSpeed * Time.deltaTime);

            if (lightTransform != null) lightTransform.position = currentLightPosition;
            if (flareTransform != null) flareTransform.position = currentLightPosition;
            if (droneAudioSourceTransform != null) droneAudioSourceTransform.position = currentLightPosition;

            if (droneAudioSource != null)
            {
                if (!droneAudioSource.isPlaying)
                {
                    droneAudioSource.gameObject.SetActive(true);
                    droneAudioSource.enabled = true;
                    droneAudioSource.Play();
                }
                droneAudioSource.volume = Mathf.MoveTowards(droneAudioSource.volume, targetDroneAudioVolume, Time.deltaTime);
            }
        }

        private void AttemptLevelling()
        {
            if (!netView || !netView.IsOwner() || !netView.IsValid() || Player.m_localPlayer == null)
            {
                Invoke("AttemptLevelling", 5.0f);
                return;
            }

            if (flattenTerrainOp == null)
            {
                Invoke("AttemptLevelling", 5.0f);
                return;
            }

            UpdateCircle(currentRadius);

            if (currentScanRadius > currentRadius + placementSpacing)
            {
                currentScanRadius = 0.0f;
                currentScanAngle = 0.0f;
            }

            var radius = Mathf.Min(currentScanRadius, currentRadius);
            var offset = new Vector3(Mathf.Sin(currentScanAngle) * radius, 0.0f, Mathf.Cos(currentScanAngle) * radius);
            var position = transform.position + offset;
            float totalRaise = 0.0f;
            float totalLower = 0.0f;
            float fuelRequired = 0.0f;
            float stoneRequired = 0.0f;
            float triggerVolume = 0.0f;
            const float sampleSpacing = 0.5f;
            const float sampleSpacingSqr = sampleSpacing * sampleSpacing;
            const float cefgw = 0.1f;
            foreach (var groundPoint in EachGroundPointInSpiral(position, 2.0f, sampleSpacing))
            {
                if (groundPoint.y > position.y + cefgw)
                {
                    var volume = (groundPoint.y - position.y) * sampleSpacingSqr;
                    totalRaise += volume;
                    fuelRequired += volume * configFuelPerLower.Value;
                    stoneRequired -= volume * configStonePerLower.Value;
                    triggerVolume += volume * Mathf.Pow(Mathf.Clamp01(1.0f - groundPoint.w / 2.0f), 0.3f);
                }
                else if (groundPoint.y < position.y - cefgw)
                {
                    float volume = (position.y - groundPoint.y) * sampleSpacingSqr;
                    totalLower += volume;
                    fuelRequired += volume * configFuelPerRaise.Value;
                    stoneRequired += volume * configStonePerRaise.Value;
                    triggerVolume += volume * Mathf.Pow(Mathf.Clamp01(1.0f - groundPoint.w / 2.0f), 0.3f);
                }
            }

            bool doFlatten = triggerVolume > 0.35f;
            if (doFlatten && stoneRequired > 0 && currentStoneStored < stoneRequired) doFlatten = false;
            if (doFlatten && stoneRequired < 0 && (configMaximumStone.Value - currentStoneStored) < -stoneRequired) doFlatten = false;

            float nextUpdateDelay = configScanningTime.Value;
            if (doFlatten)
            {
                fuelRequired += configFuelPerScan.Value;
            }
            else
            {
                fuelRequired = configFuelPerScan.Value;
                stoneRequired = 0.0f;
            }

            var fuelStored = GetTotalFuelStored();
            if (fuelStored >= fuelRequired)
            {
                ConsumeFuel(fuelRequired);
                currentStoneStored -= stoneRequired;

                if (doFlatten)
                {
                    nextUpdateDelay = configFlatteningTime.Value;
                    var terrainOpGO = Instantiate(flattenTerrainOp.gameObject, position, Quaternion.identity);
                    flattenPlaceEffect?.Create(position, Quaternion.identity, terrainOpGO.transform);
                }

                currentScanAngle += (currentScanRadius > 0.0f) ? placementSpacing / currentScanRadius : placementSpacing;
                currentScanRadius = placementRadiusScale * currentScanAngle;

                if (currentScanRadius > currentRadius + placementSpacing)
                {
                    targetLightPosition = transform.position + Vector3.up * 0.5f;
                }
                else
                {
                    var targetRadius = Mathf.Min(currentScanRadius, currentRadius);
                    var targetOffset = new Vector3(Mathf.Sin(currentScanAngle) * targetRadius, 0.5f, Mathf.Cos(currentScanAngle) * targetRadius);
                    targetLightPosition = transform.position + targetOffset;
                }

                lightSpeed = Vector3.Distance(currentLightPosition, targetLightPosition) / nextUpdateDelay;
                targetDroneAudioVolume = 1.0f;
            }
            else
            {
                targetDroneAudioVolume = 0.0f;
            }

            Invoke("AttemptLevelling", nextUpdateDelay);
        }

        private IEnumerable<Vector4> EachGroundPointInSpiral(Vector3 origin, float radius, float sampleSpacing = 0.5f)
        {
            float sampleScale = sampleSpacing / (2.0f * Mathf.PI);
            float sampleAngle = 0.0f;
            float sampleRadius = 0.0f;
            float sampleHeight;
            while (sampleRadius < radius)
            {
                var sampleOffset = new Vector3(Mathf.Sin(sampleAngle) * sampleRadius, 0.0f, Mathf.Cos(sampleAngle) * sampleRadius);
                var samplePosition = origin + sampleOffset;
                if (ZoneSystem.instance.GetGroundHeight(samplePosition, out sampleHeight))
                {
                    yield return new Vector4(samplePosition.x, sampleHeight, samplePosition.z, sampleRadius);
                }

                sampleAngle += (sampleRadius > 0.0f) ? sampleSpacing / sampleRadius : sampleSpacing;
                sampleRadius = sampleScale * sampleAngle;
            }
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

            circleProjector.m_radius = currentRadius + 1.5f;
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

            for (int fuelItemIndex = -1; fuelItemIndex < fuelItems.Count; fuelItemIndex++)
            {
                if (item.m_shared.m_name == GetFuelItemDisplayName(fuelItemIndex))
                {
                    currentFuelItemIndex = fuelItemIndex;
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
            stringBuilder.Append($"[{TextProgressBar(totalFuelStored / configMaximumFuel.Value, 12)}]\n");
            stringBuilder.Append($"{msgFuel}: {totalFuelStored:0.0}/{configMaximumFuel.Value}\n");
            stringBuilder.Append($"[<color=yellow><b>$KEY_Use</b></color>] $piece_smelter_add {CurrentFuelItemName}\n");
            stringBuilder.Append($"[{msgHold} <color=yellow><b>$KEY_Use</b></color>] $piece_smelter_add {msgAll} {CurrentFuelItemName}\n");
            stringBuilder.Append($"[<color=yellow><b>1-8</b></color>] {msgSelectFuel}\n");

            stringBuilder.Append($" {(currentFuelItemIndex == -1 ? '●' : '○')} {stoneItemDisplayName}: {currentStoneStored:0.0}\n");
            if (fuelItems != null)
            {
                for (int fuelItemIndex = 0; fuelItemIndex < fuelItems.Count; fuelItemIndex++)
                {
                    stringBuilder.Append($" {(currentFuelItemIndex == fuelItemIndex ? '●' : '○')} {GetFuelItemDisplayName(fuelItemIndex)}: {GetFuelItemStorage(fuelItemIndex)}\n");
                }
            }

            stringBuilder.Append($"[<color=yellow>{configIncreaseRadiusKey.Value}</color>/<color=yellow>{configDecreaseRadiusKey.Value}</color>] {msgRadius}: {Mathf.RoundToInt(currentRadius)}\n");
            stringBuilder.Append($"[<color=yellow>{configEjectFuelKey.Value}</color>] {msgEjectFuel}\n");
            stringBuilder.Append($"[<color=yellow>{configEjectStoneKey.Value}</color>] {msgEjectStone}\n");
            stringBuilder.Append($"[<color=yellow>{configResetScanKey.Value}</color>] {msgResetScan}\n");

            HoverUpdate();

            return Localization.instance.Localize(stringBuilder.ToString());
        }

        private void HoverUpdate()
        {
            if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return;

            if (configIncreaseRadiusKey.Value.IsDown() && currentRadius + 1.0f <= 40.0f)
            {
                currentRadius += 1.0f;
            }

            if (configDecreaseRadiusKey.Value.IsDown() && currentRadius - 1.0f > 1.0f)
            {
                currentRadius -= 1.0f;
            }

            if (configResetScanKey.Value.IsDown())
            {
                currentScanAngle = 0.0f;
                currentScanRadius = 0.0f;
            }

            if (configEjectFuelKey.Value.IsDown()) EjectFuel(true);
            if (configEjectStoneKey.Value.IsDown()) EjectStone(true);
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

        private void RPC_SetCurrentRadius(long sender, float radius)
        {
            if (netView.IsOwner())
            {
                Jotunn.Logger.LogInfo($"currentRadius: {currentRadius}");
                Jotunn.Logger.LogInfo($"radius: {radius}");
                currentRadius = Mathf.Clamp(radius, 1.0f, configMaximumFlatteningRadius.Value);
            }

            UpdateCircle(radius);
        }

        private void UpdateCircle(float radius)
        {
            if (circleProjector != null)
            {
                circleProjector.m_radius = radius + 1.5f;
                circleProjector.m_nrOfSegments = Mathf.CeilToInt(circleProjector.m_radius * 4.0f);
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
            SetFuelItemStorage(fuelItemIndex, GetFuelItemStorage(fuelItemIndex) + fuelItemsToAdd);
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
            currentStoneStored += stoneToAdd;
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
    }
}
