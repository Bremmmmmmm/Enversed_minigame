using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using KS.SceneFusion2.Client;
using KS.SceneFusion;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Syncs changes made by a component addition or removal operation.
     */
    class sfUndoComponentOperation : sfBaseUndoOperation
    {
        private GameObject m_gameObject;

        /**
         * Game objects affected by the operation.
         */
        public override GameObject[] GameObjects
        {
            get
            {
                return new GameObject[] { m_gameObject };
            }
        }

        /**
         * Constructor
         * 
         * @param   GameObject gameObject with added or removed components.
         */
        public sfUndoComponentOperation(GameObject gameObject)
        {
            m_gameObject = gameObject;
        }

        /**
         * Syncs components on the game object affected by the undo or redo operation.
         * 
         * @param   bool isUndo - true if this is an undo operation, false if it is a redo.
         */
        public override void HandleUndoRedo(bool isUndo)
        {
            if (m_gameObject == null)
            {
                return;
            }
            sfObject obj = sfObjectMap.Get().GetSFObject(m_gameObject);
            if (obj == null)
            {
                return;
            }
            // We sync component changes on selected objects every frame, so to avoid syncing twice, only sync if the
            // object is not selected.
            if (!Selection.Contains(m_gameObject))
            {
                sfComponentTranslator translator = sfObjectEventDispatcher.Get().GetTranslator<sfComponentTranslator>(
                    sfType.Component);
                translator.SyncComponents(m_gameObject);
            }
        }
    }
}
