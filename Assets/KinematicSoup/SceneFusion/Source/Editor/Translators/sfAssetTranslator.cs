using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using KS.SceneFusion2.Client;
using KS.SceneFusion;
using KS.Reactor;
using KS.Unity.Editor;
using UObject = UnityEngine.Object;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Manages syncing of assets. Assets are synced when they are referenced if they have a generator in sfLoader.
     * The uploader computes a checksum for the asset. Other users compute their own checksum from their values and the
     * server values. If their checksum does not match the one the asset was uploaded with or the one computed from the
     * server values, the asset will not sync for that user.
     */
    public class sfAssetTranslator : sfBaseUObjectTranslator
    {
        private HashSet<UObject> m_conflictingAssets = new HashSet<UObject>();
        private HashSet<UObject> m_lockedAssets = new HashSet<UObject>();

        /**
         * Initialization
         */
        public override void Initialize()
        {
            PostUObjectChange.Add<TerrainLayer>((UObject uobj) => sfUI.Get().MarkSceneViewStale());
        }

        /**
         * Called after disconnecting from a session.
         */
        public override void OnSessionDisconnect()
        {
            // Unlock all assets
            foreach (UObject asset in m_lockedAssets)
            {
                asset.hideFlags &= ~HideFlags.NotEditable;
                sfUI.Get().MarkInspectorStale(asset);
            }
            m_lockedAssets.Clear();
            m_conflictingAssets.Clear();
        }

        /**
         * Creates an sfObject for a uobject if the object is a createable asset type.
         *
         * @param   UObject uobj to create sfObject for.
         * @param   sfObject outObj created for the uobject.
         * @return  bool true if the uobject was handled by this translator.
         */
        public override bool TryCreate(UObject uobj, out sfObject outObj)
        {
            if (!sfLoader.Get().IsAsset(uobj) || !sfLoader.Get().IsCreatableAssetType(uobj))
            {
                outObj = null;
                return false;
            }
            if (m_conflictingAssets.Contains(uobj))
            {
                outObj = null;
                return true;
            }
            outObj = CreateObject(uobj, sfType.Asset);
            if (outObj == null)
            {
                return true;
            }
            if (Selection.Contains(uobj))
            {
                outObj.RequestLock();
            }
            sfDictionaryProperty dict = (sfDictionaryProperty)outObj.Property;
            dict[sfProp.Path] = sfPropertyUtils.FromString(sfLoader.Get().GetAssetPath(uobj));
            dict[sfProp.CheckSum] = Checksum(dict);

            SceneFusion.Get().Service.Session.Create(outObj);
            return true;
        }

        /**
         * Called when an object is created by another user.
         *
         * @param   sfObject obj that was created.
         * @param   int childIndex of new object. -1 if object is a root.
         */
        public override void OnCreate(sfObject obj, int childIndex)
        {
            sfDictionaryProperty properties = (sfDictionaryProperty)obj.Property;
            string path = sfPropertyUtils.ToString(properties[sfProp.Path]);
            UObject asset = sfLoader.Get().Load(path);
            if (asset == null)
            {
                return;
            }

            sfObject current = sfObjectMap.Get().GetSFObject(asset);
            if (current != null)
            {
                // The asset was created twice, which can happen if two users try to create the asset at the same time. Keep
                // the version that was created first and delete the second one.
                ksLog.Warning(this, "Asset '" + path + "' was uploaded by multiple users. The second version will " +
                    "be ignored.");
                if (current.IsCreated)
                {
                    SceneFusion.Get().Service.Session.Delete(obj);
                    return;
                }
                SceneFusion.Get().Service.Session.Delete(current);
                sfObjectMap.Get().Remove(current);
            }

            // If the asset was created on load, this user did not have the asset so we don't have to check for conflicts.
            if (!sfLoader.Get().WasCreatedOnLoad(asset))
            {
                sfDictionaryProperty dict = new sfDictionaryProperty();
                sfPropertyManager.Get().CreateProperties(asset, dict);
                ulong checksum = Checksum(dict);
                // If our checksum does not match either the initial checksum the asset was uploaded with, or the checksum
                // computed from the the current asset server values, the asset is conflicting and we won't sync it.
                if (checksum != ((sfValueProperty)properties[sfProp.CheckSum]).Value &&
                    checksum != Checksum(properties))
                {
                    string message = "Your asset '" + path + "' conflicts with the server version and will not sync.";
                    sfNotification.Create(sfNotificationCategory.AssetConflict, message, asset);
                    m_conflictingAssets.Add(asset);
                    return;
                }
            }

            sfObjectMap.Get().Add(obj, asset);
            sfPropertyManager.Get().ApplyProperties(asset, properties);

            if (obj.IsLocked)
            {
                OnLock(obj);
            }
        }

        /**
         * Called when an object is locked by another user.
         * 
         * @param   sfObject obj that was locked.
         */
        public override void OnLock(sfObject obj)
        {
            UObject asset = sfObjectMap.Get().GetUObject(obj);
            if (asset != null)
            {
                m_lockedAssets.Add(asset);
                asset.hideFlags |= HideFlags.NotEditable;
                sfUI.Get().MarkInspectorStale(asset);
            }
        }

        /**
         * Called when an object is unlocked by another user.
         * 
         * @param   sfObject obj that was unlocked.
         */
        public override void OnUnlock(sfObject obj)
        {
            UObject asset = sfObjectMap.Get().GetUObject(obj);
            if (asset != null)
            {
                m_lockedAssets.Remove(asset);
                asset.hideFlags &= ~HideFlags.NotEditable;
                sfUI.Get().MarkInspectorStale(asset);
            }
        }

        /**
         * Computes a checksum from a dictionary property.
         * 
         * @param   sfDictionaryProperty dict to compute checksum from.
         * @return  ulong checksum
         */
        private ulong Checksum(sfDictionaryProperty dict)
        {
            return sfChecksum.Fletcher64(dict, (string name) =>
            {
                // Don't include our custom properties beginning with # in the checksum
                return !name.StartsWith("#");
            });
        }
    }
}
