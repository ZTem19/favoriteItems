using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using BepInEx.Logging;
using System.Reflection;
using System.Collections;
using TMPro;
using BepInEx.Configuration;
using System.Diagnostics;


namespace favoriteItems
{
    [BepInPlugin(pluginGUID, pluginName, pluginVersion)]
    public class Main : BaseUnityPlugin
    {
        // BepIn Setup
        const string pluginGUID = "com.grizzyggs.FavoriteItems";
        const string pluginName = "Favorite Items";
        const string pluginVersion = "0.1.1";

        private readonly Harmony HarmonyInstance = new Harmony(pluginGUID);

        public static ManualLogSource logger = BepInEx.Logging.Logger.CreateLogSource(pluginName);
        // End of BepIn Setup

        // Custom Variables
        public static bool IsQuickStacking = false;
        public static string FavoriteKey = "favoriteItems_favorite";
        public static ItemDrop.ItemData CurrentHoveredItem = null;
        // End Custom Variables

        // Config File 
        private ConfigEntry<bool> modEnabled;
        private static ConfigEntry<KeyCode> favoriteKey;



        public void Awake()
        { 
            modEnabled = Config.Bind("General", "Enabled", true, "Enable or disable the Favorite Items mod.");
            favoriteKey = Config.Bind("Settings", "FavoriteHotkey", KeyCode.LeftAlt, "Hotkey to toggle favorite status on hovered item.");

            if (!modEnabled.Value) {
                Main.logger.LogInfo("Favorite Items mod is disabled in config. Exiting.");
                return;
            }
            else {
                Main.logger.LogInfo("Loaded Favorite Items with config file");
                Assembly assembly = Assembly.GetExecutingAssembly();
                HarmonyInstance.PatchAll(assembly);
            }
        }

        void FixedUpdate()
        {
            getHoveredItem();
        }

        void Update()
        {
            // Simple Hotkey to toggle "Favorite" status on hovered item
            if (UnityEngine.Input.GetKeyDown(Main.favoriteKey.Value) && Main.CurrentHoveredItem != null)
            {
                Main.logger.LogDebug($"Attempting to toggle favorite status on item: {Main.CurrentHoveredItem}");
                if (Main.CurrentHoveredItem.m_customData.ContainsKey(FavoriteKey))
                {
                    Main.CurrentHoveredItem.m_customData.Remove(FavoriteKey);


                    Logger.LogInfo($"Unfavorited: {Main.CurrentHoveredItem.m_shared.m_name}");
                }
                else
                {
                    Main.CurrentHoveredItem.m_customData[FavoriteKey] = "true";

                    Logger.LogInfo($"Favorited: {Main.CurrentHoveredItem.m_shared.m_name}");
                }
            }
        }

        private void getHoveredItem()
        {
            if (InventoryGui.instance == null)
            {
                Main.logger.LogDebug("inventory is null");
                return; 
            }
            
            InventoryGrid playerGrid = InventoryGui.instance.m_playerGrid;
            if (playerGrid == null)
            {
                Main.logger.LogDebug("playergrid is null");
                return;
            }

            InventoryGrid.Element hoveredElement = playerGrid.GetHoveredElement();
            if (hoveredElement != null && playerGrid.m_inventory != null)
            {
                ItemDrop.ItemData hoveredItem = playerGrid.m_inventory.GetItemAt(hoveredElement.m_pos.x, hoveredElement.m_pos.y);
                Main.CurrentHoveredItem = hoveredItem;
                if(hoveredItem != null)
                {
                    Main.logger.LogDebug($"hoveredelement item: {hoveredItem.m_shared.m_name}");
                }
                else
                {
                    Main.logger.LogDebug("hoveredelement has no item");
                }

            }
            else
            {
                Main.logger.LogDebug("hoveredelement is null");
            }

        }

        [HarmonyPatch(typeof(Humanoid), nameof(Player.IsItemEquiped))]
        public static class Inventory_MoveItem_Patch
        {
            // __instance: The inventory trying to move the item (The Player's inventory)
            // item: The specific item being moved
            [HarmonyPrefix]
            public static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
            {
                // 1. Are we currently running a Quick Stack operation?
                // If NO, return true (allow normal manual dragging/dropping)
                if (!Main.IsQuickStacking) return true;

                // 2. Is this item Favorited?
                if (item.m_customData.ContainsKey(Main.FavoriteKey))
                {
                    // BLOCK THE EXECUTION
                    // Returning 'false' in a Prefix prevents the original method from running.
                    Main.logger.LogDebug($"Item blocked from quick stacking: {item.m_shared.m_name}");
                    __result = true;
                    return false;
                }

                // Otherwise, allow the game to proceed with its logic
                Main.logger.LogDebug($"Item allowed to quick stack: {item.m_shared.m_name}");
                return true;
            }


            // This hooks into the Native "Quick Stack" button on the Chest UI
            [HarmonyPatch(typeof(Container), nameof(Container.StackAll))]
            public static class Container_StackAll_Patch
            {
                // Run BEFORE
                [HarmonyPrefix]
                public static void Prefix()
                {
                    Main.logger.LogDebug("Quick stacking now");
                    Main.IsQuickStacking = true;
                }

                // Run AFTER
                [HarmonyPostfix]
                public static void Postfix()
                {
                    Main.logger.LogDebug("No longer quick stacking");
                    Main.IsQuickStacking = false;
                }
            }
        }
        
        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui))]
        public static class InventoryGrid_Patch
        {
            public static void Postfix(InventoryGrid __instance, Player player, ItemDrop.ItemData dragItem)
            {
                if (__instance == null || __instance.m_inventory == null) return;

                foreach (var element in __instance.m_elements)
                {
                    if (element?.m_go == null) continue;

                    Transform textTransform = element.m_go.transform.Find("FavoriteText");

                    // If the slot is inactive in the pool, force hide our custom UI and skip
                    if (!element.m_used)
                    {
                        textTransform?.gameObject.SetActive(false);
                        continue;
                    }


                    ItemDrop.ItemData item = __instance.m_inventory.GetItemAt(element.m_pos.x, element.m_pos.y);

                    bool isFavorite = item?.m_customData?.ContainsKey(Main.FavoriteKey) == true;

                    if (isFavorite)
                    {
                        TextMeshProUGUI favText;
                        if (textTransform == null)
                        {
                            GameObject go = new GameObject("FavoriteText", typeof(RectTransform));
                            go.transform.SetParent(element.m_go.transform, false);

                            favText = go.AddComponent<TextMeshProUGUI>();
                            favText.raycastTarget = false; // So it doesn't block mouse interactions

                            // FIXED: Direct assignment works if favText is TextMeshProUGUI
                            if (element.m_amount != null)
                            {
                                favText.font = element.m_amount.font;
                                favText.fontSize = element.m_amount.fontSize;
                            }

                            favText.color = UnityEngine.Color.yellow;
                            favText.alignment = TextAlignmentOptions.BottomRight;

                            
                            RectTransform rt = go.GetComponent<RectTransform>();
                            // Top-Left Corner setup
                            rt.anchorMin = new Vector2(1, 0); 
                            rt.anchorMax = new Vector2(1, 0);
                            rt.pivot = new Vector2(1, 0);     
                            rt.anchoredPosition = new Vector2(-2, 2); 
                        }
                        else
                        {
                            favText = textTransform.GetComponent<TextMeshProUGUI>();
                            textTransform.gameObject.SetActive(true);
                            favText.raycastTarget = false;
                        }

                        favText.text = "★";

                        // Debug border for your injected text element (Red)

                        AddDebugOverlay(textTransform, new Color(1, 0, 0, 0.4f));
                    }
                    else if (textTransform != null)
                    {
                        // Hide if the item is no longer favorited
                        textTransform.gameObject.SetActive(false);
                    }
                }
            }
        }

        [Conditional("DEBUG")]
        private static void AddDebugOverlay(Transform parent, Color color)
        {
            if (parent.Find("DebugBorder") != null) return;

            GameObject borderGo = new GameObject("DebugBorder");
            borderGo.transform.SetParent(parent, false);

            // Stretch to fill parent RectTransform
            RectTransform rt = borderGo.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            // Add semi-transparent color block
            Image img = borderGo.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false; // Prevents interference with clicking the item
        }
    }
}