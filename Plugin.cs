using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace JackedKnife
{
    [BepInPlugin("com.jackedknife", "JackedKnife Mod", "1.0.0")]
    public class JackedKnifePlugin : BaseUnityPlugin
    {
        public static JackedKnifePlugin Instance;
        public static new ManualLogSource Logger;

        public static ConfigEntry<bool> Enable10000xRepair;
        public static ConfigEntry<bool> EnableFixEverything;
        public static ConfigEntry<bool> EnableMitosis;
        public static ConfigEntry<bool> EnableFragmentation;

        public static readonly AccessTools.FieldRef<Repairer, IRepairable> RepairInProgressRef = AccessTools.FieldRefAccess<Repairer, IRepairable>("repairInProgress");
        public static readonly AccessTools.FieldRef<Repairer, Unit> UnitToRepairRef = AccessTools.FieldRefAccess<Repairer, Unit>("unitToRepair");
        public static readonly AccessTools.FieldRef<Repairer, float> RepairRateRef = AccessTools.FieldRefAccess<Repairer, float>("repairRate");
        public static readonly AccessTools.FieldRef<Repairer, float> RadiusRef = AccessTools.FieldRefAccess<Repairer, float>("radius");
        public static readonly AccessTools.FieldRef<Repairer, Unit> AttachedUnitRef = AccessTools.FieldRefAccess<Repairer, Unit>("attachedUnit");
        public delegate void InitializeUnitDelegate(Unit instance);
        public static InitializeUnitDelegate InitializeUnit = AccessTools.MethodDelegate<InitializeUnitDelegate>(AccessTools.Method(typeof(Unit), "InitializeUnit"));

        void Awake()
        {
            Instance = this;
            Logger = base.Logger;

            Enable10000xRepair = Config.Bind("Features", "Repair speed times 10000", true, "Makes the Jackknife repair extremely fast.");
            EnableFixEverything = Config.Bind("Features", "Fix Everything", true, "Allows Jackknife to repair scenery, buildings, and other non-standard objects.");
            EnableFragmentation = Config.Bind("Features", "Fragmentation", true, "Spawns 2 Jackknifes when one is destroyed.");
            EnableMitosis = Config.Bind("Features", "Mitosis", true, "Spawns 1 Jackknife when a repair is completed.");

            var harmony = new Harmony("com.jackedknife");
            harmony.PatchAll(typeof(JackedKnifePlugin));

            Logger.LogInfo("JackedKnife loaded successfully.");
        }

        [HarmonyPatch(typeof(Repairer), "Awake")]
        [HarmonyPostfix]
        static void Repairer_Awake_Postfix(Repairer __instance)
        {
            var attached = JackedKnifePlugin.AttachedUnitRef(__instance);
            if (attached == null) return;
            var def = attached.definition;
            if (def == null || def.name.IndexOf("UGVDozer1", StringComparison.Ordinal) < 0) return;

            if (Enable10000xRepair.Value)
            {
                JackedKnifePlugin.RepairRateRef(__instance) *= 10000f;
            }

            if (__instance.gameObject.GetComponent<JackknifeFixerController>() == null)
            {
                var fixer = __instance.gameObject.AddComponent<JackknifeFixerController>();
                fixer.repairer = __instance;
            }
        }

        [HarmonyPatch(typeof(Scenery), "Awake")]
        [HarmonyPostfix]
        static void Scenery_Awake_Postfix(Scenery __instance)
        {
            if (__instance.gameObject.GetComponent<SceneryRepairProxy>() == null)
            {
                var proxy = __instance.gameObject.AddComponent<SceneryRepairProxy>();
                proxy.scenery = __instance;
            }
        }

        [HarmonyPatch(typeof(GroundVehicle), "UnitDisabled")]
        [HarmonyPostfix]
        static void GroundVehicle_UnitDisabled_Postfix(GroundVehicle __instance, bool oldState, bool newState)
        {
            if (!EnableFragmentation.Value || GameManager.gameState == GameState.Encyclopedia) return;
            if (oldState || !newState || !__instance.IsServer) return;

            var def = __instance.definition;
            if (def != null && def.name.IndexOf("UGVDozer1", StringComparison.Ordinal) >= 0)
            {
                DoFragmentation(__instance);
            }
        }

        static void DoFragmentation(GroundVehicle vehicle)
        {
            if (vehicle == null || Spawner.i == null) return;

            FactionHQ hq = vehicle.MapHQ ?? vehicle.NetworkHQ;
            
            for (int i = 0; i < 2; i++)
            {
                Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * 15f;
                Vector3 localOffset = new Vector3(randomCircle.x, 2f, randomCircle.y);
                GlobalPosition spawnPos = GlobalPositionExtensions.ToGlobalPosition(vehicle.transform.position) + localOffset;
                
                try
                {
                    GroundVehicle spawned = Spawner.i.SpawnVehicle(vehicle.definition.unitPrefab, spawnPos, vehicle.transform.rotation, Vector3.zero, hq, $"Mitosis_{Guid.NewGuid().ToString().Substring(0,4)}", 1f, false, null);
                    if (spawned != null)
                    {
                        var initMethod = AccessTools.Method(typeof(Unit), "InitializeUnit");
                        if (initMethod != null)
                        {
                            initMethod.Invoke(spawned, null);
                        }
                    }
                }
                catch (Exception e)
                {
                    JackedKnifePlugin.Logger.LogError($"Error in DoFragmentation: {e}");
                }
            }
        }

        public static void SpawnJackknife(GroundVehicle vehicle)
        {
            if (vehicle == null || Spawner.i == null) return;

            FactionHQ hq = vehicle.MapHQ ?? vehicle.NetworkHQ;
            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * 10f;
            Vector3 localOffset = new Vector3(randomCircle.x, 2f, randomCircle.y);
            GlobalPosition spawnPos = GlobalPositionExtensions.ToGlobalPosition(vehicle.transform.position) + localOffset;

            try
            {
                GroundVehicle spawned = Spawner.i.SpawnVehicle(vehicle.definition.unitPrefab, spawnPos, vehicle.transform.rotation, Vector3.zero, hq, $"Mitosis_{Guid.NewGuid().ToString().Substring(0,4)}", 1f, false, null);
                if (spawned != null && InitializeUnit != null)
                {
                    InitializeUnit(spawned);
                }
            }
            catch (Exception e)
            {
                JackedKnifePlugin.Logger.LogError($"Error in SpawnJackknife: {e}");
            }
        }
    }

    public class SceneryRepairProxy : MonoBehaviour, IRepairable
    {
        public Scenery scenery;
        public float GetRepairPriority(GlobalPosition position, float distance) => 1f;
        
        public bool NeedsRepair()
        {
            return scenery != null && scenery.disabled;
        }

        public void Repair(Unit repairerUnit, float value)
        {
            if (scenery != null && scenery.disabled)
            {
                scenery.disabled = false;
                
                var method = AccessTools.Method(typeof(Unit), "UnitDisabled");
                if (method != null)
                {
                    method.Invoke(scenery, new object[] { true, false });
                }
            }
        }

        public void OnRepairComplete()
        {
        }
    }

    public class JackknifeFixerController : MonoBehaviour
    {
        public Repairer repairer;
        private float timer = 0f;
        private IRepairable lastRepair = null;

        void Update()
        {
            if (repairer == null) return;
            var attached = JackedKnifePlugin.AttachedUnitRef(repairer);
            if (attached == null || attached.disabled) return;

            var inProgress = JackedKnifePlugin.RepairInProgressRef(repairer);

            if (lastRepair != null && inProgress == null)
            {
                try
                {
                    if (!lastRepair.NeedsRepair())
                    {
                        if (JackedKnifePlugin.EnableMitosis.Value)
                        {
                            JackedKnifePlugin.SpawnJackknife(attached as GroundVehicle);
                        }
                    }
                }
                catch { } // Suppress errors if the target was completely destroyed and accessing it threw a NullReferenceException
                lastRepair = null;
            }
            else if (inProgress != null)
            {
                lastRepair = inProgress;
            }

            if (!JackedKnifePlugin.EnableFixEverything.Value) return;

            if (inProgress != null) return;

            timer += Time.deltaTime;
            if (timer >= 1f)
            {
                timer = 0f;
                float r = JackedKnifePlugin.RadiusRef(repairer);
                if (r <= 0f) r = 60f;
                Collider[] cols = Physics.OverlapSphere(transform.position, r);
                foreach (var col in cols)
                {
                    // 1. Try Scenery
                    var proxy = col.GetComponentInParent<SceneryRepairProxy>();
                    if (proxy != null && proxy.NeedsRepair())
                    {
                        JackedKnifePlugin.RepairInProgressRef(repairer) = proxy;
                        JackedKnifePlugin.UnitToRepairRef(repairer) = proxy.scenery;
                        break;
                    }
                    
                    // 2. Try Generic Units that might not be targeted naturally
                    var unit = col.GetComponentInParent<Unit>();
                    if (unit != null && unit.disabled)
                    {
                        var ir = unit.GetComponent<IRepairable>();
                        if (ir != null && ir.NeedsRepair())
                        {
                            JackedKnifePlugin.RepairInProgressRef(repairer) = ir;
                            JackedKnifePlugin.UnitToRepairRef(repairer) = unit;
                            break;
                        }
                    }
                }
            }
        }
    }
}
