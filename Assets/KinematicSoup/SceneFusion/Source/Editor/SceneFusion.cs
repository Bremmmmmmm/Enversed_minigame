using System;
using UnityEngine;
using UnityEditor;
using KS.SceneFusion2.Client;
using KS.SceneFusion;
using KS.Unity.Editor;
using KS.Reactor;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Scene Fusion entry point. Does initialization and runs the update loop.
     */
    public class SceneFusion : ksSingleton<SceneFusion>
    {
        /**
         * Update delegate
         * 
         * @param   float deltaTime in seconds since the last update.
         */
        public delegate void UpdateDelegate(float deltaTime);

        /**
         * Service
         */
        public sfService Service
        {
            get { return m_service; }
        }
        private sfService m_service;

        /**
         * Invoked every update before processing server RPCs.
         */
        public event UpdateDelegate PreUpdate;

        /**
         * Invoked every update after processing server RPCs.
         */
        public event UpdateDelegate OnUpdate;

        [NonSerialized]
        private long m_lastTime;
        [NonSerialized]
        private bool m_running = false;
        [SerializeField]
        private sfSessionInfo m_reconnectInfo;
        [SerializeField]
        private string m_reconnectToken;

        /**
         * Initialization
         */
        protected override void Initialize()
        {
            m_service = new sfService();
            m_service.OnConnect += OnConnect;
            m_service.OnDisconnect += OnDisconnect;
#if MOCK_WEB_SERVICE
            m_service.WebService = new sfMockWebService();
#else
            m_service.WebService = sfWebService.Get();
#endif
            sfGuidManager.Get().RegisterEventListeners();

            sfOnlineMenu.DrawSettings = new sfOnlineMenuUI().Draw;

            // Set icon for MissingPrefab and MissingComponent scripts
            ksIconUtility util = new ksIconUtility();
            util.SetIcon<sfMissingPrefab>(sfTextures.QuestionSmall);
            util.SetIcon<sfMissingComponent>(sfTextures.QuestionSmall);
            util.CleanUp();

            sfSceneTranslator sceneTranslator = new sfSceneTranslator();
            sfObjectEventDispatcher.Get().Register(sfType.Scene, sceneTranslator);
            sfObjectEventDispatcher.Get().Register(sfType.SceneLock, sceneTranslator);
            sfObjectEventDispatcher.Get().Register(sfType.Hierarchy, sceneTranslator);

            sfLightingTranslator lightingTranslator = new sfLightingTranslator();
            sfObjectEventDispatcher.Get().Register(sfType.LightmapSettings, lightingTranslator);
            sfObjectEventDispatcher.Get().Register(sfType.RenderSettings, lightingTranslator);

            sfObjectEventDispatcher.Get().Register(sfType.GameObject, new sfGameObjectTranslator());
            sfObjectEventDispatcher.Get().Register(sfType.Component, new sfComponentTranslator());
            sfObjectEventDispatcher.Get().Register(sfType.Terrain, new sfTerrainTranslator());

            // The asset translator should be registered after all other UObject translators so other translators get a
            // chance to handle sfObjectEventDispatcher.Create events first.
            sfObjectEventDispatcher.Get().Register(sfType.Asset, new sfAssetTranslator());

            sfAvatarTranslator avatarTranslator = new sfAvatarTranslator();
            sfObjectEventDispatcher.Get().Register(sfType.Avatar, avatarTranslator);
            sfUI.Get().OnFollowUserCamera = avatarTranslator.OnFollow;
            sfUI.Get().OnGotoUserCamera = avatarTranslator.OnGoTo;
            avatarTranslator.OnUnfollow = sfUI.Get().UnfollowUserCamera;

            sfObjectEventDispatcher.Get().InitializeTranslators();

            if (m_reconnectInfo != null && m_reconnectInfo.ProjectId != -1)
            {
                // It is not safe to join a session until all ksSingletons are finished initializing, so we wait till
                // the end of the frame.
                EditorApplication.delayCall += () =>
                {
                    m_service.WebService.SFToken = m_reconnectToken;
                    m_service.JoinSession(m_reconnectInfo);
                    m_reconnectInfo = null;
                    m_reconnectToken = null;
                };
            }

            m_lastTime = DateTime.Now.Ticks;
            EditorApplication.update += Update;
        }

        /**
         * Unity on disable. Disconnects from the session.
         */
        private void OnDisable()
        {
            if (m_service != null && m_service.IsConnected)
            {
                if (EditorApplication.isUpdating)
                {
                    ksLog.Debug(this, "Disconnecting temporarily to recompile.");
                    m_reconnectInfo = m_service.SessionInfo;
                    m_reconnectToken = m_service.WebService.SFToken;
                    m_service.LeaveSession(true);
                }
                else if (!EditorApplication.isPlaying && EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    ksLog.Debug(this, "Disconnecting temporarily to enter play mode.");
                    m_reconnectInfo = m_service.SessionInfo;
                    m_reconnectToken = m_service.WebService.SFToken;
                    m_service.LeaveSession(true);
                }
                else
                {
                    m_service.LeaveSession();
                }
                Stop();
            }
        }

        /**
         * Called after connecting to a session.
         * 
         * @param   sfSession session
         * @param   string errorMessage
         */
        public void OnConnect(sfSession session, string errorMessage)
        {
            if (session == null)
            {
                ksLog.Error(this, errorMessage);
                return;
            }
            if (m_running)
            {
                return;
            }
            m_running = true;
            ksLog.Info(this, "Connected to Scene Fusion session.");
            sfLoader.Get().Initialize();
            sfHierarchyIconDrawer.Get().Start();
            sfHierarchyWatcher.Get().Start();
            PreUpdate += sfHierarchyWatcher.Get().PreUpdate;
            OnUpdate += sfHierarchyWatcher.Get().Update;
            sfSelectionWatcher.Get().Start();
            sfUndoManager.Get().Start();
            sfLockManager.Get().Start();
            sfMissingScriptSerializer.Get().Start();
            if (!EditorApplication.isPlaying)
            {
                sfObjectEventDispatcher.Get().Start(m_service.Session);
            }
        }

        /**
         * Called after disconnecting from a session.
         * 
         * @param   sfSession session
         * @param   string errorMessage
         */
        public void OnDisconnect(sfSession session, string errorMessage)
        {
            if (errorMessage != null)
            {
                ksLog.Error(this, errorMessage);
            }
            ksLog.Info(this, "Disconnected from Scene Fusion session.");
            Stop();
        }

        /**
         * Stops running Scene Fusion.
         */
        private void Stop()
        {
            if (!m_running)
            {
                return;
            }
            m_running = false;
            sfUnityEventDispatcher.Get().Disable();
            sfGuidManager.Get().SaveGuids();
            sfGuidManager.Get().Clear();
            sfObjectEventDispatcher.Get().Stop(m_service.Session);
            sfHierarchyIconDrawer.Get().Stop();
            sfHierarchyWatcher.Get().Stop();
            PreUpdate += sfHierarchyWatcher.Get().PreUpdate;
            OnUpdate += sfHierarchyWatcher.Get().Update;
            sfSelectionWatcher.Get().Stop();
            sfUndoManager.Get().Stop();
            sfLockManager.Get().Stop();
            sfMissingScriptSerializer.Get().Stop();
            sfPropertyManager.Get().CleanUp();
            sfLoader.Get().CleanUp();
            sfObjectMap.Get().Clear();
            sfNotificationManager.Get().Clear();
        }

        /**
         * Called every frame.
         */
        private void Update()
        {
            // We can't access the config from intialize as detecting the scene fusion root isn't possible yet, so
            // we do it here.
            m_service.WebService.LatestVersion = sfConfig.Get().Version;

            // Time.deltaTime is not accurate in the editor so we track it ourselves.
            long ticks = DateTime.Now.Ticks;
            float dt = (ticks - m_lastTime) / (float)TimeSpan.TicksPerSecond;
            m_lastTime = ticks;

            // Start the object event dispatcher when we leave play mode.
            if (!sfObjectEventDispatcher.Get().IsActive && m_running && m_service.Session != null && !EditorApplication.isPlaying)
            {
                sfObjectEventDispatcher.Get().Start(m_service.Session);
                // Create all the objects
                foreach (sfObject obj in m_service.Session.GetRootObjects())
                {
                    sfObjectEventDispatcher.Get().OnCreate(obj, -1);
                }
            }

            if (m_running && m_service.Session != null && !EditorApplication.isPlaying)
            {
                // Disable Unity events while SF is changing the scene
                sfUnityEventDispatcher.Get().Disable();
            }
            if (PreUpdate != null)
            {
                PreUpdate(dt);
            }
            m_service.Update(dt);
            if (OnUpdate != null)
            {
                OnUpdate(dt);
            }
            if (m_running && m_service.Session != null && !EditorApplication.isPlaying)
            {
                // Reenable Unity events when SF is done changing the scene
                sfUnityEventDispatcher.Get().Enable();
            }

            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                sfCameraManager.Get().LastSceneCamera = view.camera;
            }

            if (Application.isPlaying && Camera.allCamerasCount > 0)
            {
                sfCameraManager.Get().LastGameCamera = Camera.allCameras[0];
            }
        }
    }
}
