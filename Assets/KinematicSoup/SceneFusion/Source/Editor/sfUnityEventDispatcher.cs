using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using KS.SceneFusion;
using KS.SceneFusion2.Client;
using UObject = UnityEngine.Object;

#if !UNITY_2021_3_OR_NEWER
using UnityEngine.Experimental.TerrainAPI;
#endif

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Listens for and dispatches Unity events, in some cases changing the parameters of the event. Allows all events
     * to be enabled or disabled. Register with this class instead of directly against the Unity events to ensure you
     * do not respond to events that were triggered by Scene Fusion.
     */
    public class sfUnityEventDispatcher
    {
        /**
         * @return  sfUnityEventDispatcher singleton instance
         */
        public static sfUnityEventDispatcher Get()
        {
            return m_instance;
        }
        private static sfUnityEventDispatcher m_instance = new sfUnityEventDispatcher();

        /**
         * Are events enabled?
         */
        public bool Enabled
        {
            get { return m_enabled; }
        }
        private bool m_enabled = false;

        /**
         * Invoked when a scene is opened or a new scene is created.
         */
        public event EditorSceneManager.SceneOpenedCallback OnOpenScene;

        /**
         * Invoked when a scene is closed.
         */
        public event EditorSceneManager.SceneClosedCallback OnCloseScene;

        /**
         * Create game object event callback.
         * 
         * @param   GameObject gameObject that was created.
         */
        public delegate void CreateCallback(GameObject gameObject);

        /**
         * Invoked when a game object is created. Only invoked if an undo operation is registered for the object
         * creation.
         */
        public event CreateCallback OnCreate;

        /**
         * Delete game object event callback.
         * 
         * @param   int instanceId of game object that was deleted.
         */
        public delegate void DeleteCallback(int instanceId);

        /**
         * Invoked when a game object is deleted.
         */
        public event DeleteCallback OnDelete;

        /**
         * Invoked when a terrain's heightmap is changed.
         */
        public event TerrainCallbacks.HeightmapChangedCallback OnTerrainHeightmapChange;

        /**
         * Invoked when a terrain's textures are changed
         */
        public event TerrainCallbacks.TextureChangedCallback OnTerrainTextureChange;

        public delegate void TerrainDetailChangedCallback(TerrainData terrainData, RectInt changeArea, int layer);
        public event TerrainDetailChangedCallback OnTerrainDetailChange;

        public delegate void TerrainTreeChangedCallback(TerrainData terrainData, bool hasRemovals);
        public event TerrainTreeChangedCallback OnTerrainTreeChange;

        public delegate void TerrainCheckCallback(TerrainData terrainData);
        public event TerrainCheckCallback OnTerrainCheck;

        /**
         * Invoked when properties are modified.
         */
        public event Undo.PostprocessModifications OnModifyProperties
        {
            add
            {
                if (value != null)
                {
                    m_propertyModificationHandlers.Add(value);
                }
            }
            remove
            {
                if (value != null)
                {
                    m_propertyModificationHandlers.Remove(value);
                }
            }
        }
        private List<Undo.PostprocessModifications> m_propertyModificationHandlers =
            new List<Undo.PostprocessModifications>();

        /**
         * Singleton constructor
         */
        private sfUnityEventDispatcher()
        {
            
        }

        /**
         * Enables events. Starts listening for Unity events.
         */
        public void Enable()
        {
            if (m_enabled)
            {
                return;
            }
            m_enabled = true;
            EditorSceneManager.newSceneCreated += InvokeOnOpenScene;
            EditorSceneManager.sceneOpened += InvokeOnOpenScene;
            EditorSceneManager.sceneClosing += InvokeOnCloseScene;
            Undo.postprocessModifications += InvokeOnModifyProperties;
            TerrainCallbacks.heightmapChanged += InvokeOnHeightmapChange;
            TerrainCallbacks.textureChanged += InvokeOnTextureChange;
#if UNITY_2022_2_OR_NEWER
            ObjectChangeEvents.changesPublished += OnChangesPublished;
#else
            EditorApplication.hierarchyChanged += OnHierarchyChange;
            sfSelectionWatcher.Get().OnDelete += OnDeleteSelection;
#endif
        }

        /**
         * Disables events. Stops listening for Unity events.
         */
        public void Disable()
        {
            if (!m_enabled)
            {
                return;
            }
            m_enabled = false;
            EditorSceneManager.newSceneCreated -= InvokeOnOpenScene;
            EditorSceneManager.sceneOpened -= InvokeOnOpenScene;
            EditorSceneManager.sceneClosing -= InvokeOnCloseScene;
            Undo.postprocessModifications -= InvokeOnModifyProperties;
            TerrainCallbacks.heightmapChanged -= InvokeOnHeightmapChange;
            TerrainCallbacks.textureChanged -= InvokeOnTextureChange;
#if UNITY_2022_2_OR_NEWER
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
#else
            EditorApplication.hierarchyChanged -= OnHierarchyChange;
            sfSelectionWatcher.Get().OnDelete -= OnDeleteSelection;
#endif
        }

        /**
         * Invokes the on open scene event.
         * 
         * @param   Scene scene that was created.
         * @param   NewSceneSetup setup
         * @param   NewSceneMode mode the scene was created with.
         */
        private void InvokeOnOpenScene(Scene scene, NewSceneSetup setup, NewSceneMode mode)
        {
            InvokeOnOpenScene(scene, mode == NewSceneMode.Additive ? OpenSceneMode.Additive : OpenSceneMode.Single);
        }

        /**
         * Invokes the on open scene event.
         * 
         * @param   Scene scene that was opened.
         * @param   OpenSceneMode mode the scene was opened with.
         */
        public void InvokeOnOpenScene(Scene scene, OpenSceneMode mode)
        {
            if (OnOpenScene != null)
            {
                OnOpenScene(scene, mode);
            }
        }

        /**
         * Invokes the on close scene event.
         * 
         * @param   Scene scene that was closed.
         * @param   bool removed - true if the scene was removed.
         */
        public void InvokeOnCloseScene(Scene scene, bool removed)
        {
            if (OnCloseScene != null)
            {
                OnCloseScene(scene);
            }
        }

        /**
         * Invokes the on modify properties event.
         * 
         * @param   UndoPropertyModification[] modifications. Remove modifications from the returned array to prevent
         *          them.
         * @return  UndoPropertyModification[] modifications that are allowed.
         */
        public UndoPropertyModification[] InvokeOnModifyProperties(UndoPropertyModification[] modifications)
        {
            foreach (Undo.PostprocessModifications handler in m_propertyModificationHandlers)
            {
                modifications = handler(modifications);
            }
            return modifications;
        }

#if UNITY_2022_2_OR_NEWER
        /**
         * Called at the end of the frame when operations were recorded on the undo stack. Checks for and invokes
         * events for game object creation events.
         * 
         * @param   ref ObjectChangeEventStream stream of undo operations
         */
        private void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                    {
                        if (OnCreate == null)
                        {
                            return;
                        }
                        CreateGameObjectHierarchyEventArgs data;
                        stream.GetCreateGameObjectHierarchyEvent(i, out data);
                        GameObject gameObj = EditorUtility.InstanceIDToObject(data.instanceId) as GameObject;
                        if (gameObj != null)
                        {
                            OnCreate(gameObj);
                        }
                        break;
                    }
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                    {
                        if (OnDelete == null)
                        {
                            return;
                        }
                        DestroyGameObjectHierarchyEventArgs data;
                        stream.GetDestroyGameObjectHierarchyEvent(i, out data);
                        OnDelete(data.instanceId);
                        break;
                    }
                }
            }
        }
#else
        /**
         * Called when a game object is created, deleted, reparented, renamed. Iterates all game objects looking for
         * new objects to invoke the OnCreate event with. Iterates all sfObjects for game objects looking for deleted
         * game objects to invoke the OnDelete event with.
         */
        private void OnHierarchyChange()
        {
            if (OnCreate == null)
            {
                return;
            }
            // Iterate all game objects looking for new objects that are syncable and are not synced. This is the only
            // way to detect new game objects prior to 2020.2.
            sfGameObjectTranslator translator = sfObjectEventDispatcher.Get().GetTranslator<sfGameObjectTranslator>(
                sfType.GameObject);
            sfUnityUtils.ForEachGameObject((GameObject gameObject) =>
            {
                sfObject obj = sfObjectMap.Get().GetSFObject(gameObject);
                if (obj == null || !obj.IsSyncing)
                {
                    if (translator.IsSyncable(gameObject))
                    {
                        OnCreate(gameObject);
                    }
                    return false;
                }
                return true;
            });
        }

        /**
         * Called when a selected object is destroyed. Invokes the OnDelete event if the object was a game object.
         * 
         * @param   UObject uobj that was destroyed.
         */
        private void OnDeleteSelection(UObject uobj)
        {
            if (OnDelete != null && uobj is GameObject)
            {
                OnDelete(uobj.GetInstanceID());
            }
        }
#endif

        /**
         * Invokes the on heightmap change event. This fires when the terrain heightmap changed.
         * 
         * @param   Terrain terrain - the Terrain object that references a changed TerrainData asset.
         * @param   RectInt changeArea - the area that the heightmap changed.
         * @param   bool synced - indicates whether the changes were fully synchronized back to CPU memory.
         */
        public void InvokeOnHeightmapChange(Terrain terrain, RectInt changeArea, bool synced)
        {
            if (OnTerrainHeightmapChange != null)
            {
                OnTerrainHeightmapChange(terrain, changeArea, synced);
            }
        }

        /**
         * Invokes the on texture change event. This fires when the terrain textures changed.
         * 
         * @param   Terrain terrain - the Terrain object that references a changed TerrainData asset.
         * @param   string textureName - the name of the texture that changed.
         * @param   RectInt changeArea - the region of the Terrain texture that changed, in texel coordinates.
         * @param   bool synced - indicates whether the changes were fully synchronized back to CPU memory.
         */
        public void InvokeOnTextureChange(Terrain terrain, string textureName, RectInt changeArea, bool synced)
        {
            if (OnTerrainTextureChange != null)
            {
                OnTerrainTextureChange(terrain, textureName, changeArea, synced);
            }
        }

        public void InvokeOnTerrainDetailChange(Terrain terrain, RectInt changeArea, int layer)
        {
            if (OnTerrainDetailChange != null && terrain != null)
            {
                OnTerrainDetailChange(terrain.terrainData, changeArea, layer);
            }
        }

        public void InvokeOnTerrainTreeChange(Terrain terrain, bool hasRemovals)
        {
            if (OnTerrainTreeChange != null && terrain != null)
            {
                OnTerrainTreeChange(terrain.terrainData, hasRemovals);
            }
        }

        internal void InvokeTerrainCheck(Terrain terrain)
        {
            if (OnTerrainCheck != null && terrain != null)
            {
                OnTerrainCheck(terrain.terrainData);
            }
        }
    }
}
