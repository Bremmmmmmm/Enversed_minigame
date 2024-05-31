using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.AI;
using UnityEngine.Video;
using UnityEngine.U2D;
using UnityEditor;
using UnityEditor.Animations;
using KS.Reactor;
using KS.Unity.Editor;
using KS.SceneFusion;
using UObject = UnityEngine.Object;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Loads and caches assets.
     */
    internal class sfLoader
    {
        /**
         * Singleton instance
         */
        public static sfLoader Get()
        {
            return m_instance;
        }
        private static sfLoader m_instance = new sfLoader();

        /**
         * Generator for an asset.
         * 
         * @return  UObject generated asset.
         */
        private delegate UObject Generator();
        
        private Dictionary<string, UObject> m_cache = new Dictionary<string, UObject>();
        private Dictionary<UObject, string> m_pathCache = new Dictionary<UObject, string>();
        private Dictionary<Type, Generator> m_generators = new Dictionary<Type, Generator>();
        private Dictionary<Type, Generator> m_standInGenerators = new Dictionary<Type, Generator>();
        private Dictionary<Type, UObject> m_standIns = new Dictionary<Type, UObject>();
        private HashSet<UObject> m_standInInstances = new HashSet<UObject>();
        private HashSet<UObject> m_createdAssets = new HashSet<UObject>();

        /**
         * Singleton constructor
         */
        public sfLoader()
        {
            m_generators[typeof(TerrainData)] = () => new TerrainData();
            m_generators[typeof(TerrainLayer)] = () => new TerrainLayer();
#if UNITY_2020_3_OR_NEWER
            m_generators[typeof(LightingSettings)] = () => new LightingSettings();
#endif

            m_standInGenerators[typeof(Font)] = () => new Font();
            m_standInGenerators[typeof(ShaderVariantCollection)] = () => new ShaderVariantCollection();
            m_standInGenerators[typeof(Cubemap)] = () => new Cubemap(1, TextureFormat.Alpha8, false);
            m_standInGenerators[typeof(Texture3D)] = () => new Texture3D(1, 1, 1, TextureFormat.Alpha8, false);
            m_standInGenerators[typeof(Texture2DArray)] = () =>
                new Texture2DArray(1, 1, 1, TextureFormat.Alpha8, false);
            m_standInGenerators[typeof(SparseTexture)] = () => new SparseTexture(1, 1, TextureFormat.Alpha8, 1);
            m_standInGenerators[typeof(WebCamTexture)] = () => new WebCamTexture();
            m_standInGenerators[typeof(AnimationClip)] = () => new AnimationClip();
            m_standInGenerators[typeof(Motion)] = () => new AnimationClip();
            
            m_standInGenerators[typeof(VideoClip)] = delegate ()
            {
                string path = sfPaths.FusionRoot + "Stand-Ins/BlackFrame";
                string oldPath = path + ".bin";
                string newPath = path + ".avi";
                if (RenameFile(oldPath, newPath) && File.Exists(newPath))
                {
                    VideoClip asset = AssetDatabase.LoadAssetAtPath<VideoClip>(newPath);
                    if (asset == null)
                    {
                        ksLog.Error(this, "Unable to load VideoClip '" + newPath + "'.");
                    }
                    else
                    {
                        m_standIns[typeof(VideoClip)] = asset;
                        return UObject.Instantiate(asset);
                    }
                }
                return null;
            };

            m_standInGenerators[typeof(AvatarMask)] = () => new AvatarMask();
            m_standInGenerators[typeof(NavMeshData)] = () => new NavMeshData();
            
            m_standInGenerators[typeof(CustomRenderTexture)] = () => new CustomRenderTexture(1, 1);
        }

        /**
         * Renames a file and deletes the meta file for it. Returns true if the file was successfully renamed.
         * 
         * @param   string oldPath
         * @param   string newPath
         * @return  bool
         */
        private bool RenameFile(string oldPath, string newPath)
        {
            if (File.Exists(oldPath) && !File.Exists(newPath))
            {
                try
                {
                    File.Move(oldPath, newPath);
                }
                catch (Exception e)
                {
                    ksLog.Error(this, "Error moving '" + oldPath + "' to '" + newPath + "'.", e);
                    return false;
                }
                string oldMetaFilePath = oldPath + ".meta";
                try
                {
                    if (File.Exists(oldMetaFilePath))
                    {
                        File.Delete(oldMetaFilePath);
                    }
                }
                catch (Exception e)
                {
                    ksLog.Error(this, "Error deleting '" + oldMetaFilePath + "'.", e);
                }
                AssetDatabase.Refresh();
                return true;
            }
            return false;
        }

        /**
         * Loads built-in and stand-in assets.
         */
        public void Initialize()
        {
            LoadBuiltInAssets();
            if (m_standIns.Count == 0)
            {
                LoadStandInAssets();
            }
            ksEditorEvents.OnNewAssets += OnNewAssets;
        }

        /**
         * Destroys stand-in instances and clears the cache.
         */
        public void CleanUp()
        {
            foreach (UObject standIn in m_standInInstances)
            {
                UObject.DestroyImmediate(standIn);
            }
            m_createdAssets.Clear();
            m_standInInstances.Clear();
            m_cache.Clear();
            m_pathCache.Clear();
            ksEditorEvents.OnNewAssets -= OnNewAssets;
        }

        /**
         * Checks if we can create a stand-in of the given type.
         * 
         * @param   Type type to check.
         * @return  bool true if we can create a stand-in for the type.
         */
        public bool CanCreateStandIn(Type type)
        {
            return m_standIns.ContainsKey(type) || m_standInGenerators.ContainsKey(type) ||
                m_generators.ContainsKey(type) || typeof(ScriptableObject).IsAssignableFrom(type);
        }

        /**
         * Checks if a Unity object is an asset or asset stand-in.
         * 
         * @param   UObject obj to check.
         * @return  bool true if the object is an asset or asset stand-in.
         */
        public bool IsAsset(UObject obj)
        {
            if (obj == null)
            {
                return false;
            }
            string path;
            if (!m_pathCache.TryGetValue(obj, out path))
            {
                path = AssetDatabase.GetAssetPath(obj);
            }
            return path != "";
        }

        /**
         * Checks if an object is an asset stand-in.
         * 
         * @param   UObject obj to check.
         * @return  bool true if the object is an asset stand-in.
         */
        public bool IsStandIn(UObject obj)
        {
            return obj != null && m_standInInstances.Contains(obj);
        }

        /**
         * Was this asset created when we tried to load it?
         * 
         * @param   UObject obj to check.
         * @return  bool true if the object was created on load.
         */
        public bool WasCreatedOnLoad(UObject obj)
        {
            return obj != null && m_createdAssets.Contains(obj);
        }

        /**
         * Is this object a creatable asset type? These assets are created if they are not found during loading.
         * 
         * @param   UObject obj to check.
         * @return  bool true if the object is a creatable asset type.
         */
        public bool IsCreatableAssetType(UObject obj)
        {
            return obj != null && m_generators.ContainsKey(obj.GetType());
        }

        /**
         * Gets the path for an asset used to load the object from the asset cache.
         * 
         * @param   UObject asset to get path for.
         * @param   GameObject gameObject that referenced the asset. Used to determine if the warning for scene assets
         *          should be suppressed.
         * @return  string asset path.
         */
        public string GetAssetPath(UObject asset, GameObject gameObject = null)
        {
            if (asset == null)
            {
                return "";
            }
            string path;
            if (!m_pathCache.TryGetValue(asset, out path))
            {
                path = AssetDatabase.GetAssetPath(asset);
                if (path == "")
                {
                    ksLog.Warning(this, "Cannot sync '" + asset.name + "' (" + asset.GetType().Name +
                        ") because it is not in Unity's asset database.", gameObject);
                    m_pathCache[asset] = "";
                    return "";
                }
                else if (path == "Resources/unity_builtin_extra" || path == "Library/unity default resources")
                {
                    path = asset.GetType() + "/" + asset.name;
                }
                else
                {
                    path = CacheAssets(path, asset);
                }
            }
            return path;
        }

        /**
         * Loads an asset of type T. Tries first to load from the cache, and if it's not found, caches it.
         * 
         * @param   string path of asset to load.
         * @return  T asset, or null if the asset was not found.
         */
        public T Load<T>(string path) where T : UObject
        {
            return Load(path) as T;
        }

        /**
         * Loads an asset. Tries first to load from the cache, and if it's not found, caches it.
         * 
         * @param   string path of asset to load.
         * @return  UObject asset, or null if the asset was not found.
         */
        public UObject Load(string path)
        {
            if (path == null || path.Length == 0)
            {
                return null;
            }
            UObject asset;
            if (!m_cache.TryGetValue(path, out asset) || asset.IsDestroyed())
            {
                int index = path.IndexOf("/");
                Type type = null;
                string assetPath;
                if (index >= 0)
                {
                    string typeName = path.Substring(0, index);
                    assetPath = path.Substring(index + 1);
                    type = sfTypeCache.Get().Load(typeName);
                }
                else
                {
                    assetPath = path;
                }
                if (type == null)
                {
                    type = typeof(UObject);
                    ksLog.Warning(this, "Cannot determine type of asset " + path + ". Trying " + type);
                }
                int assetIndex = 0;
                index = assetPath.IndexOf("//");
                if (index >= 0)
                {
                    string indexStr = assetPath.Substring(index + 2);
                    assetPath = assetPath.Substring(0, index);
                    if (!int.TryParse(indexStr, out assetIndex))
                    {
                        ksLog.Warning(this, "Invalid asset index '" + indexStr + "'.");
                    }
                }
                asset = CacheAssets(assetPath, assetIndex);

                if (asset == null)
                {
                    Generator generator;
                    if (assetIndex == 0 && m_generators.TryGetValue(type, out generator))
                    {
                        ksLog.Info(this, "Generating " + type + " '" + assetPath + "'.");
                        asset = generator();
                        if (asset != null && asset.GetType() == type)
                        {
                            ksPathUtils.Create(sfPaths.ProjectRoot + assetPath);
                            AssetDatabase.CreateAsset(asset, assetPath);
                            m_pathCache[asset] = path;
                            m_cache[path] = asset;
                            m_createdAssets.Add(asset);
                        }
                        else
                        {
                            ksLog.Warning(this, "Could not generate " + type + ".");
                        }
                    }
                    else
                    {
                        string message = "Unable to load " + type.Name + " '" + assetPath + "'";
                        if (assetIndex != 0)
                        {
                            message += " at index " + assetIndex;
                        }
                        message += ".";
                        sfNotification.Create(sfNotificationCategory.MissingAsset, message);
                    }
                }

                if (asset != null && asset.GetType() != type)
                {
                    string message = "Expected asset at '" + assetPath + "' index " + assetIndex + " to be type " + type +
                        " but found " + asset.GetType();
                    sfNotification.Create(sfNotificationCategory.MissingAsset, message);
                    asset = null;
                }

                if (asset == null)
                {
                    Generator generator;
                    if (m_standIns.TryGetValue(type, out asset))
                    {
                        asset = UObject.Instantiate(asset);
                    }
                    else if (m_standInGenerators.TryGetValue(type, out generator))
                    {
                        asset = generator();
                    }
                    else if (typeof(ScriptableObject).IsAssignableFrom(type))
                    {
                        asset = ScriptableObject.CreateInstance(type);
                    }
                    if (asset != null)
                    {
                        m_pathCache[asset] = path;
                        asset.name = "Missing " + type.Name + " (" + assetPath + ")";
                        if (assetIndex != 0)
                        {
                            asset.name += "[" + assetIndex + "]";
                        }
                        asset.hideFlags = HideFlags.HideAndDontSave;
                        m_standInInstances.Add(asset);
                    }
                    else
                    {
                        ksLog.Warning(this, "Could not create " + type + " stand-in.");
                    }
                    m_cache[path] = asset;
                }
            }
            return asset;
        }

        /**
         * Caches all assets at the given path. Replaces missing references to the cached assets.
         * 
         * @param   string path of assets to cache.
         */
        private void CacheAssets(string path)
        {
            string assetPath = "";
            UObject asset = null;
            CacheAssets(path, -1, ref asset, ref assetPath);
        }

        /**
         * Caches all assets at the given path. Replaces missing references to the cached assets. Returns the sub-asset
         * path for the given asset.
         * 
         * @param   string path of assets to cache.
         * @param   UObject asset to get sub-asset path for.
         * @return  string sub asset path for the asset, or empty string if the asset wasn't found at the path.
         */
        private string CacheAssets(string path, UObject asset)
        {
            string assetPath = "";
            CacheAssets(path, -1, ref asset, ref assetPath);
            if (assetPath == "")
            {
                ksLog.Warning(this, "Cannot sync '" + asset.name + "' (" + asset.GetType().Name +
                    ") Because it was not found at its asset path '" + path + "'.");
                m_pathCache[asset] = "";
            }
            return assetPath;
        }

        /**
         * Caches all assets at the given path. Replaces missing references to the cached assets. Returns the asset at
         * the given index.
         * 
         * @param   string path of assets to cache.
         * @param   int index of sub-asset to get.
         * @return  UObject asset at the given index, or null if none was found.
         */
        private UObject CacheAssets(string path, int index)
        {
            UObject asset = null;
            CacheAssets(path, index, ref asset, ref path);
            return asset;
        }

        /**
         * Caches all assets at the given path. Replaces missing references to the cached assets. Optionally retrieves
         * a sub-asset by index or a sub-asset path for an asset.
         * 
         * @param   string path of assets to cache.
         * @param   int index of sub-asset to retrieve. Pass negative number to not retrieve a sub asset.
         * @param   ref UObject asset - set to sub-asset at the given index if one is found. Otherwise
         *          retrieves the sub-asset path for this asset.
         * @param   ref string assetPath - set to sub-asset path for asset if asset is not null.
         */
        private void CacheAssets(string path, int index, ref UObject asset, ref string assetPath)
        {
            // Load all assets if this is not a scene asset (loading all assets from a scene asset causes an error)
            UObject[] assets = null;
            if (!path.EndsWith(".unity"))
            {
                assets = AssetDatabase.LoadAllAssetsAtPath(path);
            }
            if (assets == null || assets.Length == 0)
            {
                // Some assets (like folders) will return 0 results if you use LoadAllAssetsAtPath, but can be loaded
                // using LoadAssetAtPath.
                assets = new UObject[] { AssetDatabase.LoadAssetAtPath<UObject>(path) };
                if (assets[0] == null)
                {
                    return;
                }
            }
            else if (assets.Length > 1)
            {
                // Sub-asset order is not guaranteed so we sort based on type and name. This may fail if two sub-assets
                // have the exact same type and name...
                assets = new AssetSorter().Sort(assets, AssetDatabase.LoadAssetAtPath<UObject>(path));
            }
            for (int i = 0; i < assets.Length; i++)
            {
                UObject obj = assets[i];
                if (obj == null)
                {
                    continue;
                }
                string subAssetPath = obj.GetType() + "/" + path;
                if (i > 0)
                {
                    subAssetPath += "//" + i;
                }

                m_pathCache[obj] = subAssetPath;
                m_cache[subAssetPath] = obj;
                if (index == i)
                {
                    asset = obj;
                }
                else if (asset == obj)
                {
                    assetPath = subAssetPath;
                }
            }
        }

        /**
         * Loads built-in assets into the cache. Built-in assets cannot be loaded programmatically, so we assign
         * references to them to a scriptable object in the editor, and load the scriptable object to get asset
         * references.
         */
        private void LoadBuiltInAssets()
        {
            CacheBuiltIns(new ksIconUtility().GetBuiltInIcons());

            sfBuiltInAssetsLoader loader = sfBuiltInAssetsLoader.Get();
            CacheBuiltIns(loader.LoadBuiltInAssets<Material>());
            CacheBuiltIns(loader.LoadBuiltInAssets<Texture2D>());
            CacheBuiltIns(loader.LoadBuiltInAssets<Sprite>());
            CacheBuiltIns(loader.LoadBuiltInAssets<LightmapParameters>());
            CacheBuiltIns(loader.LoadBuiltInAssets<Mesh>());
            CacheBuiltIns(loader.LoadBuiltInAssets<Font>());
            CacheBuiltIns(loader.LoadBuiltInAssets<Shader>());
        }

        /**
         * Loads stand-in assets. When an asset is missing, we use a stand-in asset of the same type to represent it.
         * Stand-in assets are assigned to a scriptable object in the editor, and we load the scriptable object get
         * get references to the stand-in assets.
         */
        private void LoadStandInAssets()
        {
            CacheStandInFromPath<Material>(sfPaths.FusionRoot + "Stand-Ins/Material.mat", "Material");
            CacheStandInFromPath<Texture2D>(sfPaths.FusionRoot + "Textures/QuestionSmall.png", "QuestionSmall");
            CacheStandInFromPath<Texture>(sfPaths.FusionRoot + "Textures/QuestionSmall.png", "QuestionSmall");
            CacheStandInFromPath<Sprite>(sfPaths.FusionRoot + "Textures/QuestionSmall.png", "QuestionSmall");
            CacheStandInFromBuiltIn<LightmapParameters>("Default-HighResolution");
            CacheStandInFromPath<Flare>(sfPaths.FusionRoot + "Stand-Ins/Flare.flare", "Flare");
            CacheStandInFromBuiltIn<Mesh>("Cube");
            CacheStandInFromPath<PhysicMaterial>(sfPaths.FusionRoot + "Stand-Ins/Physic Material.physicMaterial",
                "Physic Material");
            CacheStandInFromPath<PhysicsMaterial2D>(sfPaths.FusionRoot +
                "Stand-Ins/Physics2D Material.physicsMaterial2D", "Physics2D Material");
            CacheStandInFromPath<RenderTexture>(sfPaths.FusionRoot + "Stand-Ins/Render Texture.renderTexture",
                "Render Texture");
            CacheStandInFromPath<AudioMixer>(sfPaths.FusionRoot + "Stand-Ins/Audio Mixer.mixer", "Audio Mixer");
            CacheStandInFromPath<AudioMixerGroup>(sfPaths.FusionRoot + "Stand-Ins/Audio Mixer.mixer", "Master");
            CacheStandInFromPath<AudioMixerSnapshot>(sfPaths.FusionRoot + "Stand-Ins/Audio Mixer.mixer", "Snapshot");
            CacheStandInFromPath<AudioClip>(sfPaths.FusionRoot + "Stand-Ins/AudioClip.wav", "AudioClip");
            CacheStandInFromPath<AnimatorController>(sfPaths.FusionRoot + "Stand-Ins/Animator Controller.controller",
                "Animator Controller");
            CacheStandInFromPath<RuntimeAnimatorController>(sfPaths.FusionRoot + "Stand-Ins/Animator Controller.controller",
                "Animator Controller");
            CacheStandInFromPath<AnimatorOverrideController>(
                sfPaths.FusionRoot + "Stand-Ins/Animator Override Controller.overrideController",
                "Animator Override Controller");
            CacheStandInFromPath<GUISkin>(sfPaths.FusionRoot + "Stand-Ins/GUI Skin.guiskin", "GUI Skin");
            CacheStandInFromPath<Avatar>(sfPaths.FusionRoot + "Stand-Ins/StandIn.fbx", "StandInAvatar");
            CacheStandInFromPath<UObject>(sfPaths.FusionRoot + "Textures/QuestionSmall.png", "QuestionSmall");
            CacheStandInFromPath<TextAsset>(sfPaths.FusionRoot + "Stand-Ins/TextAsset.txt", "TextAsset");
            CacheStandInFromPath<GameObject>(sfPaths.FusionRoot + "Stand-Ins/GameObject.prefab", "GameObject");
            CacheStandInFromPath<SpriteAtlas>(sfPaths.FusionRoot + "Stand-Ins/Sprite Atlas.spriteatlas", "Sprite Atlas");
            CacheStandInFromPath<MonoScript>(sfPaths.FusionRoot + "FusionRoot.cs", "FusionRoot");
            CacheStandInFromPath<Shader>(sfPaths.FusionRoot + "Shaders/Missing.shader", "KS/Missing Shader");
            CacheStandInFromPath<ComputeShader>(sfPaths.FusionRoot + "Stand-Ins/DoNothing.compute", "DoNothing");
            CacheStandInFromPath<BillboardAsset>(sfPaths.FusionRoot + "Stand-Ins/Billboard.asset", "Billboard");
            CacheStandInFromPath<LightingDataAsset>(sfPaths.FusionRoot + "Stand-Ins/LightingData.asset", "LightingData");
            // AssetDatabase.LoadAllAssetsAtPath (called from CacheStandInFromPath) cannot be used with scene assets so
            // we use LoadAssetAtPath.
            RegisterStandIn(AssetDatabase.LoadAssetAtPath<SceneAsset>(sfPaths.FusionRoot + "Stand-Ins/Scene.unity"));
            RegisterStandIn(AssetDatabase.LoadAssetAtPath<DefaultAsset>(sfPaths.FusionRoot + "Stand-Ins"));
        }

        /**
         * Attempt to cache a stand-in asset found at a known path location
         * 
         * @param   string assetPath
         * @param   string assetName
         */
        private void CacheStandInFromPath<T>(string assetPath, string assetName) where T : UObject
        {
            UObject[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            foreach (UObject asset in assets)
            {
                if (typeof(T).IsAssignableFrom(asset.GetType()))
                {
                    if (asset.name == assetName)
                    {
                        RegisterStandIn<T>(asset as T);
                        return;
                    }
                }
            }

            ksLog.Warning("Unable to cache asset " + assetPath + "(" + assetName + ") of type " + typeof(T).ToString());
        }

        /**
         * Attempt to cache a stand-in asset found in the built-in assets
         * 
         * @param   string assetName
         */
        private void CacheStandInFromBuiltIn<T>(string assetName) where T : UObject
        {
            sfBuiltInAssetsLoader loader = sfBuiltInAssetsLoader.Get();
            T[] assets = loader.LoadBuiltInAssets<T>();

            foreach (T asset in assets)
            {
                if (asset.name == assetName)
                {
                    RegisterStandIn<T>(asset);
                    return;
                }
            }

            ksLog.Warning("Unable to cache built-in asset " + assetName + " of type " + typeof(T).ToString());
        }

        /**
         * Registers a stand in asset to be used when assets of type T are missing.
         * 
         * @param   T asset
         */
        private void RegisterStandIn<T>(T asset) where T : UObject
        {
            if (asset != null)
            {
                m_standIns[typeof(T)] = asset;
            }
        }

        /**
         * Adds built-in assets to the cache.
         * 
         * @param   T[] assets to cache.
         */
        private void CacheBuiltIns<T>(T[] assets) where T : UObject
        {
            string prefix = typeof(T).ToString() + "/";
            foreach (T asset in assets)
            {
                if (asset != null)
                {
                    string path = prefix + asset.name;
                    m_cache[path] = asset;
                    m_pathCache[asset] = path;
                }
            }
        }

        /**
         * Checks if an asset is destroyed.
         * 
         * @param   UObject asset to check.
         * @return  bool true if the asset is destroyed.
         */
        private bool IsDestroyed(UObject asset)
        {
            if (asset != null)
            {
                return false;
            }
            try
            {
                // Unity overloads the == operator to pretend objects are null when they're destroyed, but they aren't
                // really null and you can still call some functions like GetHashCode() on them. This is how we check
                // if the object really is null.
                asset.GetHashCode();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /**
         * Called when new assets are created. Replaces missing asset references with the new assets.
         * 
         * @param   string[] paths to new assets.
         */
        private void OnNewAssets(string[] paths)
        {
            foreach (string path in paths)
            {
                CacheAssets(path);
            }
        }
    }
}
