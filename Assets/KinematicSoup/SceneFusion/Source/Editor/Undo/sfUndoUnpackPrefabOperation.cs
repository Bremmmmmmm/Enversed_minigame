using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using KS.SceneFusion2.Client;
using KS.SceneFusion;
using KS.Reactor;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Syncs changes made by an unpack prefab undo operation.
     */
    class sfUndoUnpackPrefabOperation : sfBaseUndoOperation
    {
        private GameObject m_gameObject;

        /**
         * Game objects affected by the operation.
         */
        public override GameObject[] GameObjects
        {
            get
            {
                List<GameObject> gameObjects = new List<GameObject>();
                sfUnityUtils.ForSelfAndDesendants(m_gameObject, delegate (GameObject gameObject)
                {
                    gameObjects.Add(gameObject);
                    return true;
                });
                return gameObjects.ToArray();
            }
        }

        /**
         * Constructor
         * 
         * @param   GameObject gameObject that was unpacked.
         */
        public sfUndoUnpackPrefabOperation(GameObject gameObject)
        {
            m_gameObject = gameObject;
        }

        /**
         * Syncs changes made by undoing or redoing a prefab unpacking.
         * 
         * @param   bool isUndo - true if this is an undo operation, false if it is a redo.
         */
        public override void HandleUndoRedo(bool isUndo)
        {
            if (isUndo)
            {
                Undo();
            }
            else
            {
                Redo();
            }
        }

        /**
         * Syncs changes made by undoing the prefab unpacking.
         */
        private void Undo()
        {
            sfObject obj = sfObjectMap.Get().GetSFObject(m_gameObject);
            if (obj == null || !obj.IsSyncing)
            {
                return;
            }
            sfDictionaryProperty properties = (sfDictionaryProperty)obj.Property;
            sfBaseProperty prop;
            properties.TryGetField(sfProp.Path, out prop);
            sfGameObjectTranslator translator =
                    sfObjectEventDispatcher.Get().GetTranslator<sfGameObjectTranslator>(sfType.GameObject);
            if (obj.IsLocked)
            {
                translator.OnPrefabPathChange(m_gameObject, prop);
                translator.ApplyServerState(obj, true);
                return;
            }
            string oldPath = sfPropertyUtils.ToString(prop);
            GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(m_gameObject);
            string path = AssetDatabase.GetAssetPath(prefab);
            if (path == oldPath || (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(oldPath)))
            {
                return;
            }
            properties[sfProp.Path] = path;
            translator.SyncProperties(m_gameObject);
            sfComponentTranslator componentTranslator =
                sfObjectEventDispatcher.Get().GetTranslator<sfComponentTranslator>(sfType.Component);
            // Destroy unsynced components
            foreach (Component component in m_gameObject.GetComponents<Component>())
            {
                if (!sfObjectMap.Get().Contains(component) && componentTranslator.IsSyncable(component))
                {
                    componentTranslator.DestroyComponent(component);
                }
            }
            sfUnityUtils.ForEachDescendant(m_gameObject, (GameObject child) =>
            {
                uint[] childIndexes;
                sfUnityUtils.GetPrefabInfo(child, out path, out childIndexes);
                sfObject childObj = sfObjectMap.Get().GetSFObject(child);
                if (childObj == null || !childObj.IsSyncing)
                {
                    if (childIndexes == null && translator.IsSyncable(child))
                    {
                        translator.DestroyGameObject(child);
                    }
                    return false;
                }
                if (childIndexes == null)
                {
                    // Undoing a prefab unpacking resets the state of all descendants that aren't part of the prefab.
                    // Reset to the server state.
                    translator.ApplyServerState(childObj, true);
                    return false;
                }
                // If a missing prefab gets moved to the wrong index, delete it.
                if (child.GetComponent<sfMissingPrefab>() != null &&
                    child.transform.GetSiblingIndex() != childIndexes[childIndexes.Length - 1])
                {
                    translator.DestroyGameObject(child);
                    SceneFusion.Get().Service.Session.Delete(childObj);
                    return false;
                }
                translator.SyncHierarchyNextUpdate(childObj.Parent);
                sfDictionaryProperty props = (sfDictionaryProperty)childObj.Property;
                props[sfProp.Path] = path;
                props[sfProp.ChildIndexes] = childIndexes;
                translator.SyncProperties(child);
                // Destroy unsynced components
                foreach (Component component in child.GetComponents<Component>())
                {
                    if (!sfObjectMap.Get().Contains(component) && componentTranslator.IsSyncable(component))
                    {
                        componentTranslator.DestroyComponent(component);
                    }
                }
                return true;
            });
        }

        /**
         * Syncs changes made by redoing the prefab unpacking.
         */
        private void Redo()
        {
            sfGameObjectTranslator translator =
                    sfObjectEventDispatcher.Get().GetTranslator<sfGameObjectTranslator>(sfType.GameObject);
            translator.SyncPrefabUnpacking(m_gameObject, true);
        }
    }
}
