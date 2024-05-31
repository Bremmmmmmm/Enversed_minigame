using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using KS.SceneFusion2.Client;
using KS.Reactor;
using UObject = UnityEngine.Object;
using UnityEditor.UIElements;
using UnityEditor;
using KS.SceneFusion;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Manages scene syncing.
     */
    public class sfSceneTranslator : sfBaseTranslator
    {
        private sfObject m_lockObject;
        private sfSession m_session;
        private ksLinkedList<Scene> m_uploadList = new ksLinkedList<Scene>();
        private List<Scene> m_closedScenes = new List<Scene>();
        private Dictionary<Scene, sfObject> m_sceneToObjectMap = new Dictionary<Scene, sfObject>();
        private Dictionary<sfObject, Scene> m_objectToSceneMap = new Dictionary<sfObject, Scene>();
        private int m_missingObjectCount = 0;

        /**
         * Called after connecting to a session.
         */
        public override void OnSessionConnect()
        {
            SceneFusion.Get().OnUpdate += Update;
            sfUnityEventDispatcher.Get().OnOpenScene += OnOpenScene;
            sfUnityEventDispatcher.Get().OnCloseScene += OnCloseScene;
            m_session = SceneFusion.Get().Service.Session;
            if (SceneFusion.Get().Service.IsSessionCreator)
            {
                // Upload scenes
                RequestLock();
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (scene.isLoaded)
                    {
                        m_uploadList.Add(scene);
                    }
                }
            } 
        }

        /**
         * Called after disconnecting from a session.
         */
        public override void OnSessionDisconnect()
        {
            m_lockObject = null;
            SceneFusion.Get().OnUpdate -= Update;
            sfUnityEventDispatcher.Get().OnOpenScene -= OnOpenScene;
            sfUnityEventDispatcher.Get().OnCloseScene -= OnCloseScene;
            m_sceneToObjectMap.Clear();
            m_objectToSceneMap.Clear();
            m_closedScenes.Clear();
            m_uploadList.Clear();
        }

        /**
         * Gets the sfObject for a scene.
         * 
         * @param   Scene scene to get sfObject for.
         * @return  sfObject for the scene, or null if the scene has no sfObject.
         */
        public sfObject GetSceneObject(Scene scene)
        {
            sfObject obj;
            m_sceneToObjectMap.TryGetValue(scene, out obj);
            return obj;
        }

        /**
         * Gets the hierarchy sfObject for a scene.
         * 
         * @param   Scene scene to get hierarchy sfObject for.
         * @return  sfObject for the scene hierarchy, or null if the scene has no sfObject.
         */
        public sfObject GetHierarchyObject(Scene scene)
        {
            sfObject obj = GetSceneObject(scene);
            if (obj != null)
            {
                foreach (sfObject child in obj.Children)
                {
                    if (child.Type == sfType.Hierarchy)
                    {
                        return child;
                    }
                }
            }
            return null;
        }

        /**
         * Gets the scene for an sfObject.
         * 
         * @param   sfObject obj to get scene for. Can be a scene object or a hierarchy object.
         * @return  Scene scene for the sfObject. Invalid scene if the sfObject has no scene.
         */
        public Scene GetScene(sfObject obj)
        {
            if (obj == null)
            {
                return new Scene();
            }
            if (obj.Type == sfType.Hierarchy)
            {
                obj = obj.Parent;
            }
            Scene scene;
            m_objectToSceneMap.TryGetValue(obj, out scene);
            return scene;
        }

        /**
         * Called when another user creates a scene or scene lock sfObject.
         * 
         * @param   sfObject obj that was created.
         * @param   int childIndex of the object. -1 if the object is a root.
         */
        public override void OnCreate(sfObject obj, int childIndex)
        {
            switch (obj.Type)
            {
                case sfType.SceneLock:
                {
                    m_lockObject = obj;
                    // If we have levels to upload, request a lock and upload the levels once we have the lock.
                    if (m_uploadList.Count > 0)
                    {
                        m_lockObject.RequestLock();
                    }
                    break;
                }
                case sfType.Scene:
                {
                    EditorUtility.DisplayProgressBar("Scene Fusion", "Syncing scene data.", 0.0f);
                    try
                    {
                        OnCreateScene(obj);
                    }
                    finally
                    {
                        EditorUtility.ClearProgressBar();
                    }
                    break;
                }
            }
        }

        /**
         * Called when a scene is closed by another user. Closes the scene locally.
         * 
         * @param   sfObject obj that was deleted.
         */
        public override void OnDelete(sfObject obj)
        {
            Scene scene;
            if (!m_objectToSceneMap.TryGetValue(obj, out scene))
            {
                return;
            }
            m_objectToSceneMap.Remove(obj);
            m_sceneToObjectMap.Remove(scene);
            if (EditorSceneManager.SaveModifiedScenesIfUserWantsTo(new Scene[] { scene }))
            {
                EditorSceneManager.CloseScene(scene, true);
            }
            else
            {
                ksLog.Info(this, "User cancelled saving a scene closed by another user. Disconnecting.");
                SceneFusion.Get().Service.LeaveSession();
            }
        }

        /**
         * Called when a scene object is confirmed as deleted. Removes the scene and scene object from the scene to
         * object and object to scene maps.
         * 
         * @param   sfObject obj whose deletion was confirmed.
         */
        public override void OnConfirmDelete(sfObject obj)
        {
            Scene scene;
            if (m_objectToSceneMap.TryGetValue(obj, out scene))
            {
                m_sceneToObjectMap.Remove(scene);
                m_objectToSceneMap.Remove(obj);
            }
        }

        /**
         * Called every frame.
         * 
         * @param   float deltaTime in seconds since the last frame.
         */
        private void Update(float deltaTime)
        {
            UploadNewScenes();
            ProcessClosedScenes();
        }

        /**
         * Uploads scenes in the upload list once we acquire the scene lock.
         */
        private void UploadNewScenes()
        {
            // Wait until we acquire the lock on the lock object before uploading scenes to ensure two users don't try
            // to upload the same scene at the same time.
            if (m_lockObject != null && !m_lockObject.IsLockPending &&
                m_lockObject.LockOwner == m_session.LocalUser)
            {
                m_missingObjectCount = 0;
                foreach (Scene scene in m_uploadList)
                {
                    sfObject obj;
                    if (m_sceneToObjectMap.TryGetValue(scene, out obj))
                    {
                        if (obj.IsDeletePending)
                        {
                            // We have to wait until the delete is confirmed to reupload the scene.
                            continue;
                        }
                        if (obj.IsSyncing)
                        {
                            m_uploadList.RemoveCurrent();
                            continue;
                        }
                        m_sceneToObjectMap.Remove(scene);
                        m_objectToSceneMap.Remove(obj);
                    }
                    m_uploadList.RemoveCurrent();
                    UploadScene(scene);
                }
                if (m_uploadList.Count == 0)
                {
                    m_lockObject.ReleaseLock();
                }
                if (m_missingObjectCount > 0)
                {
                    if (!EditorUtility.DisplayDialog("Missing Object References",
                        "Found " + m_missingObjectCount + " missing object reference" +
                        (m_missingObjectCount == 1 ? "" : "s") + ". These references cannot sync properly and will " +
                        "be synced to other users as null if you continue. See logs for details. Do you want to " +
                        "continue anyway?", "Continue", "Cancel"))
                    {
                        SceneFusion.Get().Service.LeaveSession();
                    }
                    m_missingObjectCount = 0;
                }
            }
        }

        /**
         * Reopens closed scenes if they are locked, otherwise deletes them from the server.
         */
        private void ProcessClosedScenes()
        {
            if (m_closedScenes.Count > 0 && SceneFusion.Get().Service.Session != null)
            {
                foreach (Scene scene in m_closedScenes)
                {
                    sfObject obj = GetSceneObject(scene);
                    if (obj == null)
                    {
                        return;
                    }
                    // Reopen the scene if it is locked.
                    if (obj.IsLocked)
                    {
                        OnCreateScene(obj);
                    }
                    else
                    {
                        m_session.Delete(obj);
                    }
                }
                m_closedScenes.Clear();
            }
        }

        /**
         * Called when a scene is opened. If it is opened additively, queues the scene to be uploaded. If it is opened
         * as a single scene, disconnects from the session.
         * 
         * @param   Scene scene that was opened.
         * @param   OpenSceneMode mode the scene was opened with.
         */
        private void OnOpenScene(Scene scene, OpenSceneMode mode)
        {
            // Note: new scenes will have a null/empty name
            if (!scene.IsValid() || scene.isSubScene) {
                return;
            }
            switch (mode)
            {
                case OpenSceneMode.Additive:
                {
                    RequestLock();
                    m_uploadList.Add(scene);
                    break;
                }
                case OpenSceneMode.Single:
                {
                    ksLog.Info(this, "User opened a new scene. Disconnecting from server.");
                    SceneFusion.Get().Service.LeaveSession();
                    break;
                }
            }
        }

        /**
         * Called when a scene is closed. Adds the scene to the closed scenes list.
         * 
         * @param   Scene scene that was closed.
         */
        private void OnCloseScene(Scene scene)
        {
            m_closedScenes.Add(scene);
        }

        /**
         * Called when a scene is created by another user. Loads and syncs the scene, or creates it if does not exist.
         * 
         * @param   sfObject obj that was created.
         */
        private void OnCreateScene(sfObject obj)
        {
            sfDictionaryProperty properties = (sfDictionaryProperty)obj.Property;
            sfBaseProperty property;
            Scene scene;
            if (properties.TryGetField(sfProp.Path, out property))
            {
                string path = "Assets/" + sfPropertyUtils.ToString(property);
                bool loaded = true;
                scene = SceneManager.GetActiveScene();
                if (scene.path != path || SceneManager.sceneCount > 1)
                {
                    try
                    {
                        OpenSceneMode mode = OpenSceneMode.Additive;
                        if (m_objectToSceneMap.Count == 0)
                        {
                            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                            {
                                ksLog.Info(this, "User cancelled saving the current scene(s). Disconnecting.");
                                SceneFusion.Get().Service.LeaveSession();
                                return;
                            }
                            mode = OpenSceneMode.Single;
                        }
                        scene = EditorSceneManager.OpenScene(path, mode);
                    }
                    catch (Exception)
                    {
                        loaded = false;
                    }
                }
                if (!loaded || !scene.IsValid())
                {
                    ksLog.Info(this, "Could not find scene '" + path + "'. Creating new scene.");
                    scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                        m_objectToSceneMap.Count == 0 ? NewSceneMode.Single : NewSceneMode.Additive);
                    EditorSceneManager.SaveScene(scene, path);
                }
            }
            else
            {
                // Create a new untitled scene
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                        m_objectToSceneMap.Count == 0 ? NewSceneMode.Single : NewSceneMode.Additive);
            }
            m_sceneToObjectMap[scene] = obj;
            m_objectToSceneMap[obj] = scene;

            // Sync the child objects
            sfObject hierarchyObj = null;
            int index = 0;
            foreach (sfObject childObj in obj.Children)
            {
                if (childObj.Type == sfType.Hierarchy)
                {
                    hierarchyObj = childObj;
                }
                else
                {
                    sfObjectEventDispatcher.Get().OnCreate(childObj, index);
                }
                index++;
            }
            if (hierarchyObj == null)
            {
                ksLog.Warning(this, "Scene " + scene.name + " has no hierarchy object.");
                return;
            }

            // Load guids for game objects
            sfGuidManager.Get().LoadGuids(scene);
            sfGameObjectTranslator translator = sfObjectEventDispatcher.Get().GetTranslator<sfGameObjectTranslator>(
                sfType.GameObject);
            // Create deterministic guids for game objects that don't already have guids
            foreach (GameObject gameObject in scene.GetRootGameObjects())
            {
                translator.CreateGuids(gameObject);
            }
            // Sync the game objects
            foreach (sfObject childObj in hierarchyObj.Children)
            {
                translator.InitializeGameObject(childObj, scene);
            }
            // Destroy unsynced game objects
            foreach (GameObject gameObject in scene.GetRootGameObjects())
            {
                if (!sfObjectMap.Get().Contains(gameObject) && translator.IsSyncable(gameObject))
                {
                    UObject.DestroyImmediate(gameObject, true);
                }
            }
            // Sync game object order
            translator.ApplyHierarchyChanges(hierarchyObj);
        }

        /**
         * Uploads a scene to the server.
         * 
         * @param   Scene scene to upload.
         */
        private void UploadScene(Scene scene)
        {
            sfDictionaryProperty properties = new sfDictionaryProperty();
            sfObject obj = new sfObject(sfType.Scene, properties);
            m_sceneToObjectMap[scene] = obj;
            m_objectToSceneMap[obj] = scene;
            if (scene.path != "")
            {
                properties[sfProp.Path] = sfPropertyUtils.FromString(scene.path.Substring("Assets/".Length));
            }

            // Create a hierarchy child object. The game objects will be children of the hierarchy object. Other
            // scene-objects such as lighting will be children of the scene object.
            sfObject hierarchyObj = new sfObject(sfType.Hierarchy);
            obj.AddChild(hierarchyObj);

            sfGuidManager.Get().LoadGuids(scene);
            sfGameObjectTranslator translator = sfObjectEventDispatcher.Get().GetTranslator<sfGameObjectTranslator>(
                sfType.GameObject);

            EditorUtility.DisplayProgressBar("Scene Fusion", "Syncing scene data.", 0.0f);
            try
            {
                sfPropertyManager.Get().OnMissingObject += IncrementMissingObjectCount;

                sfLightingTranslator lightingTranslator = sfObjectEventDispatcher.Get()
                    .GetTranslator<sfLightingTranslator>(sfType.LightmapSettings);
                if (lightingTranslator != null)
                {
                    lightingTranslator.CreateLightingObjects(scene, obj);
                }

                GameObject[] gameObjects = scene.GetRootGameObjects();
                for (int i = 0; i < gameObjects.Length; ++i)
                {
                    float progress = (float)i / gameObjects.Length;
                    GameObject gameObject = gameObjects[i];
                    EditorUtility.DisplayProgressBar("Scene Fusion", "Syncing scene data.", progress);
                    if (translator.IsSyncable(gameObject))
                    {
                        translator.CreateGuids(gameObject);
                        sfObject childObj = translator.CreateObject(gameObject);
                        if (childObj != null)
                        {
                            hierarchyObj.AddChild(childObj);
                        }
                    }
                }

                m_session.Create(obj);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                sfPropertyManager.Get().OnMissingObject -= IncrementMissingObjectCount;
            }
        }

        /**
         * Requests the lock for uploading levels.
         */
        private void RequestLock()
        {
            // If the lock object does not exist, create and lock it.
            if (m_lockObject == null && SceneFusion.Get().Service.IsSessionCreator)
            {
                m_lockObject = new sfObject(sfType.SceneLock);
                m_lockObject.RequestLock();
                m_session.Create(m_lockObject);
            }
            else if (m_lockObject != null && m_lockObject.LockOwner != m_session.LocalUser)
            {
                // This will send a lock request as soon as the lock object becomes unlocked
                m_lockObject.RequestLock();
            }
        }

        /**
         * Increments the missing object count.
         * 
         * @param   UObject uobj that references a missing object. Unused.
         */
        private void IncrementMissingObjectCount(UObject uobj)
        {
            m_missingObjectCount++;
        }
    }
}
