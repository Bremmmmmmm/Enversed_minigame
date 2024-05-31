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
     * Syncs hierarchy changes (parent changes and child order) made by an undo operation.
     */
    public class sfUndoHierarchyOperation : sfBaseUndoOperation
    {
        private HashSet<sfObject> m_oldParents;
        private sfObject m_newParent;

        /**
         * Constructor
         * 
         * @param   HashSet<sfObject> oldParents the children had before the undo operation.
         * @param   sfObject newParent the children have after the undo operation.
         */
        public sfUndoHierarchyOperation(HashSet<sfObject> oldParents, sfObject newParent)
        {
            m_oldParents = oldParents;
            m_newParent = newParent;
        }

        /**
         * Syncs hierarchy changes made by and undo or redo operation.
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
         * Syncs the hierarchies of the parents the children had before the undo operation.
         */
        private void Undo()
        { 
            sfGameObjectTranslator translator = sfObjectEventDispatcher.Get().GetTranslator<sfGameObjectTranslator>(
                sfType.GameObject);
            foreach (sfObject parent in m_oldParents)
            {
                translator.SyncHierarchy(parent);
            }
        }

        /**
         * Syncs the hierarchy of the parent the children had after the undo operation.
         */
        private void Redo()
        {
            sfGameObjectTranslator translator = sfObjectEventDispatcher.Get().GetTranslator<sfGameObjectTranslator>(
                sfType.GameObject);
            translator.SyncHierarchy(m_newParent);
        }
    }
}
