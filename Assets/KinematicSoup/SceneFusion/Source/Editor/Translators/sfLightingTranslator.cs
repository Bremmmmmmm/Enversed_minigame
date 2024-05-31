using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using KS.SceneFusion2.Client;
using KS.SceneFusion;
using KS.Reactor;
using KS.Unity.Editor;
using UObject = UnityEngine.Object;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Manages syncing of lighting properties.
     */
    public class sfLightingTranslator : sfBaseUObjectTranslator
    {
        private EditorWindow m_lightingWindow;

        /**
         * Initialization
         */
        public override void Initialize()
        {
            // Do not sync the 'Auto Generate' lighting setting.
#if UNITY_2020_3_OR_NEWER
            sfPropertyManager.Get().Blacklist.Add<LightingSettings>("m_GIWorkflowMode");
#else
            sfPropertyManager.Get().Blacklist.Add<UObject>("m_GIWorkflowMode");
#endif

            PostUObjectChange.Add<UObject>((UObject uobj) => RefreshWindow());
#if UNITY_2020_3_OR_NEWER
            sfAssetTranslator translator = sfObjectEventDispatcher.Get().GetTranslator<sfAssetTranslator>(sfType.Asset);
            translator.PostUObjectChange.Add<LightingSettings>((UObject uobj) => RefreshWindow());
#endif
        }

        /**
         * Called after connecting to a session.
         */
        public override void OnSessionConnect()
        {
            base.OnSessionConnect();
        }

        /**
         * Called after disconnecting from a session.
         */
        public override void OnSessionDisconnect()
        {
            base.OnSessionDisconnect();
        }

        /**
         * Creates sfObjects for a scene's LightingSettings and RenderSettings as child objects of the scene object.
         * 
         * @param   Scene scene to create lighting objects for.
         * @param   sfObject sceneObject to make the lighting objects children of.
         */
        public void CreateLightingObjects(Scene scene, sfObject sceneObj)
        {
            // We can only get the lighting objects for this scene when it is the active scene.
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene != scene)
            {
                SceneManager.SetActiveScene(scene);
            }
            sceneObj.AddChild(CreateObject(GetLightmapSettings(), sfType.LightmapSettings));
            sceneObj.AddChild(CreateObject(GetRenderSettings(), sfType.RenderSettings));
            // Restore the active scene.
            SceneManager.SetActiveScene(activeScene);
        }

        /**
         * Called when a lighting object is created by another user.
         * 
         * @param   sfObject obj that was created.
         * @param   int childIndex of the new object. -1 if the object is a root.
         */
        public override void OnCreate(sfObject obj, int childIndex)
        {
            if (obj.Parent == null)
            {
                ksLog.Warning(this, obj.Type + " object has no parent.");
                return;
            }
            sfSceneTranslator translator = sfObjectEventDispatcher.Get()
                .GetTranslator<sfSceneTranslator>(sfType.Scene);
            Scene scene = translator.GetScene(obj.Parent);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }
            // We can only get the lighting objects for this scene when it is the active scene.
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene != scene)
            {
                SceneManager.SetActiveScene(scene);
            }
            UObject uobj = null;
            switch (obj.Type)
            {
                case sfType.LightmapSettings: uobj = GetLightmapSettings(); break;
                case sfType.RenderSettings: uobj = GetRenderSettings(); break;
            }
            if (uobj != null)
            {
                sfObjectMap.Get().Add(obj, uobj);
                sfPropertyManager.Get().ApplyProperties(uobj, (sfDictionaryProperty)obj.Property);
            }
            // Restore the active scene.
            SceneManager.SetActiveScene(activeScene);
        }

        /**
         * Gets the lightmap settings object for the active scene.
         */
        private LightmapSettings GetLightmapSettings()
        {
            return new ksReflectionObject(typeof(LightmapEditorSettings))
                .GetMethod("GetLightmapSettings").Invoke() as LightmapSettings;
        }

        /**
         * Gets the render settings object for the active scene.
         */
        private RenderSettings GetRenderSettings()
        {
            return new ksReflectionObject(typeof(RenderSettings))
                .GetMethod("GetRenderSettings").Invoke() as RenderSettings;
        }

        /**
         * Refreshes the lighting window.
         */
        private void RefreshWindow()
        {
            if (m_lightingWindow == null)
            {
                m_lightingWindow = ksEditorUtils.FindWindow("LightingWindow");
                if (m_lightingWindow == null)
                {
                    return;
                }
            }
            m_lightingWindow.Repaint();
        }
    }
}
