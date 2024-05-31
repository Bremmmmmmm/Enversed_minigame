using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KS.SceneFusion2.Client;
using KS.Reactor;
using UnityEngine;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Syncs changes made by an undo revert prefab operation.
     */
    public class sfUndoRevertOperation : sfBaseUndoOperation
    {
        private GameObject[] m_gameObjects;

        /**
         * Game objects affected by the operation.
         */
        public override GameObject[] GameObjects
        {
            get
            {
                return m_gameObjects;
            }
        }

        /**
         * Constructor
         * 
         * @param   GameObject[] gameObjects affected by the undo operation.
         */
        public sfUndoRevertOperation(GameObject[] gameObjects)
        {
            m_gameObjects = gameObjects;
        }

        /**
         * Syncs properties on the game objects and components affected by the undo or redo operation.
         * 
         * @param   bool isUndo - true if this is an undo operation, false if it is a redo.
         */
        public override void HandleUndoRedo(bool isUndo)
        {
            sfGameObjectTranslator translator = sfObjectEventDispatcher.Get().GetTranslator<sfGameObjectTranslator>(
                sfType.GameObject);
            foreach (GameObject gameObject in m_gameObjects)
            {
                translator.SyncProperties(gameObject);
            }
        }
    }
}
