using System.Collections.Generic;
using BepInEx;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Linq;
using UnityEngine;

namespace Plateautem
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class PlateautemPlugin : BaseUnityPlugin
    {
        public const string PluginName = "Plateautem";
        public const string PluginAuthor = "erkle64";
        public const string PluginGUID = "com."+PluginAuthor+"."+PluginName;
        public const string PluginVersion = "0.0.1";
        
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        private void Awake()
        {
            Jotunn.Logger.LogInfo("Plateautem loading");

            Plateautem.LoadConfig(this);

            Localization.AddTranslation("English", new Dictionary<string, string>
            {
                { "piece_plateautem", "Plateautem" },
                { "piece_plateautem_description", "Terrain flattening totem." },
                { Plateautem.msgNoFuel, "You have no $1" },
                { Plateautem.msgHold, "Hold" },
                { Plateautem.msgAll, "all" },
                { Plateautem.msgFuel, "Fuel" },
            });

            PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;
        }

        private void OnVanillaPrefabsAvailable()
        {
            //AssetBundle assetBundle = AssetUtils.LoadAssetBundleFromResources("plateautem");
            try
            {
                var plateautemPieceConfig = new PieceConfig();
                plateautemPieceConfig.Name = "$piece_plateautem";
                plateautemPieceConfig.PieceTable = "Hammer";
                plateautemPieceConfig.AddRequirement(new RequirementConfig("Wood", 2, 0, true));

                var plateautemPrefab = PrefabManager.Instance.CreateClonedPrefab("erkle_plateautem", "guard_stone");
                Piece piece = plateautemPrefab.GetComponent<Piece>();
                piece.m_name = "$piece_plateautem_name";
                piece.m_description = "$piece_plateautem_description";
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
                    plateautem.TakeFromPrivateArea(privateArea);

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
                //assetBundle?.Unload(false);
            }
        }
    }
}
