using System.Collections.Generic;
using BepInEx;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Linq;
using UnityEngine;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using System.Globalization;

namespace Plateautem
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class Plugin : BaseUnityPlugin
    {
        public const string PluginName = "Plateautem";
        public const string PluginAuthor = "erkle64";
        public const string PluginGUID = "com."+PluginAuthor+"."+PluginName;
        public const string PluginVersion = "0.3.8";
        
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        private static ConfigEntry<string> configRequirements;

        private void Awake()
        {
            Jotunn.Logger.LogInfo("Plateautem loading");

            LoadConfig();
            Plateautem.RegisterRPCs();

            Localization.AddTranslation("English", new Dictionary<string, string>
            {
                { "piece_plateautem", "Plateautem" },
                { "piece_plateautem_description", "Terrain flattening totem." },
                { Plateautem.msgNoFuel, "You have no $1" },
                { Plateautem.msgHold, "Hold" },
                { Plateautem.msgAll, "all" },
                { Plateautem.msgFuel, "Fuel" },
                { Plateautem.msgTools, "Tools" },
                { Plateautem.msgRadius, "Radius" },
                { Plateautem.msgResetScan, "Reset scan" },
                { Plateautem.msgSelectFuel, "Select item to insert" },
                { Plateautem.msgEject, "Eject" },
                { Plateautem.msgEjectFuel, "Eject fuel" },
                { Plateautem.msgEjectStone, "Eject stone" },
                { Plateautem.msgSelectMode, "Insert item set to $1\nPress or hold E to insert $1." },
            });

            PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;

            Harmony.CreateAndPatchAll(this.GetType().Assembly);
            //Harmony.CreateAndPatchAll(typeof(Plateautem.Patch));
        }

        private void LoadConfig()
        {
            configRequirements = Config.Bind("Server", "Requirements", defaultValue: "Wood*5,Stone*2", new ConfigDescription("Requirements for crafting.  Use Prefab column at https://valheim-modding.github.io/Jotunn/data/objects/item-list.html", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));

            Plateautem.LoadConfig(this);
        }

#if DEBUG
        private void Update()
        {
            if (UnityInput.Current.GetKeyDown(KeyCode.Keypad5))
            {
                Jotunn.Logger.LogInfo("Activating debug batch");
                Console.instance.TryRunCommand("devcommands");
                Console.instance.TryRunCommand("debugmode");
                Console.instance.TryRunCommand("env Clear");
                Console.instance.TryRunCommand("tod 0.5");
            }
        }
#endif

        private void OnVanillaPrefabsAvailable()
        {
            Plateautem.LoadFuelItems();

            AssetBundle assetBundle = AssetUtils.LoadAssetBundleFromResources("plateautem");
            try
            {
                var plateautemPieceConfig = new PieceConfig();
                plateautemPieceConfig.Name = "$piece_plateautem";
                plateautemPieceConfig.PieceTable = "Hammer";

                var requirementRegex = new Regex(@"\s*([^*,\s]+)(?:\s*\*\s*(\d+))?\s*(?:,|$)");
                var requirementMatches = requirementRegex.Matches(configRequirements.Value);
                if (requirementMatches.Count > 0)
                {
                    for (int i = 0; i < requirementMatches.Count; ++i)
                    {
                        var match = requirementMatches[i];
                        if (match.Groups.Count >= 2)
                        {
                            string requirementName = match.Groups[1].Value;
                            int requirementCount = 1;
                            if (match.Groups.Count >= 3 && !string.IsNullOrWhiteSpace(match.Groups[2].Value))
                            {
                                try
                                {
                                    requirementCount = int.Parse(match.Groups[2].Value);
                                }
                                catch
                                {
                                    try
                                    {
                                        requirementCount = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                                    }
                                    catch
                                    {
                                        requirementCount = 1;
                                    }
                                }
                            }
                            plateautemPieceConfig.AddRequirement(new RequirementConfig(requirementName, requirementCount, 0, true));
                            Jotunn.Logger.LogInfo($"Adding {requirementName}x{requirementCount} as requirement for Plateautem.");
                        }
                    }
                }
                else
                {
                    plateautemPieceConfig.AddRequirement(new RequirementConfig("Wood", 2, 0, true));
                }

                var plateautemPrefab = PrefabManager.Instance.CreateClonedPrefab("erkle_plateautem", "guard_stone");
                Piece piece = plateautemPrefab.GetComponent<Piece>();
                piece.m_name = "$piece_plateautem_name";
                piece.m_description = "$piece_plateautem_description";
                piece.m_icon = assetBundle.LoadAsset<Sprite>("plateautem");
                piece.m_clipGround = true;
                piece.m_groundOnly = true;
                piece.m_noInWater = true;
                foreach (GuidePoint guidePoint in plateautemPrefab.GetComponentsInChildren<GuidePoint>().ToArray())
                {
                    DestroyImmediate(guidePoint);
                }

                Plateautem plateautem = plateautemPrefab.AddComponent<Plateautem>();
                PrivateArea privateArea = plateautemPrefab.GetComponent<PrivateArea>();
                if (privateArea != null)
                {
                    plateautem.BuildPrefab(privateArea, assetBundle);

                    DestroyImmediate(privateArea);
                }

                PieceManager.Instance.AddPiece(new CustomPiece(plateautemPrefab, fixReference: false, plateautemPieceConfig));
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"Error while adding cloned item: {ex}");
            }
            finally
            {
                PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabsAvailable;
                assetBundle?.Unload(false);
            }
        }
    }
}
