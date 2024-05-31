using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KS.SceneFusion2.Client;
using KS.SceneFusion;
using KS.Reactor;
using UnityEngine;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Syncs component order changes made by an undo operation.
     */
    public class sfUndoComponentOrderOperation : sfBaseUndoOperation
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
        public sfUndoComponentOrderOperation(GameObject[] gameObjects)
        {
            m_gameObjects = gameObjects;
        }

        /**
         * Syncs component order on game objects affected by the undo or redo operation.
         * 
         * @param   bool isUndo - true if this is an undo operation, false if it is a redo.
         */
        public override void HandleUndoRedo(bool isUndo)
        {
            sfComponentTranslator translator = sfObjectEventDispatcher.Get().GetTranslator<sfComponentTranslator>(
                sfType.Component);
            foreach (GameObject gameObject in m_gameObjects)
            {
                sfObject obj = sfObjectMap.Get().GetSFObject(gameObject);
                if (obj != null)
                {
                    translator.SyncComponentOrder(gameObject);
                }
            }
        }
    }
}
