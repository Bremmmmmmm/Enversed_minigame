using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using KS.SceneFusion2.Client;
using KS.SceneFusion;
using KS.Reactor;
using KS.Unity.Editor;
using UObject = UnityEngine.Object;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Manages syncing of game objects.
     */
    public class sfGameObjectTranslator : sfBaseUObjectTranslator
    {
        /**
         * Lock type
         */
        public enum LockType
        {
            NOT_SYNCED,
            UNLOCKED,
            PARTIALLY_LOCKED,
            FULLY_LOCKED
        }

        /**
         * Lock state change event handler.
         * 
         * @param   GameObject gameObject whose lock state changed.
         * @param   LockType lockType
         * @param   sfUser user who owns the lock, or null if the object is not fully locked.
         */
        public delegate void OnLockStateChangeHandler(GameObject gameObject, LockType lockType, sfUser user);

        /**
         * Invoked when a game object's lock state changes.
         */
        public event OnLockStateChangeHandler OnLockStateChange;

        // Don't sync gameobjects with these types of components
        private HashSet<Type> m_blacklist = new HashSet<Type>();

        private bool m_reachedObjectLimit = false;
        private bool m_relockObjects = false;
        private List<GameObject> m_tempUnlockedObjects = new List<GameObject>();
        private List<sfObject> m_recreateList = new List<sfObject>();
        private HashSet<sfObject> m_parentsWithNewChildren = new HashSet<sfObject>();
        private HashSet<sfObject> m_serverHierarchyChangedSet = new HashSet<sfObject>();
        private HashSet<sfObject> m_localHierarchyChangedSet = new HashSet<sfObject>();
        private HashSet<GameObject> m_hierarchyDraggedObjects = new HashSet<GameObject>();
        private HashSet<GameObject> m_applyPropertiesSet = new HashSet<GameObject>();
        private Dictionary<int, sfObject> m_instanceIdToSFObjectMap = new Dictionary<int, sfObject>();

        /**
         * Initialization
         */
        public override void Initialize()
        {
            sfSessionsMenu.CanSync = IsSyncable;

            ksEditorEvents.OnNewAssets += OnNewAssets;

            sfPropertyManager.Get().SyncedHiddenProperties.Add<GameObject>("m_IsActive");

            DontSyncObjectsWith<sfGuidList>();
            PostPropertyChange.Add<GameObject>("m_Name",
                (UObject uobj, sfBaseProperty prop) => sfHierarchyWatcher.Get().MarkHierarchyStale());
            PostPropertyChange.Add<GameObject>("m_Icon",
                (UObject uobj, sfBaseProperty prop) => sfLockManager.Get().RefreshLock((GameObject)uobj));
            PostPropertyChange.Add<GameObject>("m_IsActive",
                 (UObject uobj, sfBaseProperty prop) => sfUI.Get().MarkSceneViewStale());

            m_propertyChangeHandlers.Add<GameObject>(sfProp.Path, (UObject uobj, sfBaseProperty prop) =>
            {
                OnPrefabPathChange((GameObject)uobj, prop);
                return true;
            });
            m_propertyChangeHandlers.Add<GameObject>(sfProp.ChildIndexes, (UObject uobj, sfBaseProperty prop) =>
            {
                return true;
            });
        }

        /**
         * Called after connecting to a session.
         */
        public override void OnSessionConnect()
        {
            SceneFusion.Get().PreUpdate += PreUpdate;
            SceneFusion.Get().OnUpdate += Update;
            sfSelectionWatcher.Get().OnSelect += OnSelect;
            sfSceneSaveWatcher.Get().PreSave += PreSave;
            sfSceneSaveWatcher.Get().PostSave += RelockObjects;
            sfUnityEventDispatcher.Get().OnCreate += OnCreateGameObject;
            sfUnityEventDispatcher.Get().OnDelete += OnDeleteGameObject;
            sfHierarchyWatcher.Get().OnDragCancel += RelockObjects;
            sfHierarchyWatcher.Get().OnDragComplete += OnHierarchyDragComplete;
            sfHierarchyWatcher.Get().OnValidateDrag += ValidateHierarchyDrag;
            sfUndoManager.Get().OnRegisterUndo += OnRegisterUndo;
        }

        /**
         * Called after disconnecting from a session.
         */
        public override void OnSessionDisconnect()
        {
            m_reachedObjectLimit = false;

            SceneFusion.Get().PreUpdate -= PreUpdate;
            SceneFusion.Get().OnUpdate -= Update;
            sfSelectionWatcher.Get().OnSelect -= OnSelect;
            sfSceneSaveWatcher.Get().PreSave -= PreSave;
            sfSceneSaveWatcher.Get().PostSave -= RelockObjects;
            sfUnityEventDispatcher.Get().OnCreate -= OnCreateGameObject;
            sfUnityEventDispatcher.Get().OnDelete -= OnDeleteGameObject;
            sfHierarchyWatcher.Get().OnDragCancel -= RelockObjects;
            sfHierarchyWatcher.Get().OnDragComplete -= OnHierarchyDragComplete;
            sfHierarchyWatcher.Get().OnValidateDrag -= ValidateHierarchyDrag;
            sfUndoManager.Get().OnRegisterUndo -= OnRegisterUndo;

            // Unlock all game objects
            foreach (GameObject gameObject in sfUnityUtils.IterateGameObjects())
            {
                sfObject obj = sfObjectMap.Get().GetSFObject(gameObject);
                if (obj != null && obj.IsLocked)
                {
                    Unlock(gameObject);
                }

                sfMissingPrefab missingPrefab = gameObject.GetComponent<sfMissingPrefab>();
                if (missingPrefab != null)
                {
                    // Allow the missing prefab component to be removed.
                    missingPrefab.hideFlags = HideFlags.None;
                }
            }
        }

        /**
         * Called every pre-update.
         * 
         * @param   float deltaTime in seconds since the last update.
         */
        private void PreUpdate(float deltaTime)
        {
            // Relock objects that were temporarily unlocked to make dragging in the hieararchy window work
            if (m_relockObjects)
            {
                RelockObjects();
                m_relockObjects = false;
            }

            // Recreate objects in the recreate list
            foreach (sfObject obj in m_recreateList)
            {
                if (!sfObjectMap.Get().Contains(obj))
                {
                    OnCreate(obj, obj.Parent == null ? -1 : obj.Parent.Children.IndexOf(obj));
                    if (obj.IsLockPending)
                    {
                        // If we are trying to get the lock, the old game object was selected before it was destroyed.
                        // Select the new game object.
                        GameObject gameObject = sfObjectMap.Get().Get<GameObject>(obj);
                        if (gameObject == null)
                        {
                            obj.ReleaseLock();
                        }
                        else
                        {
                            List<UObject> selection = new List<UObject>(Selection.objects);
                            selection.Add(gameObject);
                            Selection.objects = selection.ToArray();
                        }
                    }
                }
            }
            m_recreateList.Clear();

            // Add the parent of each dragged object to the set with local hierarchy changes
            foreach (GameObject gameObject in m_hierarchyDraggedObjects)
            {
                sfObject parent;
                if (gameObject.transform.parent == null)
                {
                    sfSceneTranslator translator = sfObjectEventDispatcher.Get().GetTranslator<sfSceneTranslator>(
                        sfType.Hierarchy);
                    parent = translator.GetHierarchyObject(gameObject.scene);
                }
                else
                {
                    parent = sfObjectMap.Get().GetSFObject(gameObject.transform.parent);
                }
                if (parent != null)
                {
                    m_localHierarchyChangedSet.Add(parent);
                }
            }
            m_hierarchyDraggedObjects.Clear();
            // Sync the hierarchy for objects with local hierarchy changes
            foreach (sfObject parent in m_localHierarchyChangedSet)
            {
                SyncHierarchy(parent);
            }
            m_localHierarchyChangedSet.Clear();

            // Sync when prefab instances are unpacked
            foreach (GameObject gameObject in Selection.gameObjects)
            {
                SyncPrefabUnpacking(gameObject);
            }

            // Reapply properties to game objects in the apply properties set and their components
            foreach (GameObject gameObject in m_applyPropertiesSet)
            {
                sfObject obj = sfObjectMap.Get().GetSFObject(gameObject);
                if (obj == null || gameObject == null)
                {
                    continue;
                }
                sfPropertyManager.Get().ApplyProperties(gameObject, (sfDictionaryProperty)obj.Property);
                foreach (Component component in gameObject.GetComponents<Component>())
                {
                    obj = sfObjectMap.Get().GetSFObject(component);
                    if (obj != null)
                    {
                        sfPropertyManager.Get().ApplyProperties(component, (sfDictionaryProperty)obj.Property);
                    }
                }
            }
            m_applyPropertiesSet.Clear();

            // Upload new game objects
            UploadGameObjects();
        }

        /**
         * Called every update.
         * 
         * @param   float deltaTime in seconds since the last update.
         */
        private void Update(float deltaTime)
        {
            // Apply hierarchy changes from the server
            foreach (sfObject parent in m_serverHierarchyChangedSet)
            {
                ApplyHierarchyChanges(parent);
            }
            m_serverHierarchyChangedSet.Clear();

            if (!m_reachedObjectLimit)
            {
                sfSession session = SceneFusion.Get().Service.Session;
                if (session != null)
                {
                    uint limit = session.GetObjectLimit(sfType.GameObject);
                    if (limit != uint.MaxValue && session.GetObjectCount(sfType.GameObject) >= limit)
                    {
                        m_reachedObjectLimit = true;
                        EditorUtility.DisplayDialog("Game Object Limit Reached",
                            "You cannot create more game objects because you reached the " + limit +
                            " game object limit.", "OK");
                    }
                }
            }
        }

        /**
         * Prevents game objects with components of type T from syncing.
         */
        public void DontSyncObjectsWith<T>() where T : Component
        {
            m_blacklist.Add(typeof(T));
        }

        /**
         * Prevents game objects with components of the given type from syncing.
         * 
         * @param   Type type of component whose game objects should not sync.
         */
        public void DontSyncObjectsWith(Type type)
        {
            m_blacklist.Add(type);
        }

        /**
         * Checks if a game object can be synced. Objects can be synced if the following conditions are met:
         *  - They are not hidden
         *  - They have no blacklisted components
         * 
         * @param   GameObject gameObject
         * @return  bool true if the game object can be synced.
         */
        public bool IsSyncable(GameObject gameObject)
        {
            return (gameObject.hideFlags & (HideFlags.HideInHierarchy | HideFlags.DontSave)) == HideFlags.None &&
                !HasBlacklistedComponent(gameObject);
        }

        /**
         * Adds a mapping between an sfObject and a game object to the sfObjectMap and the instance id map.
         * 
         * @param   sfObject obj
         * @param   GameObject gameObject
         */
        private void AddMapping(sfObject obj, GameObject gameObject)
        {
            sfObjectMap.Get().Add(obj, gameObject);
            m_instanceIdToSFObjectMap[gameObject.GetInstanceID()] = obj;
        }

        /**
         * Removes a mapping between an sfObject and a game object from the sfObjectMap and the instance id map.
         * 
         * @param   GameObject gameObject to remove the mapping for.
         */
        private sfObject RemoveMapping(GameObject gameObject)
        {
            if ((object)gameObject == null)
            {
                return null;
            }
            sfObject obj = sfObjectMap.Get().Remove(gameObject);
            m_instanceIdToSFObjectMap.Remove(gameObject.GetInstanceID());
            return obj;
        }

        /**
         * Removes a mapping between an sfObject and a game object from the sfObjectMap and the instance id map.
         * 
         * @param   sfObject obj to remove the mapping for.
         */
        private GameObject RemoveMapping(sfObject obj)
        {
            GameObject gameObject = sfObjectMap.Get().Remove(obj) as GameObject;
            if ((object)gameObject != null)
            {
                m_instanceIdToSFObjectMap.Remove(gameObject.GetInstanceID());
            }
            return gameObject;
        }

        /**
         * Iterates a game object and its descendants and creates deterministic guids for any game objects that do not
         * have a guid.
         * 
         * @param   GameObject gameObject
         */
        public void CreateGuids(GameObject gameObject)
        {
            if (!IsSyncable(gameObject))
            {
                return;
            }
            sfGuidManager.Get().GetGuid(gameObject, true);
            foreach (Transform child in gameObject.transform)
            {
                CreateGuids(child.gameObject);
            }
        }

        /**
         * Applies server hierarchy changes to the local hierarchy (new children and child order) of an object.
         * 
         * @param   sfObject parent to apply hierarchy changes to. This should be a scene or transform object.
         */
        public void ApplyHierarchyChanges(sfObject parent)
        {
            // 1: server order, 2: client order
            int index1 = -1;
            int index2 = 0;
            int childIndex = 0;
            List<GameObject> children2 = GetChildGameObjects(parent);
            if (children2 == null)
            {
                return;
            }
            Transform parentTransform = sfObjectMap.Get().Get<Transform>(parent);// null if the parent is a scene
            Dictionary<sfObject, int> childIndexes = null;
            HashSet<GameObject> skipped = null;
            // Unity only allows you to set the child index of one child at a time. Each time you set the child index
            // is O(n). A naive algorithm could easily become O(n^2) if it sets the child index on every child. This
            // algorithm minimizes the amount of child indexes changes for better performance in most cases, though the
            // worst case is still O(n^2).
            foreach (sfObject obj1 in parent.Children)
            {
                if (obj1.Type != sfType.GameObject)
                {
                    continue;
                }
                index1++;
                GameObject gameObject1 = sfObjectMap.Get().Get<GameObject>(obj1);
                if (gameObject1 == null)
                {
                    continue;
                }
                if (gameObject1.transform.parent != parentTransform)
                {
                    // The game object has a different parent. Set the parent and child index.
                    gameObject1.transform.SetParent(parentTransform, false);
                    gameObject1.transform.SetSiblingIndex(childIndex);
                    sfHierarchyWatcher.Get().MarkHierarchyStale();
                    childIndex++;
                    continue;
                }

                if (skipped != null && skipped.Remove(gameObject1))
                {
                    // We encountered this game object in the client list already and determined it should be moved
                    // when we found it in the server list. Set to chiledIndex -1 because its current index is lower
                    // and when we remove it, the destination index is decremented.
                    gameObject1.transform.SetSiblingIndex(childIndex - 1);
                    sfHierarchyWatcher.Get().MarkHierarchyStale();
                    continue;
                }

                // Delta1 is how far to the left gameObject1 needs to move to get to the correct index. -1 means it
                // needs to be calculated. Delta2 is the difference between index1 and the server child index of
                // gameObject2. We move the object with the greater delta as this gets us closer to the server state
                // and minimizes moves.
                int delta1 = -1;
                while (index2 < children2.Count)
                {
                    GameObject gameObject2 = children2[index2];
                    if (gameObject2 == gameObject1)
                    {
                        // The game object does not need to be moved.
                        index2++;
                        childIndex++;
                        break;
                    }

                    sfObject obj2 = sfObjectMap.Get().GetSFObject(gameObject2);
                    if (obj2 == null || obj2.Parent != parent)
                    {
                        // The game object is not synced or has a different parent on the server. Its parent will
                        // change when we apply hierarchy changes to its new parent.
                        if (obj2 != null && !m_serverHierarchyChangedSet.Contains(obj2.Parent))
                        {
                            ApplyHierarchyChanges(obj2.Parent);
                        }
                        index2++;
                        childIndex++;
                        continue;
                    }

                    if (childIndexes == null)
                    {
                        // Create map of sfObjects to child indexes for fast index lookups.
                        childIndexes = new Dictionary<sfObject, int>();
                        foreach (sfObject child in parent.Children)
                        {
                            childIndexes.Add(child, childIndexes.Count);
                        }
                    }

                    int delta2 = childIndexes[obj2] - index1;
                    if (delta1 < 0)
                    {
                        // Calculate delta1
                        for (int i = index2 + 1; i < children2.Count; i++)
                        {
                            if (children2[i] == gameObject1)
                            {
                                delta1 = i - index2;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // We moved childIndex one to the right, so delta1 decreases by 1.
                        delta1--;
                    }
                    if (delta1 > delta2)
                    {
                        // Moving gameObject1 gets us closer to the server state than moving gameObject2. Move
                        // gameObject1.
                        gameObject1.transform.SetSiblingIndex(childIndex);
                        childIndex++;
                        // Since gameObject1 was moved we need to remove it from the client child list so we don't
                        // encounter it where it no longer is.
                        children2.RemoveAt(index2 + delta1);
                        sfHierarchyWatcher.Get().MarkHierarchyStale();
                        break;
                    }
                    else
                    {
                        // Moving gameObject2 gets us closer to the server state than moving gameObject1. Add
                        // gameObject2 to the skipped set and move it once we encounter it in the server list.
                        if (skipped == null)
                        {
                            skipped = new HashSet<GameObject>();
                        }
                        skipped.Add(gameObject2);
                        index2++;
                        childIndex++;
                    }
                }
            }
            while (index2 < children2.Count)
            {
                GameObject gameObject2 = children2[index2];
                sfObject obj2 = sfObjectMap.Get().GetSFObject(gameObject2);
                if (obj2 != null && obj2.IsSyncing && obj2.Parent != parent && 
                    !m_serverHierarchyChangedSet.Contains(obj2.Parent))
                {
                    // The game object has a different parent on the server. Apply hierarchy changes to its parent.
                    ApplyHierarchyChanges(obj2.Parent);
                }
                index2++;
            }
        }

        /**
         * Sends hierarchy changes (new children and child order) for an object to the server on the next update. If
         * the children are locked, reverts them to their server location.
         */
        public void SyncHierarchyNextUpdate(sfObject parent)
        {
            m_localHierarchyChangedSet.Add(parent);
        }

        /**
         * Sends hierarchy changes (new children and child order) for an object to the server. If the children are
         * locked, reverts them to their server location.
         */
        public void SyncHierarchy(sfObject parent)
        {
            if (parent.IsFullyLocked)
            {
                // Put the parent in the server changed set so it is reverted to the server state at the end of the
                // frame.
                m_serverHierarchyChangedSet.Add(parent);
                return;
            }
            // 1: server order, 2: client order
            List<GameObject> children2 = GetChildGameObjects(parent);
            if (children2 == null)
            {
                return;
            }
            // If this is not an undo/redo, track the old parents so we can register them in an undo operation
            HashSet<sfObject> oldParents = sfUndoManager.Get().IsHandlingUndoRedo ? null : new HashSet<sfObject>();
            Transform parentTransform = sfObjectMap.Get().Get<Transform>(parent);
            int index1 = 0;
            IEnumerator<sfObject> iter = parent.Children.GetEnumerator();
            bool iterValid = iter.MoveNext();
            // Iterate the client children
            for (int index2 = 0; index2 < children2.Count; index2++)
            {
                GameObject gameObject2 = children2[index2];
                sfObject obj2 = sfObjectMap.Get().GetSFObject(gameObject2);
                if (obj2 == null || !obj2.IsSyncing)
                {
                    // gameObject2 is not synced. Ignore it.
                    continue;
                }
                bool moved = true;
                // Iterate the server children
                while (iterValid)
                {
                    sfObject obj1 = iter.Current;
                    if (obj1 == obj2)
                    {
                        // We found the matching child. We don't need to move it.
                        moved = false;
                        index1++;
                        iterValid = iter.MoveNext();
                        break;
                    }
                    GameObject gameObject1 = sfObjectMap.Get().Get<GameObject>(obj1);
                    if (gameObject1 == null || gameObject1.transform.parent != parentTransform)
                    {
                        // The server object has no game object or the game object has a different parent. The parent
                        // change will be sent when we sync the hierarchy for the new parent. Ignore it and continue
                        // iterating.
                        index1++;
                        iterValid = iter.MoveNext();
                        continue;
                    }
                    // The child is not where we expected it. Either it needs to be moved or one or more other children
                    // need to be moved. eg. if the server has ABC and the client has CAB, you could move C to index 0,
                    // or you could move A to index 2, then B to index 2. We move the child if it is selected (since it
                    // needs to be selected to move it in the hierarchy), or if it has a different parent on the
                    // server.
                    if (obj2.Parent != parent || Selection.Contains(gameObject2))
                    {
                        break;
                    }
                    index1++;
                    iterValid = iter.MoveNext();
                }
                if (!moved)
                {
                    continue;
                }
                if (!obj2.IsLocked)
                {
                    if (oldParents != null)
                    {
                        oldParents.Add(obj2.Parent);
                    }
                    if (obj2.Parent != parent)
                    {
                        parent.InsertChild(index1, obj2);
                        index1++;
                    }
                    else
                    {
                        int oldIndex = parent.Children.IndexOf(obj2);
                        if (oldIndex < index1)
                        {
                            index1--;
                        }
                        obj2.SetChildIndex(index1);
                        index1++;
                    }
                }
                else
                {
                    // Put the object's parent in the server changed set so it is reverted to the server state at the
                    // end of the frame.
                    m_serverHierarchyChangedSet.Add(obj2.Parent);
                }
            }
            if (oldParents != null && oldParents.Count > 0)
            {
                sfUndoManager.Get().Record(new sfUndoHierarchyOperation(oldParents, parent));
            }
        }

        /**
         * Syncs the unpacking of a prefab instance. If the object is locked, repacks the prefab. Does nothing if not
         * called on the root object of an unpacked prefab instance.
         * 
         * @param   GameObject gameObject root of the unpacked prefab instance.
         * @param   bool applyServerStateOutOfPrefab - if true, will apply the server state to descendants that are not
         *          part of the prefab, and will destroy unsynced components in the prefab.
         */
        public void SyncPrefabUnpacking(GameObject gameObject, bool applyServerState = false)
        {
            sfObject obj = sfObjectMap.Get().GetSFObject(gameObject);
            if (obj == null || !obj.IsSyncing)
            {
                return;
            }
            sfDictionaryProperty properties = (sfDictionaryProperty)obj.Property;
            sfBaseProperty prop;
            // Unpacking a prefab must be done through the root, so only send changes if this was the root of the
            // prefab.
            if (!properties.TryGetField(sfProp.Path, out prop) || properties.HasField(sfProp.ChildIndexes))
            {
                return;
            }
            string prefabPath;
            uint[] childIndexes;
            sfUnityUtils.GetPrefabInfo(gameObject, out prefabPath, out childIndexes);
            if (prefabPath != null && prefabPath == sfPropertyUtils.ToString(prop))
            {
                // The prefab was not unpacked.
                return;
            }
            if (obj.IsLocked)
            {
                // Revert to the server state.
                OnPrefabPathChange(gameObject, prop);
                if (applyServerState)
                {
                    ApplyServerState(obj, true);
                }
                return;
            }
            if (prefabPath == null)
            {
                properties.RemoveField(sfProp.Path);
            }
            else
            {
                properties[sfProp.Path] = sfPropertyUtils.FromString(prefabPath);
            }
            sfComponentTranslator translator = sfObjectEventDispatcher.Get().GetTranslator<sfComponentTranslator>(
                sfType.Component);
            // Resend the properties since unpacking a prefab changes the default values.
            SyncProperties(gameObject);
            // Sync prefab path and properties for descendants that were part of the unpacked prefab instance.
            sfUnityUtils.ForEachDescendant(gameObject, (GameObject child) =>
            {
                sfObject childObj = sfObjectMap.Get().GetSFObject(child);
                if (childObj == null || !childObj.IsSyncing)
                {
                    if (applyServerState)
                    {
                        DestroyGameObject(child);
                    }
                    return false;
                }
                sfDictionaryProperty props = (sfDictionaryProperty)childObj.Property;
                if (!props.HasField(sfProp.Path) || !props.HasField(sfProp.ChildIndexes))
                {
                    // This wasn't part of the prefab instance.
                    if (applyServerState)
                    {
                        ApplyServerState(childObj, true);
                    }
                    return false;
                }
                sfMissingPrefab missingPrefab = child.GetComponent<sfMissingPrefab>();
                if (missingPrefab != null)
                {
                    // This is a missing part of the prefab. Remove the sfMissingPrefab component.
                    UObject.DestroyImmediate(missingPrefab);
                }
                sfUnityUtils.GetPrefabInfo(child, out prefabPath, out childIndexes);
                if (prefabPath == null)
                {
                    props.RemoveField(sfProp.Path);
                    props.RemoveField(sfProp.ChildIndexes);
                }
                else
                {
                    props[sfProp.Path] = prefabPath;
                    if (childIndexes == null)
                    {
                        props.RemoveField(sfProp.ChildIndexes);
                    }
                    else
                    {
                        props[sfProp.ChildIndexes] = childIndexes;
                    }
                }
                SyncProperties(child);
                if (applyServerState)
                {
                    // Destroy unsynced components
                    foreach (Component component in child.GetComponents<Component>())
                    {
                        if (!sfObjectMap.Get().Contains(component) && translator.IsSyncable(component))
                        {
                            translator.DestroyComponent(component);
                        }
                    }
                }
                return true;
            });
            sfUndoManager.Get().Record(new sfUndoUnpackPrefabOperation(gameObject));
        }

        /**
         * Called when a game object's prefab path is changed by the server. This happens when a prefab instance is
         * unpacked, or a prefab unpacking was undone.
         * 
         * @param   GameObject gameObject whose prefab path changed.
         * @param   sfBaseProperty property that changed. Null if the game object no longer has a prefab path.
         */
        public void OnPrefabPathChange(GameObject gameObject, sfBaseProperty property)
        {
            if (property == null)
            {
                // Unpack the game object until it is no longer the root of a prefab instance. We unpack one level at a
                // time in case the descendants are still part of a nested prefab instance.
                while (PrefabUtility.IsOutermostPrefabInstanceRoot(gameObject))
                {
                    PrefabUtility.UnpackPrefabInstance(gameObject, PrefabUnpackMode.OutermostRoot,
                        InteractionMode.AutomatedAction);
                    sfHierarchyWatcher.Get().MarkHierarchyStale();
                }
                sfMissingPrefab missingPrefab = gameObject.GetComponent<sfMissingPrefab>();
                if (missingPrefab != null)
                {
                    UObject.DestroyImmediate(missingPrefab);
                }
                else
                {
                    // Reapply properties next frame
                    m_applyPropertiesSet.Add(gameObject);
                }
            }
            else
            {
                string path = sfPropertyUtils.ToString(property);
                string currentPath = "";
                // Unpack the prefab until we get the correct prefab or are no longer the root of a prefab instance.
                while (PrefabUtility.IsOutermostPrefabInstanceRoot(gameObject))
                {
                    GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                    if (prefab != null)
                    {
                        currentPath = AssetDatabase.GetAssetPath(prefab);
                        if (path == currentPath)
                        {
                            // We unpacked to the correct prefab.
                            // Reapply properties next frame
                            m_applyPropertiesSet.Add(gameObject);
                            return;
                        }
                    }
                    PrefabUtility.UnpackPrefabInstance(gameObject, PrefabUnpackMode.OutermostRoot,
                        InteractionMode.AutomatedAction);
                    sfHierarchyWatcher.Get().MarkHierarchyStale();
                }
                // Unity doesn't let us reconnect a game object to a prefab so we destroy and recreate it.
                sfObject obj = RemoveMapping(gameObject);
                if (obj != null)
                {
                    obj.ForEachDescendant((sfObject child) =>
                    {
                        RemoveMapping(child);
                        return true;
                    });
                }
                DestroyGameObject(gameObject);
                // Recreate it in PreUpdate after we receive all property changes
                m_recreateList.Add(property.GetContainerObject());
            }
        }

        /**
         * Temporarily unlocks a game object, and relocks it on the next update.
         * 
         * @param   GameObject gameObject to unlock temporarily.
         */
        public void TempUnlock(GameObject gameObject)
        {
            if ((gameObject.hideFlags & HideFlags.NotEditable) != HideFlags.None)
            {
                m_relockObjects = true;// Relock objects on the next update
                sfUnityUtils.RemoveFlags(gameObject, HideFlags.NotEditable);
                m_tempUnlockedObjects.Add(gameObject);
            }
        }

        /**
         * Sends property changes for a game object and its components to the server. Reverts them to the server state
         * if the object is locked.
         * 
         * @param   GameObject gameObject to sync properties for.
         */
        public void SyncProperties(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }
            sfObject obj = sfObjectMap.Get().GetSFObject(gameObject);
            if (obj == null || !obj.IsSyncing)
            {
                return;
            }
            if (obj.IsLocked)
            {
                sfPropertyManager.Get().ApplyProperties(gameObject, (sfDictionaryProperty)obj.Property);
            }
            else
            {
                sfPropertyManager.Get().SendPropertyChanges(gameObject, (sfDictionaryProperty)obj.Property);
            }

            foreach (Component component in gameObject.GetComponents<Component>())
            {
                obj = sfObjectMap.Get().GetSFObject(component);
                if (obj == null || !obj.IsSyncing)
                {
                    continue;
                }
                if (obj.IsLocked)
                {
                    sfPropertyManager.Get().ApplyProperties(component, (sfDictionaryProperty)obj.Property);
                }
                else
                {
                    sfPropertyManager.Get().SendPropertyChanges(component, (sfDictionaryProperty)obj.Property);
                }
            }
        }

        /**
         * Applies the server state to a game object and its components.
         * 
         * @param   sfObject obj for the game object to apply server state for.
         * @param   bool recursive - if true, will also apply server state to descendants of the game object.
         */
        public void ApplyServerState(sfObject obj, bool recursive = false)
        {
            if (obj.Type != sfType.GameObject)
            {
                return;
            }
            GameObject gameObject = sfObjectMap.Get().Get<GameObject>(obj);
            if (gameObject == null)
            {
                OnCreate(obj, obj.Parent.Children.IndexOf(obj));
                return;
            }
            m_serverHierarchyChangedSet.Add(obj.Parent);
            sfPropertyManager.Get().ApplyProperties(gameObject, (sfDictionaryProperty)obj.Property);
            sfComponentTranslator translator = sfObjectEventDispatcher.Get().GetTranslator<sfComponentTranslator>(
                sfType.Component);
            int index = -1;
            foreach (sfObject child in obj.Children)
            {
                index++;
                if (child.Type != sfType.Component)
                {
                    continue;
                }
                Component component = sfObjectMap.Get().Get<Component>(child);
                if (component == null)
                {
                    translator.OnCreate(child, index);
                }
                else
                {
                    sfPropertyManager.Get().ApplyProperties(component, (sfDictionaryProperty)child.Property);
                    if (recursive && component is Transform)
                    {
                        foreach (sfObject grandChild in child.Children)
                        {
                            if (grandChild.Type == sfType.GameObject)
                            {
                                ApplyServerState(grandChild, true);
                            }
                        }
                    }
                }
            }
            // Destroy unsynced components
            foreach (Component component in gameObject.GetComponents<Component>())
            {
                if (!sfObjectMap.Get().Contains(component) && translator.IsSyncable(component))
                {
                    translator.DestroyComponent(component);
                }
            }
            if (recursive)
            {
                // Destroy unsynced children
                foreach (Transform child in gameObject.transform)
                {
                    if (!sfObjectMap.Get().Contains(child.gameObject) && IsSyncable(child.gameObject))
                    {
                        DestroyGameObject(child.gameObject);
                    }
                }
            }
        }

        /**
         * Called when an undo operation is registered on the undo stack. Syncs changes made from undoing a prefab
         * instance revert.
         */
        private void OnRegisterUndo()
        {
            // If the undo name starts with "revert" we may have reverted a prefab instance.
            if (!Undo.GetCurrentGroupName().ToLower().StartsWith("revert"))
            {
                return;
            }
            GameObject[] gameObjects = Selection.gameObjects;
            foreach (GameObject gameObject in gameObjects)
            {
                SyncProperties(gameObject);
            }
            sfUndoManager.Get().Record(new sfUndoRevertOperation(gameObjects));
        }

        /**
         * Called when the user completes dragging objects in the hierarchy. Syncs parent changes to the dragged game
         * objects on the next update.
         * 
         * @param   GameObject target the objects were dragged onto.
         */
        private void OnHierarchyDragComplete(GameObject target)
        {
            // Relock objects on the next update that were temporarily unlocked to make dragging work. If we try to
            // relock them now, Unity will cancel reparenting if the parent is locked.
            m_relockObjects = true;
            // We have to wait until the next upate before checking the game object's parents since Unity hasn't 
            // changed their parents yet.
            foreach (UObject uobj in DragAndDrop.objectReferences)
            {
                GameObject gameObject = uobj as GameObject;
                if (gameObject != null)
                {
                    m_hierarchyDraggedObjects.Add(gameObject);
                }
            }
        }

        /**
         * Validates a hierarchy drag operation. A drag operation is allowed if the target is not fully locked and all
         * dragged objects are unlocked.
         * 
         * @param   GameObject target parent for the dragged objects.
         * @param   int childIndex the dragged objects will be inserted at.
         * @return  bool true if the drag should be allowed.
         */
        private bool ValidateHierarchyDrag(GameObject target, int childIndex)
        {
            sfObject targetObj = sfObjectMap.Get().GetSFObject(target);
            // If the target is locked, temporarily unlock it. We need to unlock partially locked objects to allow
            // children to be added to them. We need to unlock fully locked objects as well because keeping them
            // locked interferes with drag target detection and causes flickering.
            if (targetObj != null && targetObj.IsLocked &&
                (target.hideFlags & HideFlags.NotEditable) != HideFlags.None)
            {
                sfUnityUtils.RemoveFlags(target, HideFlags.NotEditable);
                m_tempUnlockedObjects.Add(target);
            }

            if (targetObj != null && targetObj.IsFullyLocked)
            {
                return false;
            }
            // Do not allow inserting before a missing prefab that is not the root of the prefab.
            if (target != null && childIndex < target.transform.childCount)
            {
                sfMissingPrefab missingPrefab = target.transform.GetChild(childIndex).GetComponent<sfMissingPrefab>();
                if (missingPrefab != null && missingPrefab.ChildIndexes != null)
                {
                    return false;
                }
            }
            foreach (UObject uobj in DragAndDrop.objectReferences)
            {
                sfObject obj = sfObjectMap.Get().GetSFObject(uobj);
                if (obj != null && obj.IsLocked)
                {
                    return false;
                }
                GameObject gameObject = uobj as GameObject;
                if (gameObject == null)
                {
                    continue;
                }
                // Disallow dragging a missing prefab that is not the root of the prefab.
                sfMissingPrefab missingPrefab = gameObject.GetComponent<sfMissingPrefab>();
                if (missingPrefab != null && missingPrefab.ChildIndexes != null)
                {
                    return false;
                }
            }
            return true;
        }

        /**
         * Adds a game object's parent's sfObject to the set of objects with child game objects to upload.
         * 
         * @param   GameObject gameObject
         */
        private void AddParentToUploadSet(GameObject gameObject)
        {
            if (!IsSyncable(gameObject))
            {
                return;
            }
            sfObject parent;
            if (gameObject.transform.parent == null)
            {
                // The parent object is a hiearchy object
                sfSceneTranslator sceneTranslator = sfObjectEventDispatcher.Get()
                    .GetTranslator<sfSceneTranslator>(sfType.Hierarchy);
                parent = sceneTranslator.GetHierarchyObject(gameObject.scene);
            }
            else
            {
                // The parent object is a transform
                parent = sfObjectMap.Get().GetSFObject(gameObject.transform.parent);
            }
            if (parent != null)
            {
                m_parentsWithNewChildren.Add(parent);
            }
        }

        /**
         * Uploads new child game objects of objects in the parents-with-new-children set to the server.
         */
        private void UploadGameObjects()
        {
            if (m_parentsWithNewChildren.Count == 0)
            {
                return;
            }
            sfSession session = SceneFusion.Get().Service.Session;
            List<sfObject> uploadList = new List<sfObject>();
            foreach (sfObject parent in m_parentsWithNewChildren)
            {
                int index = 0; // Child index of first uploaded object
                // Check for new child game objects to upload
                foreach (GameObject gameObject in IterateChildGameObjects(parent))
                {
                    sfObject obj = sfObjectMap.Get().GetSFObject(gameObject);
                    if (obj != null && obj.IsSyncing)
                    {
                        // Objects uploaded together must be in a continuous sequence, so when we find an object that
                        // is already uploaded, upload the upload list if it's non-empty.
                        if (uploadList.Count > 0)
                        {
                            session.Create(uploadList, parent, index);
                            index += uploadList.Count;
                            uploadList.Clear();
                        }
                        index++;
                    }
                    else if ((obj == null || !obj.IsDeletePending) && IsSyncable(gameObject))
                    {
                        // If the parent is locked, delete the new game object.
                        if (parent.IsFullyLocked)
                        {
                            DestroyGameObject(gameObject);
                        }
                        else
                        {
                            // Found an object to upload. Create an sfObject and add it to the upload list.
                            obj = CreateObject(gameObject);
                            if (obj != null)
                            {
                                uploadList.Add(obj);
                            }
                        }
                    }
                }
                // Upload the objects
                if (uploadList.Count > 0)
                {
                    session.Create(uploadList, parent, index);
                    uploadList.Clear();
                }
            }
            m_parentsWithNewChildren.Clear();
        }

        /**
         * Gets the child game objects of an sfObject's scene or transform.
         * 
         * @param   sfObject parent for the scene or transform to get child game objects from.
         * @return  List<GameObject> children of the object. Null if the transform could not be found.
         */
        public List<GameObject> GetChildGameObjects(sfObject parent)
        {
            if (parent.Type == sfType.Hierarchy)
            {
                sfSceneTranslator sceneTranslator = sfObjectEventDispatcher.Get()
                    .GetTranslator<sfSceneTranslator>(sfType.Hierarchy);
                Scene scene = sceneTranslator.GetScene(parent);
                if (scene.isLoaded)
                {
                    List<GameObject> children = new List<GameObject>();
                    scene.GetRootGameObjects(children);
                    return children;
                }
            }
            else if (parent.Type == sfType.Component)
            {
                Transform transform = sfObjectMap.Get().Get<Transform>(parent);
                if (transform != null)
                {
                    List<GameObject> children = new List<GameObject>();
                    foreach (Transform child in transform)
                    {
                        children.Add(child.gameObject);
                    }
                    return children;
                }
            }
            return null;
        }

        /**
         * Iterates the child game objects of an sfObject's scene or transform.
         * 
         * @param   sfObject parent for the scene or transform to iterate.
         * @return  IEnumerable<GameObject>
         */
        public IEnumerable<GameObject> IterateChildGameObjects(sfObject parent)
        {
            if (parent.Type == sfType.Hierarchy)
            {
                sfSceneTranslator sceneTranslator = sfObjectEventDispatcher.Get()
                    .GetTranslator<sfSceneTranslator>(sfType.Hierarchy);
                Scene scene = sceneTranslator.GetScene(parent);
                if (scene.isLoaded)
                {
                    List<GameObject> roots = new List<GameObject>();
                    scene.GetRootGameObjects(roots);
                    foreach (GameObject gameObject in roots)
                    {
                        yield return gameObject;
                    }
                }
            }
            else if (parent.Type == sfType.Component)
            {
                Transform transform = sfObjectMap.Get().Get<Transform>(parent);
                if (transform != null)
                {
                    foreach (Transform child in transform)
                    {
                        yield return child.gameObject;
                    }
                }
            }
        }

        /**
         * Called when a game object's parent is changed by another user.
         * 
         * @param   sfObject obj whose parent changed.
         * @param   int childIndex of the object. -1 if the object is a root.
         */
        public override void OnParentChange(sfObject obj, int childIndex)
        {
            if (obj.Parent != null)
            {
                // Apply the change at the end of the frame.
                m_serverHierarchyChangedSet.Add(obj.Parent);
            }
        }

        /**
         * Creates an sfObject for a uobject. Does not upload or create properties for the object.
         *
         * @param   UObject uobj to create sfObject for.
         * @param   sfObject outObj created for the uobject.
         * @return  bool true if the uobject was handled by this translator.
         */
        public override bool TryCreate(UObject uobj, out sfObject outObj)
        {
            outObj = null;
            GameObject gameObject = uobj as GameObject;
            if (gameObject == null)
            {
                return false;
            }
            if (IsSyncable(gameObject))
            {
                outObj = new sfObject(sfType.GameObject, new sfDictionaryProperty());
                AddMapping(outObj, gameObject);
            }
            return true;
        }

        /**
         * Recusively creates sfObjects for a game object and its children.
         * 
         * @param   GameObject gameObject to create sfObject for.
         * @return  sfObject for the gameObject.
         */
        public sfObject CreateObject(GameObject gameObject)
        {
            sfObject obj = sfObjectMap.Get().GetOrCreateSFObject(gameObject, sfType.GameObject);
            if (obj.IsSyncing)
            {
                return null;
            }
            m_instanceIdToSFObjectMap[gameObject.GetInstanceID()] = obj;

            Guid guid = sfGuidManager.Get().GetGuid(gameObject);
            if (sfGuidManager.Get().GetGameObject(guid) != gameObject)
            {
                // If the game object's guid is mapped to a different game object, this is a duplicate object.
                // Duplicate objects can be created when a user deletes a locked object which is recreated because it
                // was locked, and then undoes the delete. Destroy the duplicate object.
                RemoveMapping(gameObject);
                DestroyGameObject(gameObject);
                return null;
            }
            sfDictionaryProperty properties = (sfDictionaryProperty)obj.Property;
            string path;
            uint[] childIndexes;
            RemoveInvalidMissingPrefab(gameObject);
            sfUnityUtils.GetPrefabInfo(gameObject, out path, out childIndexes);
            if (!string.IsNullOrEmpty(path))
            {
                properties[sfProp.Path] = sfPropertyUtils.FromString(path);
                if (childIndexes != null)
                {
                    properties[sfProp.ChildIndexes] = childIndexes;
                }
            }

            if (Selection.Contains(gameObject))
            {
                obj.RequestLock();
            }

            properties[sfProp.Guid] = guid.ToByteArray();
            sfPropertyManager.Get().CreateProperties(gameObject, properties);

            // Create component child objects
            sfComponentTranslator translator = sfObjectEventDispatcher.Get().GetTranslator<sfComponentTranslator>(
                sfType.Component);
            bool isFirst = true;
            foreach (Component component in GetComponents(gameObject))
            {
                if (translator.IsSyncable(component))
                {
                    // If the component's game object is not this game object, this is a prefab component that was
                    // removed from the prefab instance.
                    bool isRemoved = component.gameObject != gameObject;
                    sfObject child = translator.CreateObject(component, isFirst, isRemoved);
                    isFirst = false;
                    if (child != null)
                    {
                        obj.AddChild(child);
                    }
                }
            }

            InvokeOnLockStateChange(obj, gameObject);
            return obj;
        }

        /**
         * Removes invalid missing prefab components from a game object. A missing component is invalid if it has
         * child indexes but is not located at the correct index.
         * 
         * @param   GameObject gameObject to check for and remove missing prefab components from.
         */
        private void RemoveInvalidMissingPrefab(GameObject gameObject)
        {
            sfMissingPrefab missingPrefab = gameObject.GetComponent<sfMissingPrefab>();
            if (missingPrefab == null)
            {
                return;
            }
            // Prevent the missing prefab component from being removed
            missingPrefab.hideFlags = HideFlags.NotEditable;
            // If the missing prefab has no child indexes, it is not invalid.
            if (missingPrefab.ChildIndexes == null)
            {
                CreateMissingPrefabNotification(missingPrefab);
                return;
            }
            // If the game object has no parent, destroy the missing prefab component.
            if (gameObject.transform.parent == null)
            {
                UObject.DestroyImmediate(missingPrefab);
                return;
            }
            // If the missing prefab component is at the wrong index, destroy it.
            uint index = missingPrefab.ChildIndexes[missingPrefab.ChildIndexes.Length - 1];
            if (gameObject.transform.parent.childCount <= index ||
                gameObject.transform.parent.GetChild((int)index) != gameObject.transform)
            {
                UObject.DestroyImmediate(missingPrefab);
                return;
            }
            CreateMissingPrefabNotification(missingPrefab);
        }

        /**
         * Creates a notification for a missing prefab.
         * 
         * @param   sfMissingPrefab missingPrefab to create notification for.
         */
        private void CreateMissingPrefabNotification(sfMissingPrefab missingPrefab)
        {
            sfNotification.Create(sfNotificationCategory.MissingPrefab,
                    "Unable to load prefab '" + missingPrefab.PrefabPath + "'.", missingPrefab.gameObject);
        }

        /**
         * Called when new assets are created. Removes sfMissingPrefab components from new prefab assets.
         * 
         * @param   string[] assets that were created.
         */
        private void OnNewAssets(string[] assets)
        {
            foreach (string path in assets)
            {
                if (path.EndsWith(".prefab"))
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null)
                    {
                        foreach (sfMissingPrefab missingPrefab in prefab.GetComponentsInChildren<sfMissingPrefab>())
                        {
                            UObject.DestroyImmediate(missingPrefab, true);
                        }
                    }
                }
            }
        }

        /**
         * Called when a game object is created by another user.
         * 
         * @param   sfObject obj that was created.
         * @param   int childIndex of the new object. -1 if the object is a root.
         */
        public override void OnCreate(sfObject obj, int childIndex)
        {
            if (obj.Parent == null)
            {
                ksLog.Error(this, "GameObject sfObject has no parent.");
                return;
            }
            GameObject gameObject = null;
            if (obj.Parent.Type == sfType.Hierarchy)
            {
                sfSceneTranslator sceneTranslator = sfObjectEventDispatcher.Get()
                    .GetTranslator<sfSceneTranslator>(sfType.Hierarchy);
                Scene scene = sceneTranslator.GetScene(obj.Parent);
                if (scene.isLoaded)
                {
                    gameObject = InitializeGameObject(obj, scene);
                    if (gameObject != null && gameObject.transform.parent != null)
                    {
                        gameObject.transform.SetParent(null, false);
                    }
                }
            }
            else if (obj.Parent.Type == sfType.Component)
            {
                Transform transform = sfObjectMap.Get().Get<Transform>(obj.Parent);
                if (transform != null)
                {
                    gameObject = InitializeGameObject(obj, transform.gameObject.scene);
                    if (gameObject != null)
                    {
                        gameObject.transform.SetParent(transform, false);
                    }
                }
            }
            else
            {
                return;
            }
            // If the game object is the last child it will be in the correct location, unless it is a root prefab.
            if (gameObject != null && (childIndex != obj.Parent.Children.Count - 1 ||
                (PrefabUtility.IsPartOfPrefabInstance(gameObject) && gameObject.transform.parent == null)))
            {
                m_serverHierarchyChangedSet.Add(obj.Parent);
            }
        }

        /**
         * Creates or finds a game object for an sfObject and initializes it with server values. Recursively
         * initializes children.
         * 
         * @param   sfObject obj to initialize game object for.
         * @param   Scene scene the game object belongs to.
         * @return  GameObject gameObject for the sfObject.
         */
        public GameObject InitializeGameObject(sfObject obj, Scene scene)
        {
            sfDictionaryProperty properties = (sfDictionaryProperty)obj.Property;
            // Try get the prefab path and child indexes properties
            string path = null;
            uint[] childIndexes = null;
            sfBaseProperty property;
            if (properties.TryGetField(sfProp.Path, out property))
            {
                path = sfPropertyUtils.ToString(property);
                if (properties.TryGetField(sfProp.ChildIndexes, out property))
                {
                    childIndexes = (uint[])property;
                }
            }

            // Try get the game object by its guid
            Guid guid = new Guid((byte[])properties[sfProp.Guid]);
            GameObject gameObject = sfGuidManager.Get().GetGameObject(guid);
            if (gameObject != null && !ValidateGameObject(gameObject, path, childIndexes))
            {
                // The game object is not the correct prefab. Remove it from the guid manager and don't use it.
                sfGuidManager.Get().Remove(gameObject);
                gameObject = null;
            }
            if (gameObject == null && childIndexes != null && childIndexes.Length > 0)
            {
                // Try find the child from the prefab
                gameObject = FindChild(obj.Parent, (int)childIndexes[childIndexes.Length - 1]);
                if (gameObject != null)
                {
                    if (!ValidateGameObject(gameObject, path, childIndexes))
                    {
                        gameObject = null;
                    }
                    else
                    {
                        sfGuidManager.Get().SetGuid(gameObject, guid);
                    }
                }
            }

            // Create the game object if we couldn't find it by its guid
            sfMissingPrefab missingPrefab = null;
            if (gameObject == null)
            {
                bool isMissingPrefab = false;
                if (path != null && childIndexes == null)
                {
                    gameObject = sfUnityUtils.InstantiatePrefab(scene, path);
                    if (gameObject == null)
                    {
                        gameObject = new GameObject();
                        SceneManager.MoveGameObjectToScene(gameObject, scene);
                        isMissingPrefab = true;
                    }
                }
                else
                {
                    gameObject = new GameObject();
                    SceneManager.MoveGameObjectToScene(gameObject, scene);
                    isMissingPrefab = path != null;
                }
                if (isMissingPrefab)
                {
                    missingPrefab = gameObject.AddComponent<sfMissingPrefab>();
                    missingPrefab.PrefabPath = path;
                    missingPrefab.ChildIndexes = childIndexes;
                    // Prevent the missing prefab component from being removed.
                    missingPrefab.hideFlags = HideFlags.NotEditable;
                }
                sfGuidManager.Get().SetGuid(gameObject, guid);
                sfUI.Get().MarkSceneViewStale();
            }
            else
            {
                // Send a lock request if we have the game object selected
                if (Selection.Contains(gameObject))
                {
                    obj.RequestLock();
                }

                if (path != null)
                {
                    missingPrefab = gameObject.GetComponent<sfMissingPrefab>();
                    if (missingPrefab != null)
                    {
                        // Prevent the missing prefab component from being removed.
                        missingPrefab.hideFlags = HideFlags.NotEditable;
                    }
                }
            }
            AddMapping(obj, gameObject);
            if (missingPrefab != null)
            {
                CreateMissingPrefabNotification(missingPrefab);
            }

            sfPropertyManager.Get().ApplyProperties(gameObject, properties);

            // Set references to this game object
            sfReferenceProperty[] references = SceneFusion.Get().Service.Session.GetReferences(obj);
            sfPropertyManager.Get().SetReferences(gameObject, references);

            // Set the parent
            if (obj.Parent != null)
            {
                if (obj.Parent.Type == sfType.Hierarchy)
                {
                    if (gameObject.transform.parent != null)
                    {
                        gameObject.transform.SetParent(null, false);
                    }
                }
                else
                {
                    Transform parent = sfObjectMap.Get().Get<Transform>(obj.Parent);
                    if (parent != null && gameObject.transform.parent != parent)
                    {
                        gameObject.transform.SetParent(parent, false);
                    }
                }
            }

            // Initialize children
            sfComponentTranslator translator = sfObjectEventDispatcher.Get().GetTranslator<sfComponentTranslator>(
                sfType.Component);
            sfComponentFinder finder = new sfComponentFinder(gameObject);
            int index = 0;
            foreach (sfObject child in obj.Children)
            {
                if (child.Type == sfType.Component)
                {
                    translator.InitializeComponent(gameObject, child, finder);
                }
                else
                {
                    sfObjectEventDispatcher.Get().OnCreate(child, index);
                }
                index++;
            }
            // Destroy unsynced components
            foreach (Component component in gameObject.GetComponents<Component>())
            {
                if (!sfObjectMap.Get().Contains(component) && translator.IsSyncable(component))
                {
                    translator.DestroyComponent(component);
                }
            }
            // Sync component order
            if (!finder.InOrder)
            {
                translator.ApplyComponentOrder(gameObject);
            }

            if (obj.IsLocked)
            {
                OnLock(obj);
            }
            InvokeOnLockStateChange(obj, gameObject);
            return gameObject;
        }

        /**
         * Validates that a game object is the correct prefab and is not already in the object map.
         * 
         * @param   GameObject gameObject to validate.
         * @param   string path to prefab the game object should be an instance of.
         */
        private bool ValidateGameObject(GameObject gameObject, string path, uint[] childIndexes)
        {
            string currentPath;
            uint[] currentChildIndexes;
            sfUnityUtils.GetPrefabInfo(gameObject, out currentPath, out currentChildIndexes);
            return currentPath == path && sfUnityUtils.ListsEqual(currentChildIndexes, childIndexes) &&
                !sfObjectMap.Get().Contains(gameObject);
        }

        /**
         * Returns the child game object of an sfObject at the given index.
         * 
         * @param   sfObject obj to get child from.
         * @param   int childIndex
         * @return  GameObject gameObject child at the given index, or null if not found.
         */
        private GameObject FindChild(sfObject obj, int childIndex)
        {
            if (obj == null)
            {
                return null;
            }
            Transform parent = sfObjectMap.Get().Get<Transform>(obj);
            if (parent == null)
            {
                return null;
            }
            // If the first child is the lock object, increase the child index by one.
            if (parent.childCount > 0 &&
                (parent.GetChild(0).hideFlags & HideFlags.HideAndDontSave) == HideFlags.HideAndDontSave &&
                parent.GetChild(0).name == sfLockManager.LOCK_OBJECT_NAME)
            {
                childIndex++;
            }
            if (parent.childCount > childIndex)
            {
                return parent.GetChild(childIndex).gameObject;
            }
            return null;
        }

        /**
         * Called when a locally created object is confirmed as created.
         * 
         * @param   sfObject obj that whose creation was confirmed.
         */
        public override void OnConfirmCreate(sfObject obj)
        {
            sfHierarchyWatcher.Get().MarkHierarchyStale();
        }

        /**
         * Called when a game object is deleted by another user.
         * 
         * @param   sfObject obj that was deleted.
         */
        public override void OnDelete(sfObject obj)
        {
            GameObject gameObject = sfObjectMap.Get().Get<GameObject>(obj);
            // Destroy the game object
            if (gameObject != null)
            {
                sfUI.Get().MarkSceneViewStale();
                sfUI.Get().MarkInspectorStale(gameObject, true);
                DestroyGameObject(gameObject);
                sfHierarchyWatcher.Get().MarkHierarchyStale();
                if (gameObject != null)
                {
                    // Clears the properties and parent/child connections for the object and its descendants, then
                    // reuploads the game object, reusing the sfObjects to preserve ids.
                    OnConfirmDelete(obj);
                    return;
                }
            }
            // Remove the game object and its descendants from the guid manager.
            obj.ForSelfAndDescendants((sfObject child) =>
            {
                GameObject go = RemoveMapping(child);
                if (go.IsDestroyed())
                {
                    sfGuidManager.Get().Remove(go);
                }
                return true;
            });
        }

        /**
         * Called when a locally-deleted game object is confirmed as deleted.
         * 
         * @param   sfObject obj that was confirmed as deleted.
         */
        public override void OnConfirmDelete(sfObject obj)
        {
            // Clear the properties and children recursively, but keep the objects around so they can be resused to
            // preserve ids if the game object is recreated.
            obj.ForSelfAndDescendants((sfObject child) =>
            {
                child.Property = new sfDictionaryProperty();
                if (child.Parent != null)
                {
                    child.Parent.RemoveChild(child);
                }
                GameObject gameObject = sfObjectMap.Get().Get<GameObject>(child);
                if (gameObject != null)
                {
                    AddParentToUploadSet(gameObject);
                }
                return true;
            });
        }

        /**
         * Destroys a game object. Logs a warning if the game object could not be destroyed, which occurs if the game
         * object is part of a prefab instance and is not the root of that prefab instance.
         * 
         * @param   GameObject gameObject to destroy.
         */
        public void DestroyGameObject(GameObject gameObject)
        {
            // Remove all notifications for the game object and its descendants.
            sfUnityUtils.ForSelfAndDesendants(gameObject, (GameObject child) =>
            {
                sfNotificationManager.Get().RemoveNotificationsFor(child);
                foreach (Component component in child.GetComponents<Component>())
                {
                    if (component != null)
                    {
                        sfNotificationManager.Get().RemoveNotificationsFor(component);
                    }
                }
                return true;
            });
            EditorUtility.SetDirty(gameObject);
            try
            {
                UObject.DestroyImmediate(gameObject);
            }
            catch (Exception e)
            {
                if (gameObject != null)
                {
                    ksLog.Warning(this, "Unable to destroy game object '" + gameObject.name + "': " + e.Message);
                    // If the object was locked, we want to unlock it.
                    sfUnityUtils.RemoveFlags(gameObject, HideFlags.NotEditable);
                }
                else
                {
                    ksLog.LogException(this, e);
                }
            }
        }

        /**
         * Called when a game object is deleted locally. Deletes the game object on the server, or recreates it if the
         * game object is locked.
         * 
         * @param   int instanceId of game object that was deleted
         */
        private void OnDeleteGameObject(int instanceId)
        {
            // Do not remove the sfObject so it will be reused if the game object is recreated and references to it
            // will be preserved.
            sfObject obj;
            if (!m_instanceIdToSFObjectMap.TryGetValue(instanceId, out obj) || !obj.IsSyncing)
            {
                return;
            }
            // Remove the notifications for the deleted objects.
            obj.ForSelfAndDescendants((sfObject child) =>
            {
                UObject uobj = sfObjectMap.Get().GetUObject(child);
                if ((object)uobj != null)
                {
                    sfNotificationManager.Get().RemoveNotificationsFor(uobj);
                }
                return true;
            });
            if (obj.IsLocked)
            {
                // The object is locked. Recreate it.
                obj.ForSelfAndDescendants((sfObject child) =>
                {
                    if (child.IsLockPending)
                    {
                        child.ReleaseLock();
                    }
                    RemoveMapping(child);
                    return true;
                });
                OnCreate(obj, obj.Parent == null ? -1 : obj.Parent.Children.IndexOf(obj));
            }
            else
            {
                SceneFusion.Get().Service.Session.Delete(obj);
            }
        }

        /**
         * Called when a game object is created locally. Adds the game object's parent sfObject to set of objects with
         * new children to upload.
         * 
         * @param   GameObject gameObject that was created.
         */
        private void OnCreateGameObject(GameObject gameObject)
        {
            sfObject obj = sfObjectMap.Get().GetSFObject(gameObject);
            if (obj == null || !obj.IsSyncing && IsSyncable(gameObject))
            {
                AddParentToUploadSet(gameObject);
            }
        }

        /**
         * Called when a field is removed from a dictionary property.
         * 
         * @param   sfDictionaryProperty dict the field was removed from.
         * @param   string name of the removed field.
         */
        public override void OnRemoveField(sfDictionaryProperty dict, string name)
        {
            base.OnRemoveField(dict, name);
            sfObject obj = dict.GetContainerObject();
            if (!obj.IsLocked)
            {
                return;
            }
            // Gameobjects become unlocked when you set a prefab property to the default value, so we relock it.
            GameObject gameObject = sfObjectMap.Get().Get<GameObject>(obj);
            if (gameObject != null && PrefabUtility.GetPrefabInstanceHandle(gameObject) != null)
            {
                sfUnityUtils.AddFlags(gameObject, HideFlags.NotEditable);
            }
        }

        /**
         * Called when a game object is locked by another user.
         * 
         * @param   sfObject obj that was locked.
         */
        public override void OnLock(sfObject obj)
        {
            GameObject gameObject = sfObjectMap.Get().Get<GameObject>(obj);
            if (gameObject == null)
            {
                OnCreate(obj, obj.Parent == null ? -1 : obj.Parent.Children.IndexOf(obj));
            }
            else
            {
                Lock(gameObject, obj);
                InvokeOnLockStateChange(obj, gameObject);
            }
        }

        /**
         * Called when a game object is unlocked by another user.
         * 
         * @param   sfObject obj that was unlocked.
         */
        public override void OnUnlock(sfObject obj)
        {
            GameObject gameObject = sfObjectMap.Get().Get<GameObject>(obj);
            if (gameObject != null)
            {
                Unlock(gameObject);
                InvokeOnLockStateChange(obj, gameObject);
            }
        }

        /**
         * Called when a game object's lock owner changes.
         * 
         * @param   sfObject obj whose lock owner changed.
         */
        public override void OnLockOwnerChange(sfObject obj)
        {
            GameObject gameObject = sfObjectMap.Get().Get<GameObject>(obj);
            if (gameObject != null)
            {
                InvokeOnLockStateChange(obj, gameObject);
                sfLockManager.Get().UpdateLockMaterial(gameObject, obj);
            }
        }

        /**
         * Locks a game object.
         * 
         * @param   GameObject gameObject to lock.
         * @param   sfObject obj for the game object.
         */
        private void Lock(GameObject gameObject, sfObject obj)
        {
            sfUnityUtils.AddFlags(gameObject, HideFlags.NotEditable);
            sfUI.Get().MarkInspectorStale(gameObject, true);
            sfLockManager.Get().CreateLockObject(gameObject, obj);
        }

        /**
         * Unlocks a game object.
         * 
         * @param   GameObject gameObject to unlock.
         */
        private void Unlock(GameObject gameObject)
        {
            sfUnityUtils.RemoveFlags(gameObject, HideFlags.NotEditable);
            sfUI.Get().MarkInspectorStale(gameObject, true);

            GameObject lockObject = sfLockManager.Get().FindLockObject(gameObject);
            if (lockObject != null)
            {
                UObject.DestroyImmediate(lockObject);
                sfUI.Get().MarkSceneViewStale();
            }
        }

        /**
         * Called before saving a scene. Temporarily unlocks locked game objects in the scene so they are not saved as
         * not editable.
         * 
         * @param   Scene scene that will be saved.
         */
        private void PreSave(Scene scene)
        {
            foreach (GameObject gameObject in sfUnityUtils.IterateGameObjects(scene))
            {
                sfObject obj = sfObjectMap.Get().GetSFObject(gameObject);
                if (obj != null && obj.IsLocked)
                {
                    sfUnityUtils.RemoveFlags(gameObject, HideFlags.NotEditable);
                    m_tempUnlockedObjects.Add(gameObject);
                }
            }
        }

        /**
         * Relocks all game objects that were temporarily unlocked.
         */
        private void RelockObjects()
        {
            foreach (GameObject gameObject in m_tempUnlockedObjects)
            {
                sfObject obj = sfObjectMap.Get().GetSFObject(gameObject);
                if (obj != null && obj.IsLocked)
                {
                    sfUnityUtils.AddFlags(gameObject, HideFlags.NotEditable);
                }
            }
            m_tempUnlockedObjects.Clear();
        }

        /**
         * Called when a uobject is selected. Syncs the object if it is an unsynced game object.
         * 
         * @param   UObject uobj that was selected.
         */
        private void OnSelect(UObject uobj)
        {
            GameObject gameObject = uobj as GameObject;
            if (gameObject == null)
            {
                return;
            }
            sfObject obj = sfObjectMap.Get().GetSFObject(gameObject);
            if (obj == null || !obj.IsSyncing)
            {
                AddParentToUploadSet(gameObject);
            }
        }

        /**
         * Invokes the OnLockStateChange event.
         * 
         * @param   sfObject obj whose lock state changed.
         * @param   GameObject gameObject whose lock state changed.
         */
        private void InvokeOnLockStateChange(sfObject obj, GameObject gameObject)
        {
            sfHierarchyWatcher.Get().MarkHierarchyStale();
            sfUI.Get().MarkInspectorStale(gameObject);
            if (OnLockStateChange == null)
            {
                return;
            }
            LockType lockType = LockType.UNLOCKED;
            if (obj.IsFullyLocked)
            {
                lockType = LockType.FULLY_LOCKED;
            }
            else if (obj.IsPartiallyLocked)
            {
                lockType = LockType.PARTIALLY_LOCKED;
            }
            OnLockStateChange(gameObject, lockType, obj.LockOwner);
        }

        /**
         * Gets components from a game object. If the game object is a prefab instance with some prefab components
         * removed, the component from the prefab will be in the returned list where the prefab instance component was
         * removed.
         * 
         * @param   GameObject gameObject to get components from.
         * @param   IList<Component> components
         */
        private IList<Component> GetComponents(GameObject gameObject)
        {
            Component[] instanceComponents = gameObject.GetComponents<Component>();
            if (!PrefabUtility.IsPartOfPrefabInstance(gameObject))
            {
                return instanceComponents;
            }
            if (PrefabUtility.GetRemovedComponents(gameObject).Count == 0)
            {
                return instanceComponents;
            }
            // The game object is a prefab instance with some prefab components removed. Build a list of components
            // the removed components replaced with the components from the prefab.
            GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
            Component[] prefabComponents = prefab.GetComponents<Component>();
            List<Component> components = new List<Component>();
            int index = 0;
            Component prefabForInstance = index < instanceComponents.Length ?
                PrefabUtility.GetCorrespondingObjectFromSource(instanceComponents[index]) : null;
            foreach (Component prefabComponent in prefabComponents)
            {
                if (prefabForInstance == prefabComponent)
                {
                    // The component was not removed from the intance. Add the instance component to the list and get
                    // the prefab for the next instance component.
                    components.Add(instanceComponents[index]);
                    index++;
                    prefabForInstance = index < instanceComponents.Length ?
                        PrefabUtility.GetCorrespondingObjectFromSource(instanceComponents[index]) : null;
                }
                else
                {
                    // The component was removed from the instance. Add the prefab component to the list.
                    components.Add(prefabComponent);
                }
            }
            // Add the remaining instance components
            for (int i = index; i < instanceComponents.Length; i++)
            {
                components.Add(instanceComponents[i]);
            }
            return components;
        }

        /**
         * Checks if a game object has a component whose type is in the blacklist.
         * 
         * @param   GameObject gameObject to check for blacklisted components.
         * @return  bool true if the gameObject has a blacklisted component.
         */
        private bool HasBlacklistedComponent(GameObject gameObject)
        {
            if (m_blacklist.Count == 0)
            {
                return false;
            }
            foreach (Component component in gameObject.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }
                foreach (Type type in m_blacklist)
                {
                    if (type.IsAssignableFrom(component.GetType()))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
