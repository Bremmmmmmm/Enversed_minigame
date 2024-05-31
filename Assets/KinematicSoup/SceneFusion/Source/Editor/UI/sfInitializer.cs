using UnityEngine;
using UnityEditor;
using KS.SceneFusion;
using KS.Unity.Editor;
using KS.Reactor;
using KS.SceneFusionCommon;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Performs initialization logic when the editor loads.
     */
    [InitializeOnLoad]
    class sfInitializer
    {
        public const string MENU_NAME = Product.NAME + "/";

        /**
         * Static constructor
         */
        static sfInitializer()
        {
            EditorApplication.update += Init;
        }

        /**
         * Performs initialization logic that must wait until after Unity derserialization finishes.
         */
        private static void Init()
        {
            EditorApplication.update -= Init;

            if (sfConfig.Get().ShowGettingStartedScreen ||
                sfConfig.Get().Version != sfConfig.Get().LastVersion)
            {
                SerializedObject config = new SerializedObject(sfConfig.Get());
                SerializedProperty lastVersion = config.FindProperty("LastVersion");
                lastVersion.stringValue = sfConfig.Get().Version;
                sfPropertyUtils.ApplyProperties(config);
            }

            sfIService service = SceneFusion.Get().Service;
            sfLoginMenu.Service = service;
            sfSessionsMenu.Service = service;
            sfOnlineMenu.Service = service;
            sfFeedback.Service = service;
            sfFeedbackMenu.Service = service;
            sfNotificationWindow.Service = service;

            ksWindow window = ksWindow.Find(ksWindow.SCENE_FUSION_MAIN);
            if (window != null)
            {
                if (window.Menu == null)
                {
                    window.Menu = ScriptableObject.CreateInstance<sfSessionsMenu>();
                }
            }
#if SCENE_FUSION_LAN
            window = Window.Find(Window.SCENE_FUSION_LAN);
            if (window != null)
            {
                window.Menu = LoginMenu.Instance;
                window.Repaint();
            }
#endif
        }

        /**
         * Opens the sessions menu.
         */
        [MenuItem(MENU_NAME + "Sessions", priority = MenuGroup.UNITY_WINDOW)]
        private static void OpenMenu()
        {
            ksWindow.Open(ksWindow.SCENE_FUSION_MAIN, delegate (ksWindow window)
            {
                window.titleContent = new GUIContent(" Sessions", KS.SceneFusion.sfTextures.Logo);
                window.minSize = new Vector2(380f, 100f);
                window.Menu = ScriptableObject.CreateInstance<sfSessionsMenu>();
            });
        }

        [MenuItem(MENU_NAME + "Notifications", priority = MenuGroup.UNITY_WINDOW)]
        private static void OpenNotifications()
        {
            sfNotificationWindow.Open();
        }

        /**
         * Opens Scene Fusion settings in the inspector
         */
        [MenuItem(MENU_NAME + "Settings", priority = MenuGroup.EXTERNAL_WINDOW)]
        private static void Settings()
        {
            Selection.objects = new Object[] { sfConfig.Get() };
            ksEditorUtils.FocusInspectorWindow();
        }

        /**
         * Open web console login page.
         */
        [MenuItem(MENU_NAME + "Web Console", priority = MenuGroup.EXTERNAL_WINDOW)]
        private static void ConsoleLink()
        {
            string consoleUrl = sfConfig.Get().Urls.WebConsole;
            if (consoleUrl != null)
            {
                Application.OpenURL(consoleUrl);
            }
        }

        /**
         * Open Scene Fusion online documentation
         */
        [MenuItem(MENU_NAME + "Documentation", priority = MenuGroup.EXTERNAL_WINDOW)]
        private static void DocLink()
        {
            string docUrl = sfConfig.Get().Urls.Documentation;
            if (docUrl != null)
            {
                Application.OpenURL(docUrl);
            }
        }

        /**
         * Send email to support email address.
         */
        [MenuItem(MENU_NAME + "Email Support", priority = MenuGroup.EXTERNAL_WINDOW)]
        private static void SupportLink()
        {
            string email = sfConfig.Get().Urls.SupportEmail;
            if (email != null)
            {
                Application.OpenURL("mailto:" + email);
            }
        }

        /**
         * Check to see if feedback menu has question
         * @return boolean indicating that instance has a token and a feedback question
         */
        [MenuItem(MENU_NAME + "Feedback", true)]
        static bool CheckFeedbackMenuItem()
        {
            return sfFeedback.Instance.GetLastQuestion() != null;
        }

        /**
         * Show a scene fusion feedback screen
         */
        [MenuItem(MENU_NAME + "Feedback", priority = MenuGroup.EXTERNAL_WINDOW)]
        private static void ShowFeedback()
        {
            sfFeedbackMenu.Get().Question = sfFeedback.Instance.GetLastQuestion();
            ksWindow.Open(
                ksWindow.SCENE_FUSION_FEEDBACK,
                delegate (ksWindow window)
                {
                    window.titleContent = new GUIContent(Product.NAME + "Feedback");
                    window.minSize = new Vector2(800f, 210f);
                    window.maxSize = new Vector2(800f, 210f);
                    window.Menu = sfFeedbackMenu.Get();
                },
                ksWindow.WindowStyle.UTILITY
            );
        }
    }
}