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
        public const string PluginVersion = "0.1.0";
        
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        private static ConfigEntry<string> configRequirements;

        private void Awake()
        {
            Jotunn.Logger.LogInfo("Plateautem loading");

            LoadConfig();

            Localization.AddTranslation("English", new Dictionary<string, string>
            {
                { "piece_plateautem", "Plateautem" },
                { "piece_plateautem_description", "Terrain flattening totem." },
                { Plateautem.msgNoFuel, "You have no $1" },
                { Plateautem.msgHold, "Hold" },
                { Plateautem.msgAll, "all" },
                { Plateautem.msgFuel, "Fuel" },
                { Plateautem.msgRadius, "Radius" },
                { Plateautem.msgResetScan, "Reset scan" },
                { Plateautem.msgSelectFuel, "Select fuel to insert" },
                { Plateautem.msgEjectFuel, "Eject fuel" },
                { Plateautem.msgEjectStone, "Eject stone" },
            });

            PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;
        }

        private void LoadConfig()
        {
            configRequirements = Config.Bind("Server", "Requirements", defaultValue: "Wood*5,Stone*2", new ConfigDescription("Requirements for crafting.  Use Prefab column at https://valheim-modding.github.io/Jotunn/data/objects/item-list.html", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));

            Plateautem.LoadConfig(this);
        }

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
                            int reqirementCount = 1;
                            if (match.Groups.Count >= 3 && !string.IsNullOrWhiteSpace(match.Groups[2].Value))
                            {
                                reqirementCount = int.Parse(match.Groups[2].Value);
                            }
                            plateautemPieceConfig.AddRequirement(new RequirementConfig(requirementName, reqirementCount, 0, true));
                            Jotunn.Logger.LogInfo($"Adding {requirementName}x{reqirementCount} as requirement for Plateautem.");
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
