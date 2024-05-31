using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using KS.SceneFusion2.Client;
using KS.Unity.Editor;
using KS.SceneFusion;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * SF2 UI for the online menu. Shows notification and object counts.
     */
    public class sfOnlineMenuUI
    {
        private bool m_infoExpanded = true;
        private int m_notificationCount = 0;
        private uint m_gameObjectCount = 0;
        private uint m_gameObjectLimit = 0;

        /**
         * Draws notification and object counts.
         */
        public void Draw()
        {
            sfSession session = SceneFusion.Get().Service.Session;
            if (Event.current.type == EventType.Layout)
            {
                m_notificationCount = sfNotificationManager.Get().Count;
                m_gameObjectCount = session == null ? 0 : session.GetObjectCount(sfType.GameObject);
                m_gameObjectLimit = session == null ? uint.MaxValue : session.GetObjectLimit(sfType.GameObject);
            }

            if (m_notificationCount > 0)
            {
                string message = m_notificationCount == 1 ?
                    "1 notification" : (m_notificationCount + " notifications");
                if (ksStyle.HelpBox(MessageType.Warning, message, "") != -1)
                {
                    sfNotificationWindow.Open();
                }
            }

            if (m_gameObjectCount >= m_gameObjectLimit)
            {
                ksStyle.HelpBox(MessageType.Warning, "You cannot create more game objects because you reached the " +
                    m_gameObjectLimit + " game object limit. ", null, "Click here to upgrade.",
                    "https://www.kinematicsoup.com/scene-fusion/pricing");
            }

            m_infoExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(m_infoExpanded, "Info");
            if (m_infoExpanded)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Synced Game Objects", m_gameObjectCount + 
                    (m_gameObjectLimit != uint.MaxValue ? " / " + m_gameObjectLimit : ""));
                EditorGUILayout.LabelField("Synced Objects", session == null ? "0" : session.NumObjects.ToString());
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
    }
}
