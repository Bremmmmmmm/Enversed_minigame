using UnityEngine;
using UnityEditor;
using KS.Reactor;
using KS.SceneFusion;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Provides access to Scene Fusion texture assets.
     */
    public class sfTextures
    {
        /**
         * Lock icon
         */
        public static Texture2D Lock
        {
            get { return m_lock; }
        }
        private static Texture2D m_lock = Load("lock16");

        /**
         * Checkmark icon
         */
        public static Texture2D Check
        {
            get { return m_check; }
        }
        private static Texture2D m_check = Load("check16");

        /**
         * Small question icon
         */
        public static Texture2D QuestionSmall
        {
            get { return m_questionSmall; }
        }
        private static Texture2D m_questionSmall = Load("QuestionSmall");

        /**
         * Loads a texture.
         * 
         * @param   string name of texture to load.
         * @return  Texture2D texture, or null if texture failed to load.
         */
        private static Texture2D Load(string name)
        {
            string path = sfPaths.Textures + name + ".png";
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                ksLog.Error(typeof(sfTextures).ToString(), "Unable to load texture at " + path);
            }
            return texture;
        }
    }
}
