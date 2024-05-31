using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using KS.SceneFusion;
using KS.SceneFusion2.Client;
using KS.Reactor;
using KS.Unity.Editor;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Draws icons in the hierarchy indicating the sync/lock status of game objects.
     */
    public class sfHierarchyIconDrawer
    {
        /**
         * @return  sfHierarchyIconDrawer singleton instance
         */
        public static sfHierarchyIconDrawer Get()
        {
            return m_instance;
        }
        private static sfHierarchyIconDrawer m_instance = new sfHierarchyIconDrawer();

        /**
         * Singleton constructor
         */
        private sfHierarchyIconDrawer()
        {
        }

        /**
         * Starts drawing icons.
         */
        public void Start()
        {
            EditorApplication.hierarchyWindowItemOnGUI += DrawHierarchyItem;
        }

        /**
         * Stops drawing icons.
         */
        public void Stop()
        {
            EditorApplication.hierarchyWindowItemOnGUI -= DrawHierarchyItem;
        }

        /**
         * Draws an icon for a game object in the hierarchy view.
         * 
         * @param   int instanceId of game object to draw icon for.
         * @param   Rect area the game object label was drawn in.
         */
        private void DrawHierarchyItem(int instanceId, Rect area)
        {
            GameObject gameObject = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            sfObject obj = sfObjectMap.Get().GetSFObject(gameObject);
            if (obj == null || !obj.IsCreated)
            {
                return;
            }

            area.x += area.width - 17 - sfConfig.Get().UI.HierarchyIconOffset;
            area.width = area.height + 2;// the icons shrink for some reason unless we add + 2

            Texture2D icon;
            string tooltip;
            if (obj.CanEdit)
            {
                ksLinkedList<sfNotification> notifications =
                    sfNotificationManager.Get().GetNotifications(gameObject, true);
                if (notifications != null && notifications.Count > 0)
                {
                    icon = ksStyle.GetHelpBoxIcon(MessageType.Warning);
                    tooltip = notifications.Count == 1 ? notifications.First.Category.Name : notifications.Count + 
                        " notifications";
                }
                else
                {
                    icon = sfTextures.Check;
                    tooltip = "Synced and unlocked";
                }
            }
            else if (obj.IsFullyLocked)
            {
                icon = sfTextures.Lock;
                tooltip = "Fully locked by " + obj.LockOwner.Name + ". Property and child editing disabled.";
                GUI.color = obj.LockOwner.Color;
            }
            else
            {
                icon = sfTextures.Lock;
                tooltip = "Partially Locked. Property editing disabled.";
                if (Event.current.type == EventType.ContextClick)
                {
                    // Temporarly make the object editable so we can add children to it via the context menu.
                    sfGameObjectTranslator translator = sfObjectEventDispatcher.Get()
                        .GetTranslator<sfGameObjectTranslator>(sfType.GameObject);
                    translator.TempUnlock(gameObject);
                }
            }
            if (icon != null)
            {
                GUI.Label(area, new GUIContent(icon, tooltip));
            }
            if (obj.IsFullyLocked)
            {
                GUI.color = Color.white;
            }
        }
    }
}
