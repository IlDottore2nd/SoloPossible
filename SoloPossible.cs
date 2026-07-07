using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SoloPossible
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class SoloPossiblePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "dottore.solopossible";
        public const string PluginName = "SoloPossible";
        public const string PluginVersion = "1.0.1";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> EnableMod;

        internal static ConfigEntry<float> ItemWeightMultiplier;
        internal static ConfigEntry<float> SprintSpeedMultiplier;
        internal static ConfigEntry<float> StaminaRegenMultiplier;

        internal static ConfigEntry<int> MaxHealth;
        internal static ConfigEntry<int> ShipRegenAmount;
        internal static ConfigEntry<float> ShipRegenInterval;

        internal static ConfigEntry<int> InventorySlots;

        private Harmony harmony;
        private bool patchesApplied;

        private void Awake()
        {
            Log = Logger;

            EnableMod = Config.Bind("General", "EnableMod", true, "Enables SoloPossible.");

            ItemWeightMultiplier = Config.Bind("Balance", "ItemWeightMultiplier", 0.85f, "Item weight multiplier. 0.85 = 15% lighter.");
            SprintSpeedMultiplier = Config.Bind("Balance", "SprintSpeedMultiplier", 1.03f, "Sprint speed multiplier. 1.03 = 3% faster.");
            StaminaRegenMultiplier = Config.Bind("Balance", "StaminaRegenMultiplier", 1.03f, "Stamina recovery multiplier. 1.03 = 3% faster recovery.");

            MaxHealth = Config.Bind("Health", "MaxHealth", 150, "Health given at the start of each solo life.");
            ShipRegenAmount = Config.Bind("Health", "ShipRegenAmount", 5, "HP regenerated per interval while inside the ship.");
            ShipRegenInterval = Config.Bind("Health", "ShipRegenInterval", 5f, "Seconds between ship-only healing ticks.");

            InventorySlots = Config.Bind("Inventory", "InventorySlots", 5, "Total inventory slots when playing solo. Vanilla is 4. SoloPossible default is 5.");

            harmony = new Harmony(PluginGuid);

            if (!EnableMod.Value)
            {
                Log.LogInfo($"{PluginName} {PluginVersion} loaded but disabled by config. No patches applied.");
                return;
            }

            harmony.PatchAll();
            patchesApplied = true;

            Log.LogInfo($"{PluginName} {PluginVersion} loaded and patched.");
        }

        private void OnDestroy()
        {
            try
            {
                InventoryManager.ForceVanillaInventoryAndHud();
                WeightManager.RestoreAllWeights();
            }
            catch
            {
                // Ignore cleanup errors during game shutdown.
            }

            if (patchesApplied && harmony != null)
                harmony.UnpatchSelf();
        }

        internal static bool EffectsAllowed()
        {
            return EnableMod.Value && IsSoloGame();
        }

        internal static bool IsSoloGame()
        {
            StartOfRound round = StartOfRound.Instance;

            if (round == null)
                return false;

            // In Lethal Company, connectedPlayersAmount is normally 0 when the host is alone.
            return round.connectedPlayersAmount <= 0;
        }

        internal static bool IsLocalPlayer(PlayerControllerB player)
        {
            return player != null
                && StartOfRound.Instance != null
                && StartOfRound.Instance.localPlayerController == player;
        }

        internal static bool IsLocalPlayerAllowed(PlayerControllerB player)
        {
            return IsLocalPlayer(player) && EffectsAllowed();
        }

        internal static int WantedInventorySlots()
        {
            if (!EffectsAllowed())
                return 4;

            return Mathf.Max(4, InventorySlots.Value);
        }
    }

    internal static class InventoryManager
    {
        private static readonly MethodInfo SwitchToItemSlotMethod =
            AccessTools.Method(typeof(PlayerControllerB), "SwitchToItemSlot");

        private static float nextHudMaintenanceTime;

        internal static void TickHudMaintenance()
        {
            if (Time.realtimeSinceStartup < nextHudMaintenanceTime)
                return;

            nextHudMaintenanceTime = Time.realtimeSinceStartup + 0.5f;

            ResizeAllPlayerInventories();

            if (HUDManager.Instance != null)
                ResizeHudInventory(HUDManager.Instance);
        }

        internal static void ForceVanillaInventoryAndHud()
        {
            ResizeAllPlayerInventoriesToSize(4);

            if (HUDManager.Instance != null)
                ResizeHudInventoryToSize(HUDManager.Instance, 4);
        }

        internal static void ResizePlayerInventory(PlayerControllerB player)
        {
            ResizePlayerInventoryToSize(player, SoloPossiblePlugin.WantedInventorySlots());
        }

        internal static void ResizeAllPlayerInventories()
        {
            ResizeAllPlayerInventoriesToSize(SoloPossiblePlugin.WantedInventorySlots());
        }

        private static void ResizeAllPlayerInventoriesToSize(int wantedSlots)
        {
            PlayerControllerB[] players = Object.FindObjectsOfType<PlayerControllerB>();

            foreach (PlayerControllerB player in players)
                ResizePlayerInventoryToSize(player, wantedSlots);
        }

        private static void ResizePlayerInventoryToSize(PlayerControllerB player, int wantedSlots)
        {
            if (player == null)
                return;

            wantedSlots = Mathf.Max(4, wantedSlots);

            if (player.ItemSlots != null && player.ItemSlots.Length == wantedSlots)
                return;

            GrabbableObject[] oldSlots = player.ItemSlots ?? new GrabbableObject[4];
            GrabbableObject[] newSlots = new GrabbableObject[wantedSlots];

            int copyLength = Mathf.Min(oldSlots.Length, newSlots.Length);

            for (int i = 0; i < copyLength; i++)
                newSlots[i] = oldSlots[i];

            player.ItemSlots = newSlots;
        }

        internal static void ResizeHudInventory(HUDManager hud)
        {
            ResizeHudInventoryToSize(hud, SoloPossiblePlugin.WantedInventorySlots());
        }

        private static void ResizeHudInventoryToSize(HUDManager hud, int wantedSlots)
        {
            if (hud == null)
                return;

            wantedSlots = Mathf.Max(4, wantedSlots);

            GameObject inventoryRoot = GameObject.Find("Systems/UI/Canvas/IngamePlayerHUD/Inventory");

            if (inventoryRoot == null)
            {
                SoloPossiblePlugin.Log.LogWarning("Could not find inventory HUD root.");
                return;
            }

            if (hud.itemSlotIcons == null || hud.itemSlotIconFrames == null)
            {
                SoloPossiblePlugin.Log.LogWarning("HUD inventory icon arrays were null.");
                return;
            }

            if (hud.itemSlotIcons.Length < 4 || hud.itemSlotIconFrames.Length < 4)
            {
                SoloPossiblePlugin.Log.LogWarning("HUD inventory icon arrays were smaller than expected.");
                return;
            }

            CleanupExtraHudSlots(inventoryRoot.transform, wantedSlots);

            if (hud.itemSlotIcons.Length == wantedSlots
                && hud.itemSlotIconFrames.Length == wantedSlots)
            {
                CenterHudSlots(inventoryRoot.transform, wantedSlots);
                return;
            }

            Image[] newIcons = new Image[wantedSlots];
            Image[] newFrames = new Image[wantedSlots];

            int vanillaSlots = Mathf.Min(4, Mathf.Min(hud.itemSlotIcons.Length, hud.itemSlotIconFrames.Length));

            for (int i = 0; i < vanillaSlots; i++)
            {
                newIcons[i] = hud.itemSlotIcons[i];
                newFrames[i] = hud.itemSlotIconFrames[i];
            }

            if (wantedSlots > 4)
            {
                GameObject templateSlot = GameObject.Find("Systems/UI/Canvas/IngamePlayerHUD/Inventory/Slot3");

                if (templateSlot == null)
                {
                    SoloPossiblePlugin.Log.LogWarning("Could not find Slot3 HUD template.");
                    return;
                }

                Transform lastSlotTransform = templateSlot.transform;

                for (int i = 4; i < wantedSlots; i++)
                {
                    GameObject existingSlot = GameObject.Find($"Systems/UI/Canvas/IngamePlayerHUD/Inventory/Slot{i}");
                    GameObject newSlot;

                    if (existingSlot != null)
                    {
                        newSlot = existingSlot;
                        newSlot.SetActive(true);
                    }
                    else
                    {
                        newSlot = Object.Instantiate(templateSlot, inventoryRoot.transform);
                        newSlot.name = $"Slot{i}";
                    }

                    Vector3 lastPosition = lastSlotTransform.localPosition;

                    newSlot.transform.SetLocalPositionAndRotation(
                        new Vector3(lastPosition.x + 50f, lastPosition.y, lastPosition.z),
                        lastSlotTransform.localRotation
                    );

                    lastSlotTransform = newSlot.transform;

                    newFrames[i] = newSlot.GetComponent<Image>();

                    if (newSlot.transform.childCount > 0)
                        newIcons[i] = newSlot.transform.GetChild(0).GetComponent<Image>();
                }
            }

            hud.itemSlotIcons = newIcons;
            hud.itemSlotIconFrames = newFrames;

            CenterHudSlots(inventoryRoot.transform, wantedSlots);

            SoloPossiblePlugin.Log.LogInfo($"Inventory HUD resized to {wantedSlots} slots.");
        }

        private static void CleanupExtraHudSlots(Transform inventoryRoot, int wantedSlots)
        {
            if (inventoryRoot == null)
                return;

            for (int i = inventoryRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = inventoryRoot.GetChild(i);

                if (!child.name.StartsWith("Slot"))
                    continue;

                string numberText = child.name.Replace("Slot", "");

                int slotIndex;

                if (!int.TryParse(numberText, out slotIndex))
                    continue;

                if (slotIndex >= wantedSlots && slotIndex >= 4)
                {
                    child.gameObject.SetActive(false);
                    Object.Destroy(child.gameObject);
                }
            }
        }

        private static void CenterHudSlots(Transform inventoryRoot, int slotCount)
        {
            if (inventoryRoot == null)
                return;

            float spacing = 50f;
            float centerOffset = (slotCount - 1) / 2f;

            for (int i = 0; i < slotCount; i++)
            {
                Transform slot = inventoryRoot.Find($"Slot{i}");

                if (slot == null && i < inventoryRoot.childCount)
                    slot = inventoryRoot.GetChild(i);

                if (slot == null)
                    continue;

                Vector3 oldPosition = slot.localPosition;

                slot.localPosition = new Vector3(
                    spacing * (i - centerOffset),
                    oldPosition.y,
                    oldPosition.z
                );
            }
        }

        internal static void HandleFifthSlotHotkey(PlayerControllerB player)
        {
            if (!SoloPossiblePlugin.IsLocalPlayerAllowed(player))
                return;

            if (player.isPlayerDead)
                return;

            if (player.ItemSlots == null || player.ItemSlots.Length < 5)
                return;

            if (Keyboard.current == null)
                return;

            if (!Keyboard.current.digit5Key.wasPressedThisFrame && !Keyboard.current.numpad5Key.wasPressedThisFrame)
                return;

            SwitchToSlot(player, 4);
        }

        private static void SwitchToSlot(PlayerControllerB player, int slot)
        {
            if (SwitchToItemSlotMethod == null)
            {
                SoloPossiblePlugin.Log.LogWarning("Could not find PlayerControllerB.SwitchToItemSlot.");
                return;
            }

            ParameterInfo[] parameters = SwitchToItemSlotMethod.GetParameters();

            try
            {
                if (parameters.Length == 1)
                {
                    SwitchToItemSlotMethod.Invoke(player, new object[] { slot });
                }
                else
                {
                    SwitchToItemSlotMethod.Invoke(player, new object[] { slot, null });
                }
            }
            catch
            {
                SoloPossiblePlugin.Log.LogWarning($"Failed to switch to inventory slot {slot}.");
            }
        }
    }

    internal static class WeightManager
    {
        private static readonly Dictionary<Item, float> OriginalWeights = new Dictionary<Item, float>();
        private static bool lastAllowedState = false;

        internal static void RegisterAndApply(GrabbableObject grabbable)
        {
            if (grabbable == null || grabbable.itemProperties == null)
                return;

            Item item = grabbable.itemProperties;

            if (!OriginalWeights.ContainsKey(item))
                OriginalWeights[item] = item.weight;

            ApplyToItem(item);
        }

        internal static void RestoreAllWeights()
        {
            foreach (KeyValuePair<Item, float> pair in OriginalWeights)
            {
                if (pair.Key != null)
                    pair.Key.weight = pair.Value;
            }

            lastAllowedState = false;
        }

        internal static void RefreshAll()
        {
            lastAllowedState = SoloPossiblePlugin.EffectsAllowed();

            foreach (Item item in OriginalWeights.Keys)
                ApplyToItem(item);
        }

        internal static void RefreshAllIfNeeded()
        {
            bool allowed = SoloPossiblePlugin.EffectsAllowed();

            if (allowed == lastAllowedState)
                return;

            lastAllowedState = allowed;

            foreach (Item item in OriginalWeights.Keys)
                ApplyToItem(item);
        }

        private static void ApplyToItem(Item item)
        {
            if (item == null || !OriginalWeights.ContainsKey(item))
                return;

            float originalWeight = OriginalWeights[item];

            if (!SoloPossiblePlugin.EffectsAllowed())
            {
                item.weight = originalWeight;
                return;
            }

            float addedWeight = Mathf.Max(0f, originalWeight - 1f);
            item.weight = 1f + addedWeight * SoloPossiblePlugin.ItemWeightMultiplier.Value;
        }
    }

    internal static class HealthManager
    {
        private static readonly HashSet<ulong> InitializedPlayers = new HashSet<ulong>();
        private static float regenTimer;

        internal static void ResetLifeTracking()
        {
            InitializedPlayers.Clear();
            regenTimer = 0f;
        }

        internal static void Tick(PlayerControllerB player)
        {
            if (!SoloPossiblePlugin.IsLocalPlayerAllowed(player))
                return;

            if (!player.isPlayerControlled || player.isPlayerDead)
                return;

            EnsureSoloPossibleHealth(player);
            TickShipRegen(player);
        }

        private static void EnsureSoloPossibleHealth(PlayerControllerB player)
        {
            if (InitializedPlayers.Contains(player.playerClientId))
                return;

            if (player.health <= 0)
                return;

            int maxHealth = Mathf.Max(1, SoloPossiblePlugin.MaxHealth.Value);

            if (player.health < maxHealth)
                player.health = maxHealth;

            InitializedPlayers.Add(player.playerClientId);
            UpdateHealthUi(player.health);
        }

        private static void TickShipRegen(PlayerControllerB player)
        {
            int maxHealth = Mathf.Max(1, SoloPossiblePlugin.MaxHealth.Value);

            if (player.health >= maxHealth)
            {
                regenTimer = 0f;
                return;
            }

            if (!player.isInHangarShipRoom)
            {
                regenTimer = 0f;
                return;
            }

            regenTimer += Time.deltaTime;

            if (regenTimer < Mathf.Max(0.1f, SoloPossiblePlugin.ShipRegenInterval.Value))
                return;

            regenTimer = 0f;

            int healAmount = Mathf.Max(0, SoloPossiblePlugin.ShipRegenAmount.Value);
            player.health = Mathf.Clamp(player.health + healAmount, 0, maxHealth);

            UpdateHealthUi(player.health);
        }

        private static void UpdateHealthUi(int health)
        {
            if (HUDManager.Instance == null)
                return;

            HUDManager.Instance.UpdateHealthUI(health, false);
        }
    }

    [HarmonyPatch(typeof(HUDManager), "Awake")]
    internal static class HudManagerAwakePatch
    {
        private static void Postfix(HUDManager __instance)
        {
            InventoryManager.ResizeHudInventory(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB), "Awake")]
    internal static class PlayerControllerAwakePatch
    {
        private static void Postfix(PlayerControllerB __instance)
        {
            InventoryManager.ResizePlayerInventory(__instance);
        }
    }

    [HarmonyPatch(typeof(GrabbableObject), "Start")]
    internal static class GrabbableObjectStartPatch
    {
        private static void Postfix(GrabbableObject __instance)
        {
            WeightManager.RegisterAndApply(__instance);
        }
    }

    [HarmonyPatch(typeof(StartOfRound), "ReviveDeadPlayers")]
    internal static class ReviveDeadPlayersPatch
    {
        private static void Postfix()
        {
            HealthManager.ResetLifeTracking();
            InventoryManager.ResizeAllPlayerInventories();

            if (HUDManager.Instance != null)
                InventoryManager.ResizeHudInventory(HUDManager.Instance);

            WeightManager.RefreshAll();
        }
    }

    [HarmonyPatch(typeof(StartOfRound), "Start")]
    internal static class StartOfRoundStartPatch
    {
        private static void Postfix()
        {
            HealthManager.ResetLifeTracking();
            InventoryManager.ResizeAllPlayerInventories();

            if (HUDManager.Instance != null)
                InventoryManager.ResizeHudInventory(HUDManager.Instance);

            WeightManager.RefreshAll();
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB), "ConnectClientToPlayerObject")]
    internal static class ConnectClientToPlayerObjectPatch
    {
        private static void Postfix(PlayerControllerB __instance)
        {
            InventoryManager.ResizeAllPlayerInventories();

            if (HUDManager.Instance != null)
                InventoryManager.ResizeHudInventory(HUDManager.Instance);

            if (SoloPossiblePlugin.IsLocalPlayerAllowed(__instance))
                HealthManager.Tick(__instance);

            WeightManager.RefreshAll();
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB), "Update")]
    internal static class PlayerUpdatePatch
    {
        private static readonly Dictionary<PlayerControllerB, float> PreviousMovementSpeeds = new Dictionary<PlayerControllerB, float>();

        private static void Prefix(
            PlayerControllerB __instance,
            ref bool ___isPlayerControlled,
            ref bool ___isSprinting,
            ref float ___movementSpeed)
        {
            PreviousMovementSpeeds[__instance] = ___movementSpeed;

            if (!___isPlayerControlled)
                return;

            if (!SoloPossiblePlugin.IsLocalPlayerAllowed(__instance))
                return;

            if (___isSprinting)
                ___movementSpeed *= SoloPossiblePlugin.SprintSpeedMultiplier.Value;
        }

        private static void Postfix(
            PlayerControllerB __instance,
            ref float ___movementSpeed)
        {
            if (PreviousMovementSpeeds.ContainsKey(__instance))
                ___movementSpeed = PreviousMovementSpeeds[__instance];

            if (SoloPossiblePlugin.IsLocalPlayer(__instance))
            {
                InventoryManager.TickHudMaintenance();
                InventoryManager.HandleFifthSlotHotkey(__instance);
                WeightManager.RefreshAllIfNeeded();
                HealthManager.Tick(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate")]
    internal static class PlayerStaminaPatch
    {
        private static float previousSprintMeter = -1f;

        private static void Prefix(
            PlayerControllerB __instance,
            ref bool ___isPlayerControlled,
            ref float ___sprintMeter)
        {
            if (!___isPlayerControlled || !SoloPossiblePlugin.IsLocalPlayerAllowed(__instance))
            {
                previousSprintMeter = -1f;
                return;
            }

            previousSprintMeter = ___sprintMeter;
        }

        private static void Postfix(
            PlayerControllerB __instance,
            ref bool ___isPlayerControlled,
            ref float ___sprintMeter)
        {
            if (previousSprintMeter < 0f)
                return;

            if (!___isPlayerControlled || !SoloPossiblePlugin.IsLocalPlayerAllowed(__instance))
                return;

            float staminaDelta = ___sprintMeter - previousSprintMeter;

            if (staminaDelta > 0f)
            {
                ___sprintMeter = Mathf.Clamp(
                    previousSprintMeter + staminaDelta * SoloPossiblePlugin.StaminaRegenMultiplier.Value,
                    0f,
                    1f
                );
            }
        }
    }
}