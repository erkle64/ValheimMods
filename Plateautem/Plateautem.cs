using BepInEx;
using BepInEx.Configuration;
using Jotunn.Managers;
using System.Text;
using UnityEngine;

namespace Plateautem
{
    public class Plateautem : MonoBehaviour, Interactable, Hoverable
    {
        private TerrainOp flattenTerrainOp = null;
        private EffectList flattenPlaceEffect = null;

        private CircleProjector circleProjector = null;
        private GameObject enabledEffect = null;
        private MeshRenderer model = null;
        private ZNetView netView = null;

        private static string fuelItemName = null;
        private static string FuelItemName
        {
            get
            {
                if (fuelItemName == null)
                {
                    var itemDrop = ObjectDB.instance?.GetItemPrefab(fuelItemPrefabName)?.GetComponent<ItemDrop>();
                    if (itemDrop != null) fuelItemName = itemDrop.m_itemData.m_shared.m_name;
                }
                return fuelItemName;
            }
        }

        private static string fuelItemPrefabName;
        private static int fuelPerItem;
        private static int maxFuel;
        private static float maxRadius;
        private static float placementInterval;

        private const float placementSpacing = 2.0f;
        private const float placementRadiusScale = placementSpacing / (2.0f * Mathf.PI);

        private static ConfigEntry<string> configFuelItemPrefabName;
        private static ConfigEntry<int> configFuelPerItem;
        private static ConfigEntry<int> configMaximumFuel;
        private static ConfigEntry<float> configFlatteningRadius;
        private static ConfigEntry<float> configFlatteningInterval;

        private float lastUseTime;
        public const string msgNoFuel = "$piece_plateautem_noFuel";
        public const string msgHold = "$piece_plateautem_hold";
        public const string msgAll = "$piece_plateautem_all";
        public const string msgFuel = "$piece_plateautem_fuel";

        private const string zdonFuelStored = "fuel_stored";
        private int zdoidFuelStored;
        private int currentFuelStored
        {
            get
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return 0;
                return netView.GetZDO().GetInt(zdoidFuelStored, 0);
            }
            set
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return;
                netView.GetZDO().Set(zdoidFuelStored, value);
            }
        }

        private const string zdonPlacementRadius = "placement_radius";
        private int zdoidPlacementRadius;
        private float currentPlacementRadius
        {
            get
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return 0;
                return netView.GetZDO().GetFloat(zdoidPlacementRadius, 0.0f);
            }
            set
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return;
                netView.GetZDO().Set(zdoidPlacementRadius, value);
            }
        }

        private const string zdonPlacementAngle = "placement_angle";
        private int zdoidPlacementAngle;
        private float currentPlacementAngle
        {
            get
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return 0;
                return netView.GetZDO().GetFloat(zdoidPlacementAngle, 0.0f);
            }
            set
            {
                if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return;
                netView.GetZDO().Set(zdoidPlacementAngle, value);
            }
        }

        public static void LoadConfig(BaseUnityPlugin plugin)
        {
            configFuelItemPrefabName = plugin.Config.Bind("Server", "Fuel Item Prefab Name", defaultValue: "Resin", new ConfigDescription("Prefab name of item to use as fuel.", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configFuelPerItem = plugin.Config.Bind("Server", "Fuel Per Item", defaultValue: 5, new ConfigDescription("Amount of fuel contained in each item.", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configMaximumFuel = plugin.Config.Bind("Server", "Maximum Fuel", defaultValue: 250, new ConfigDescription("Maximum amount of fuel stored in the totem.", new AcceptableValueRange<int>(1, 10000), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configFlatteningRadius = plugin.Config.Bind("Server", "Flattening Radius", defaultValue: 15.0f, new ConfigDescription("Radius of the are to be flattened.", new AcceptableValueRange<float>(5.0f, 40.0f), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            configFlatteningInterval = plugin.Config.Bind("Server", "Flattening Interval", defaultValue: 1.5f, new ConfigDescription("Time between flattening actions.", new AcceptableValueRange<float>(0.1f, 60.0f), new ConfigurationManagerAttributes { IsAdminOnly = true }));

            fuelItemPrefabName = configFuelItemPrefabName.Value;
            fuelPerItem = configFuelPerItem.Value;
            maxFuel = configMaximumFuel.Value;
            maxRadius = configFlatteningRadius.Value;
            placementInterval = configFlatteningInterval.Value;
        }

        private void Awake()
        {
            netView = GetComponent<ZNetView>();

            zdoidFuelStored = zdonFuelStored.GetStableHashCode();
            zdoidPlacementRadius = zdonPlacementRadius.GetStableHashCode();
            zdoidPlacementAngle = zdonPlacementAngle.GetStableHashCode();

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

            InvokeRepeating("AttemptLevelling", 1.0f, placementInterval);
        }

        private void OnDestroyed()
        {
            var itemDrop = ObjectDB.instance?.GetItemPrefab(fuelItemPrefabName)?.GetComponent<ItemDrop>();
            if (itemDrop != null)
            {
                var maxStackSize = itemDrop.m_itemData.m_shared.m_maxStackSize;
                if (maxStackSize > 0)
                {
                    var amountToDrop = Mathf.FloorToInt(currentFuelStored / (float)fuelPerItem);
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
                        }
                    }
                }
            }

            CancelInvoke("AttemptLevelling");
        }

        private void AttemptLevelling()
        {
            if (!netView || !netView.IsOwner() || !netView.IsValid() || Player.m_localPlayer == null) return;
            if (currentPlacementRadius > maxRadius + placementSpacing) return;

            if (flattenTerrainOp != null)
            {
                if (currentFuelStored > 0)
                {
                    currentFuelStored--;

                    var radius = Mathf.Min(currentPlacementRadius, maxRadius);
                    var offset = new Vector3(Mathf.Sin(currentPlacementAngle) * radius, 0.0f, Mathf.Cos(currentPlacementAngle) * radius);
                    var position = transform.position + offset;

                    var terrainOpGO = Instantiate(flattenTerrainOp.gameObject, position, Quaternion.identity);
                    flattenPlaceEffect?.Create(position, Quaternion.identity, terrainOpGO.transform);

                    currentPlacementAngle += (currentPlacementRadius > 0.0f) ? placementSpacing / currentPlacementRadius : placementSpacing;
                    currentPlacementRadius = placementRadiusScale * currentPlacementAngle;
                }
            }
        }

        internal void TakeFromPrivateArea(PrivateArea privateArea)
        {
            circleProjector = privateArea.m_areaMarker;
            circleProjector.gameObject.SetActive(value: true);

            enabledEffect = privateArea.m_enabledEffect;
            model = privateArea.m_model;

            Material[] materials = model.materials;
            Color glowColor = new Color(0.3f, 0.3f, 1.0f);
            foreach (Material material in materials)
            {
                string lookFor = "Guardstone_OdenGlow_mat";
                if (material.name.StartsWith(lookFor))
                {
                    material.SetColor("_EmissionColor", glowColor);
                }
            }

            GameObject sparcsGameObject = enabledEffect.transform.Find("sparcs").gameObject;
            ParticleSystem sparcs = sparcsGameObject.GetComponent<ParticleSystem>();

            ParticleSystem.ShapeModule sparcsShape = sparcs.shape;
            Vector3 sparcsScale = sparcsShape.scale;
            sparcsScale.y = 0.5f;
            ParticleSystem.MainModule sparcsMain = sparcs.main;
            sparcsMain.startColor = new ParticleSystem.MinMaxGradient(glowColor, glowColor * 0.15f);

            GameObject lightGO = enabledEffect.transform.Find("Point light").gameObject;
            Light light = lightGO.GetComponent<Light>();
            light.color = new Color(0.0f, 0.0f, 0.8f, 0.4f);
            light.intensity = 3.0f;

            circleProjector.m_radius = maxRadius + 1.5f;
            circleProjector.m_nrOfSegments = Mathf.CeilToInt(circleProjector.m_radius * 4.0f);
            sparcsScale.x = maxRadius;
            sparcsScale.z = maxRadius;
            light.range = maxRadius;

            GameObject flareGameObject = enabledEffect.transform.Find("flare").gameObject;
            ParticleSystem flare = flareGameObject.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule flareMain = flare.main;
            flareMain.startColor = new ParticleSystem.MinMaxGradient(new Color(0.3f, 0.3f, 1.0f));
            flareMain.startSize = new ParticleSystem.MinMaxCurve(3.0f);
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return false;
            if (Player.m_localPlayer.InPlaceMode()) return false;

            if (hold)
            {
                if (Time.time - lastUseTime < 1.0f) return false;
                lastUseTime = Time.time;

                return TakeFuel(user, true);
            }

            lastUseTime = Time.time;

            return TakeFuel(user, false);
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (!netView || !netView.IsValid() || Player.m_localPlayer == null) return false;
            if (Player.m_localPlayer.InPlaceMode()) return false;
            if (item == null || item.m_shared.m_name != FuelItemName) return false;

            return TakeFuel(user, false);
        }

        public string GetHoverText()
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append($"{msgFuel}: {currentFuelStored}/{maxFuel}\n");
            stringBuilder.Append($"[<color=yellow><b>$KEY_Use</b></color>] $piece_smelter_add {FuelItemName}\n");
            stringBuilder.Append($"[{msgHold} <color=yellow><b>$KEY_Use</b></color>] $piece_smelter_add {msgAll} {FuelItemName}\n");

            return Localization.instance.Localize(stringBuilder.ToString());
        }

        public string GetHoverName() => "$piece_plateautem";

        private bool TakeFuel(Humanoid user, bool takeAll)
        {
            var inventory = user.GetInventory();
            if (inventory == null) return false;

            var playerFuelCount = inventory.CountItems(FuelItemName);
            if (playerFuelCount <= 0)
            {
                user.Message(MessageHud.MessageType.Center, Localization.instance.Localize(msgNoFuel, FuelItemName));
                return false;
            }

            var fuelStored = netView.GetZDO().GetInt(zdoidFuelStored, 0);
            var spaceRemaining = Mathf.FloorToInt((maxFuel - fuelStored) / (float)fuelPerItem);
            if (spaceRemaining <= 0)
            {
                user.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_cantaddmore", FuelItemName));
                return false;
            }

            var amountToAdd = takeAll ? Mathf.Min(spaceRemaining, playerFuelCount) : 1;
            netView.GetZDO().Set(zdoidFuelStored, fuelStored + amountToAdd * fuelPerItem);
            inventory.RemoveItem(FuelItemName, amountToAdd);

            user.Message(MessageHud.MessageType.Center, Localization.instance.Localize($"$msg_added {amountToAdd} {FuelItemName}"));

            return true;
        }
    }
}
