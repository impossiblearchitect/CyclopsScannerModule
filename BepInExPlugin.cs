using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Nautilus.Crafting;
using Nautilus.Assets.PrefabTemplates;
using Nautilus.Assets;
using Nautilus.Assets.Gadgets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UWE;
using Nautilus.Extensions;
using Nautilus.Utility;
using CyclopsScannerModule.Extensions;

namespace CyclopsScannerModule
{
    [BepInPlugin("org.ryuugoroshimonogatari.cyclopsscanner", "Cyclops Scanner Module", "0.0.1")]
    [BepInDependency("com.snmodding.nautilus")]
    public partial class BepInExPlugin : BaseUnityPlugin
    {
        private static BepInExPlugin context;

        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> isDebug;
        public static ConfigEntry<Vector3> mapPosition;
        public static ConfigEntry<float> mapSize;
        public static ConfigEntry<Vector3> uiPosition;

        public static void Dbgl(string str = "", LogLevel logLevel = LogLevel.Debug)
        {
            if (isDebug.Value)
                context.Logger.Log(logLevel, str);
        }

        public static readonly PrefabInfo mk1Info = PrefabInfo.WithTechType("CyclopsScannerUpgrade1", "Cyclops Scanner Module MK1",
                "Upgrades the Cyclops sonar into a multimessenger scanner. Replaces the 2D threat display with a 3D topographical map, and displays beacons.")
            .WithIcon(ImageUtils.LoadSpriteFromFile("BepInEx/plugins/CyclopsScannerModule/Assets/Sprite/mk1_icon.png"));
        public static readonly PrefabInfo mk2Info = PrefabInfo.WithTechType("CyclopsScannerUpgrade2", "Cyclops Scanner Module MK2",
                "Embeds complete scanning capabilities into the Cyclops. Replaces the 2D threat display with a 3D topographical map, displays beacons, and adds a full scan console to the cockpit.")
            .WithIcon(ImageUtils.LoadSpriteFromFile("BepInEx/plugins/CyclopsScannerModule/Assets/Sprite/mk2_icon.png"));
        

        private void Awake()
        {
            context = this;
            modEnabled = Config.Bind("General", "Enabled", true, "Enable this mod");
            isDebug = Config.Bind("General", "IsDebug", true, "Enable debug logs");
            mapPosition = Config.Bind("Options", "CyclopsMapPosition", new Vector3(0, 0.1f, -0.15f), "Cyclops topographical map position");
            mapSize = Config.Bind("Options", "CyclopsMapSize", 1.0f, "Cyclops topographical map size");
            uiPosition = Config.Bind("Options", "UIPosition", new Vector3(-0.7f, 0.5f, 0.5f), "UI position");

            Dbgl("Plugin awake");

            CustomPrefab mk1Prefab = new(mk1Info);
            CloneTemplate mk1Clone = new(mk1Info, TechType.CyclopsSonarModule);
            mk1Prefab.SetGameObject(mk1Clone);
            mk1Prefab.SetEquipment(EquipmentType.CyclopsModule);
            mk1Prefab.SetRecipe(new RecipeData() {
                craftAmount = 1,
                Ingredients = new List<Ingredient>()
                {
                    new(TechType.CyclopsSonarModule, 1),
                    new(TechType.Copper, 2),
                    new(TechType.Gold, 1),
                    new(TechType.JeweledDiskPiece, 1)
                }
            }).WithFabricatorType(CraftTree.Type.CyclopsFabricator).WithCraftingTime(2f);
            mk1Prefab.SetPdaGroupCategoryAfter(TechGroup.Cyclops, TechCategory.CyclopsUpgrades, TechType.CyclopsSonarModule);
            mk1Prefab.Register();

            CustomPrefab mk2Prefab = new(mk2Info);
            CloneTemplate mk2Clone = new(mk2Info, TechType.CyclopsSonarModule);
            mk2Prefab.SetGameObject(mk2Clone);
            mk2Prefab.SetEquipment(EquipmentType.CyclopsModule);
            mk2Prefab.SetRecipe(new RecipeData()
            {
                craftAmount = 1,
                Ingredients = new List<Ingredient>()
                {
                    new(mk1Info.TechType, 1),
                    new(TechType.AdvancedWiringKit, 1),
                    new(TechType.MapRoomUpgradeScanSpeed, 2)
                }
            }).WithFabricatorType(CraftTree.Type.CyclopsFabricator).WithCraftingTime(2f);
            mk2Prefab.SetPdaGroupCategoryAfter(TechGroup.Cyclops, TechCategory.CyclopsUpgrades, mk1Info.TechType);
            mk2Prefab.Register();

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);

            // WaitScreenHandler.RegisterLateAsyncLoadTask("Cyclops Scanner Module", AttachMRF);

            // StartCoroutine(AttachMRF());
        }

        private IEnumerator Start()//(WaitScreenHandler.WaitScreenTask _task)
        {
            Dbgl("Getting MapRoomFunctionality prefab");
            var task = PrefabDatabase.GetPrefabForFilenameAsync("Submarine/Build/MapRoomFunctionality.prefab");
            yield return task;
            task.TryGetPrefab(out GameObject prefab);
            if (prefab is null)
            {
                Dbgl("Couldn't get MapRoomFunctionality prefab");
                yield break;
            }

            yield return new WaitUntil(() => LightmappedPrefabs.main);
            LightmappedPrefabs.main.RequestScenePrefab("Cyclops", MkOnSubPrefabLoaded(prefab));
        }

        private static void AttachCOI(GameObject go)
        {
            var coi = go.AddComponent<ChildObjectIdentifier>();
            coi.Id = Guid.NewGuid().ToString();
            coi.ClassId = Guid.NewGuid().ToString();
        }
        
        private static IEnumerable AttachCOIRecursive(GameObject parent) { return AttachCOIRecursive(parent.transform); }
        private static IEnumerable AttachCOIRecursive(Transform parent)
        {
            bool flag = false;
            // Dbgl("AttachCOIRecursive called on parent: " + parent.name);
            foreach (Transform child in parent)
            {
                if (!flag)
                {
                    // Dbgl("Parent: " + parent.name + " has children; adding COI to parent");
                    flag = true;
                    AttachCOI(parent.gameObject);
                }
                foreach (var _void in AttachCOIRecursive(child)) yield return _void;
            }
        }

        private static LightmappedPrefabs.OnPrefabLoaded MkOnSubPrefabLoaded(GameObject mrfPrefab)
        {
            return subPrefab =>
            {
                Dbgl("Cyclops prefab loaded");

                SubRoot subRoot = subPrefab.EnsureComponent<SubRoot>();
                if (subRoot is null)
                {
                    Dbgl("No SubRoot, cannot attach MRF");
                    return;
                }
                GameObject sonarMap = subPrefab.gameObject.SearchChild("SonarMap_Small");

                //Scale Cyclops icon sonar map to match aspect ratios of the real thing
                sonarMap?.SearchChild("CyclopsMini")?.transform.localScale.Scale(new Vector3(0.75f, 5f/6f, 1f));
                


                Dbgl("Attaching MRF clone to Cyclops");
                GameObject go = Instantiate(mrfPrefab, sonarMap.transform, false);
                go.transform.localPosition = new Vector3(0, 0, 0);
                Dbgl("Copying fields to Cyclops MapRoomFunctionality");
                var mrf = subPrefab.gameObject.AddComponent<MapRoomFunctionality>();
                var templateMRF = go.GetComponent<MapRoomFunctionality>();
                foreach (var field in typeof(MapRoomFunctionality).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    field.SetValue(mrf, field.GetValue(templateMRF));
                mrf.roomBlip.blipTransform.gameObject.SetActive(false);

                var pdt = sonarMap.AddComponent<PlayerDistanceTracker>();
                foreach (var field in typeof(PlayerDistanceTracker).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    field.SetValue(pdt, field.GetValue(templateMRF.playerDistanceTracker));
                mrf.playerDistanceTracker = pdt;

                Dbgl("Setting up MiniWorld");
                MiniWorld mw = mrf.miniWorld;
                mw.transform.SetParent(sonarMap.transform, false);
                mw.updatePosition = true;
                Dbgl("Setting MiniWorld local position to " + mapPosition.Value);
                mw.transform.localPosition = mapPosition.Value;
                mw.hologramHolder.transform.localPosition = Vector3.zero;
                
                mw.fadeRadius = mapSize.Value;
                mw.fadeSharpness = 30f;
                
                foreach (var t in AttachCOIRecursive(mw.transform)) continue;

                mw.gameObject.SetActive(false);

                // Scale to match Cyclops sonar display size
                mrf.Scale(1 / (120 * mrf.mapScale));

                var screen = templateMRF.screenRoot;
                screen.transform.SetParent(sonarMap.transform, false);
                var ui = screen.SearchChild("scannerUI");
                ui.transform.localPosition = uiPosition.Value;
                ui.transform.localEulerAngles = new Vector3(0, 270, 0);
                ui.GetComponent<uGUI_MapRoomScanner>().mapRoom = mrf;
                var cullable = ui.SearchChild("scanner_cullable");
                DestroyImmediate(screen.SearchChild("cameraScreen"));
                DestroyImmediate(screen.SearchChild("background"));
                DestroyImmediate(screen.SearchChild("foreground"));

                foreach (GameObject obj in new List<GameObject>() {sonarMap, screen, ui, cullable}) AttachCOI(obj);
                
                screen.SetActive(false);

                Dbgl("Setting up PowerConsumer");

                var pc = sonarMap.AddComponent<PowerConsumer>();
                var templatePC = templateMRF.powerConsumer;
                foreach (var field in typeof(PowerConsumer).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    field.SetValue(pc, field.GetValue(templatePC));
                mrf.powerConsumer = pc;
                mrf.powerConsumer.powerRelay = subRoot.powerRelay;
                
                Dbgl("Setting up StorageContainer");

                // Won't be used (at least for now) but needs to exist to avoid an NRE
                templateMRF.storageContainer.transform.SetParent(sonarMap.transform, false); 
                templateMRF.storageContainer.storageRoot.gameObject.transform.SetParent(sonarMap.transform, false);
                mrf.storageContainer.container = templateMRF.storageContainer.container;

                Dbgl("Setting up UpgradeSlots");

                // Again, won't be used (at least for now) but needs to exist to avoid an NRE
                var slots = templateMRF.transform.SearchChild("UpgradeSlots");
                slots?.SetParent(sonarMap.transform, false);
                AttachCOI(slots.gameObject);
                // foreach ()
                mrf.upgradeSlots = templateMRF.upgradeSlots;

                DestroyImmediate(go);
            };
        }

        [HarmonyDebug]
        [HarmonyEmitIL("./dumps")]
        [HarmonyPatch(typeof(SubRoot), nameof(SubRoot.UpdateSubModules))]
        private static class SubRoot_UpdateSubModules_Patch
        {
            private static int GetScannerModuleLevel(SubRoot inst)
            {
                if (inst?.upgradeConsole?.modules.GetCount(mk2Info.TechType) > 0)
                    return 2;
                else if (inst?.upgradeConsole?.modules.GetCount(mk1Info.TechType) > 0)
                    return 1;
                else
                    return 0;
            }

            private static void UpdateScanner(SubRoot inst) 
            {
                if (inst is null)
                    Dbgl("SubRoot instance is null in UpdateScanner");
                var mrf = inst?.gameObject.GetComponent<MapRoomFunctionality>();
                int level = GetScannerModuleLevel(inst);
                Dbgl($"UpdateScanner called. Scanner module level: {level}");
                bool active = level >= 1;
                bool ui_active = level >= 2;
                mrf.enabled = active;
                mrf.miniWorld.gameObject.SetActive(active);
                mrf.worlddisplay.SetActive(active);
                mrf.screenRoot.SetActive(ui_active);
                mrf.screenRoot.SearchChild("scannerUI").SetActive(ui_active);
                Dbgl("Set screen active: " + mrf.screenRoot.activeSelf);
                Dbgl("Set scanner UI active: " + mrf.screenRoot.SearchChild("scannerUI").activeSelf);
                // SetMRFActive(mrf, level >= 1, ui);
            }
            
            private static readonly MethodInfo m_UpdateScanner = SymbolExtensions.GetMethodInfo<SubRoot>((inst) => UpdateScanner(inst));
            private static readonly MethodInfo m_UpdatePowerRating = AccessTools.Method(typeof(SubRoot), nameof(SubRoot.UpdatePowerRating));

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instrs)
            {
                foreach (var inst in instrs)
                {
                    yield return inst;
                    if (inst.Calls(m_UpdatePowerRating))
                    {
                        // After updating power rating, update scanner functionality
                        yield return new CodeInstruction(OpCodes.Ldarg_0); // Load 'this' (SubRoot)
                        yield return new CodeInstruction(OpCodes.Call, m_UpdateScanner); // Call UpdateScanner
                    }
                }
            }
        }

        [HarmonyDebug]
        [HarmonyEmitIL("./dumps")]
        [HarmonyPatch(typeof(MapRoomFunctionality), nameof(MapRoomFunctionality.UpdateModel))]
        private static class MapRoomFunctionality_UpdateModel_Patch
        {
            private static bool Prefix(MapRoomFunctionality __instance)
            {
                Dbgl("MapRoomFunctionality::UpdateModel: line 0");
                int count = __instance.storageContainer.container.count;
                Dbgl("MapRoomFunctionality::UpdateModel: enter for loop");
                for (int i = 0; i < __instance.upgradeSlots.Length; i++)
                {
                    Dbgl($"MapRoomFunctionality::UpdateModel: line 1, i={i}, count={count}");
                    __instance.upgradeSlots[i].SetActive(i < count);
                }
                Dbgl("MapRoomFunctionality::UpdateModel: after for loop");
                __instance.roomBlip.cameraName.text = Language.main.GetFormat<int>("MapRoomScanningRange", Mathf.RoundToInt(__instance.scanRange));
                Dbgl("MapRoomFunctionality::UpdateModel: method end");
                return false;
            }
        }

        private static MethodInfo m_GetComponentInParent(Type ty) { return AccessTools.Method(typeof(Component), nameof(GetComponentInParent), generics: new Type[] { ty }); }
        private static readonly MethodInfo m_Base_add_onPostRebuildGeometry = AccessTools.Method(typeof(Base), "add_onPostRebuildGeometry");
        private static readonly FieldInfo f_powered = AccessTools.Field(typeof(MapRoomFunctionality), nameof(MapRoomFunctionality.powered));

        [HarmonyDebug]
        [HarmonyEmitIL("./dumps")]
        [HarmonyPatch(typeof(MapRoomFunctionality), nameof(MapRoomFunctionality.Start))]
        private static class MapRoomFunctionality_Start_Patch
        {
            private static bool IsPoweredAndBase(bool powered, Base maybeBase) 
            {
                return powered && maybeBase is not null;
            }


            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instrs, ILGenerator il)
            {
                Dbgl("Transpiling MapRoomFunctionality.Start");
                CodeMatcher m = new(instrs);

                Label l_afterBase = il.DefineLabel();
                LocalBuilder lb_maybeBase = il.DeclareLocal(typeof(Base));
                lb_maybeBase.SetLocalSymInfo("maybeBase");

                m.Start().MatchEndForward(
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Call, m_GetComponentInParent(typeof(Base))) // `this.GetComponentInParent<Base>()`, which returns null on a Cyclops
                ).Advance(1).Insert(
                    // new CodeInstruction(OpCodes.Dup), new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Stloc_S, lb_maybeBase.LocalIndex),
                    new CodeInstruction(OpCodes.Ldloc_S, lb_maybeBase.LocalIndex),
                    new CodeInstruction(OpCodes.Brfalse_S, l_afterBase), // If null, skip
                    new CodeInstruction(OpCodes.Ldloc_S, lb_maybeBase.LocalIndex) // Load `maybeBase` again for further use
                ).MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, m_Base_add_onPostRebuildGeometry) // `.onPostRebuildGeometry += ...`
                ).Advance(1).AddLabels(new List<Label>(){l_afterBase})
                .MatchStartForward(
                    new CodeMatch(OpCodes.Ldfld, f_powered) // `this.powered`
                ).Repeat(m => m.Advance(1).InsertAndAdvance(
                        new CodeInstruction(OpCodes.Ldloc_S, lb_maybeBase.LocalIndex), // Load `maybeBase`
                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MapRoomFunctionality_Start_Patch), nameof(IsPoweredAndBase))) // Call IsPoweredAndBase(this.powered, maybeBase)
                    )
                );

                return m.InstructionEnumeration();
            }
        }



        private static readonly FieldInfo f_scanRange = AccessTools.Field(typeof(MapRoomFunctionality), nameof(MapRoomFunctionality.scanRange));
        private static readonly MethodInfo m_get_mapScale = AccessTools.PropertyGetter(typeof(MapRoomFunctionality), nameof(MapRoomFunctionality.mapScale));

        [HarmonyDebug]
        [HarmonyEmitIL("./dumps")]
        [HarmonyPatch(typeof(MapRoomFunctionality), nameof(MapRoomFunctionality.UpdateScanning))]
        private static class MapRoomFunctionality_UpdateScanning_Patch
        {
            private static readonly MethodInfo m_fixFade = AccessTools.Method(typeof(MapRoomFunctionality_UpdateScanning_Patch), nameof(FixFade));

            private static float FixFade(float fade, MapRoomFunctionality mrf)
            {
                Dbgl($"FixFade called. Original fade: {fade}");
                // If on Cyclops, fix the culling fade at the available space
                return (mrf.gameObject.GetComponent<SubRoot>() is not null) ? mapSize.Value : fade;
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instrs)
            {
                Dbgl("Transpiling MapRoomFunctionality.UpdateScanning");
                CodeMatcher m = new(instrs);
                m.Start().MatchEndForward(
                    // `... this.scanRange * this.mapScale ...`
                    new CodeMatch(OpCodes.Ldarg_0), new CodeMatch(OpCodes.Ldfld, f_scanRange),
                    new CodeMatch(OpCodes.Ldarg_0), new CodeMatch(OpCodes.Call, m_get_mapScale),
                    new CodeMatch(OpCodes.Mul)
                ).Advance(1).Insert(
                        new CodeInstruction(OpCodes.Ldarg_0), // Load 'this' (MapRoomFunctionality)
                        new CodeInstruction(OpCodes.Call, m_fixFade)
                    );
                return m.InstructionEnumeration();
            }
        }


        // [HarmonyDebug]
        // [HarmonyEmitIL("./dumps")]
        [HarmonyPatch(typeof(SubRoot), nameof(SubRoot.SetCyclopsUpgrades))]
        private class SubRoot_SetCyclopsUpgrades_Patch
        {
            private static readonly MethodInfo m_countScannerAsSonar = AccessTools.Method(typeof(SubRoot_SetCyclopsUpgrades_Patch), nameof(CountScannerAsSonar));
            private static void CountScannerAsSonar (SubRoot inst, TechType techTypeInSlot)
            {
                if (techTypeInSlot == mk1Info.TechType || techTypeInSlot == mk2Info.TechType)
                {
                    inst.sonarUpgrade = true;
                    inst.sonarPowerCost = 0; // scanner already consumes power
                }
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instrs)
            {
                Dbgl("Transpiling SubRoot.SetCyclopsUpgrades");
                CodeMatcher m = new(instrs);
                m.Start().MatchStartForward(new CodeMatch(OpCodes.Switch))
                    .MatchStartBackwards(new CodeMatch(OpCodes.Ldloc_S)); // Find load local for operand of `switch`
                if (m.Operand is LocalBuilder techTypeInSlotLocal)
                {
                    m.InsertAndAdvance(
                        new CodeInstruction(OpCodes.Ldarg_0), // Load 'this' (SubRoot)
                        new CodeInstruction(OpCodes.Ldloc_S, techTypeInSlotLocal.LocalIndex), // Load local `techTypeInSlot`
                        new CodeInstruction(OpCodes.Call, m_countScannerAsSonar) // Call CountScannerAsSonar(this, techTypeInSlot)
                    );
                }
                return m.InstructionEnumeration();
                
            }
        }
        
    }
}