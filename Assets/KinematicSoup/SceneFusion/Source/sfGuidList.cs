using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using KS.Reactor;

namespace KS.SceneFusion2.Unity
{
    /**
     * Stores a list of game object guid pairs. One sfGuidList is saved in each scene with the guids for the game
     * objects in that scene.
     */
    [AddComponentMenu("")]
    [ExecuteInEditMode]
    public class sfGuidList : MonoBehaviour
    {
        /**
         * Game object + guid pair.
         */
        [Serializable]
        public struct ObjectGuid
        {
            /**
             * Game object
             */
            public GameObject GameObject;

            /**
             * Guid
             */
            public string Guid;

            /**
             * Constructor
             * 
             * @param   GameObject gameObject
             * @param   Guid guid
             */
            public ObjectGuid(GameObject gameObject, Guid guid)
            {
                GameObject = gameObject;
                Guid = guid.ToString();
            }
        }

        /**
         * List of game object + guid pairs.
         */
        public List<ObjectGuid> ObjectGuids = new List<ObjectGuid>();

        // Maps scenes to sfGuidLists
        private static Dictionary<Scene, sfGuidList> m_sceneToList = new Dictionary<Scene, sfGuidList>();

        /**
         * Gets the sfGuidList for a scene. Creates one if there isn't already one in the scene.
         * 
         * @param   Scene scene to get guid list for.
         * @return  sfGuidList for the scene.
         */
        public static sfGuidList Get(Scene scene)
        {
            sfGuidList map;
            if (!m_sceneToList.TryGetValue(scene, out map))
            {
                GameObject gameObject = new GameObject("Scene Fusion Guids");
                SceneManager.MoveGameObjectToScene(gameObject, scene);
                map = gameObject.AddComponent<sfGuidList>();
            }
            return map;
        }

        /**
         * Initialization. Puts this list in the scene to list map.
         */
        private void OnEnable()
        {
            sfGuidList map;
            if (!m_sceneToList.TryGetValue(gameObject.scene, out map) || map == null)
            {
                m_sceneToList[gameObject.scene] = this;
            }
            else
            {
                ksLog.Warning(this, "Destroying duplicate sfGuidList in scene " + gameObject.scene.name);
                DestroyImmediate(this);
            }
        }

        /**
         * Deinitialization. Removes this list from the scene to list map.
         */
        private void OnDisable()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isUpdating ||
               (!Application.isPlaying && UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode))
            {
                return;
            }
#endif
            sfGuidList map;
            if (m_sceneToList.TryGetValue(gameObject.scene, out map) && map == this)
            {
                m_sceneToList.Remove(gameObject.scene);
            }
        }
    }
}
