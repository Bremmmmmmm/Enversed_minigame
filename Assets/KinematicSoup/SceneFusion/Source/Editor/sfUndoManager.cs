using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using KS.Unity.Editor;
using KS.Reactor;
using KS.SceneFusion2.Client;
using KS.SceneFusion;
using UObject = UnityEngine.Object;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Handles syncing of undo/redo operations. Maintains undo and redo stacks that can sync the changes made by each
     * undo operation. Changes made through Unity's serialized property system are automatically detected and recorded
     * on the stack. Other changes must be recorded when detected by translators by calling Record() with a
     * sfBaseUndoOperation.
     */
    public class sfUndoManager
    {
        /**
         * @return  sfUndoManager singleton instance.
         */
        public static sfUndoManager Get()
        {
            return m_instance;
        }
        private static sfUndoManager m_instance = new sfUndoManager();

        /**
         * Stores all property modifications and operations that occurred in an undo operation.
         */
        private class Transaction
        {
            /**
             * Property modifications. May be null.
             */
            public UndoPropertyModification[] Modifications;

            /**
             * Undo operations. May be null.
             */
            public List<sfBaseUndoOperation> Operations;

            /**
             * Constructor
             * 
             * @param   UndoPropertyModification[] modifications
             * @param   List<sfBaseUndoOperation> operations
             */
            public Transaction(UndoPropertyModification[] modifications, List<sfBaseUndoOperation> operations)
            {
                Modifications = modifications;
                Operations = operations;
            }

            /**
             * Syncs changes caused by undoing or redoing the transaction.
             * 
             * @param   bool isUndo - true if this is an undo operation, false if it is a redo.
             */
            public void HandleUndoRedo(bool isUndo)
            {
                if (Modifications != null)
                {
                    RelockObjects();
                    sfPropertyManager.Get().OnModifyProperties(Modifications);
                }
                if (Operations != null)
                {
                    foreach (sfBaseUndoOperation op in Operations)
                    {
                        op.HandleUndoRedo(isUndo);
                        RelockObjects(op.GameObjects);
                    }
                }
            }

            /**
             * Undo/redo operations that modified properties will unlock locked objects, so this relocks any locked
             * objects that had modified properties.
             */
            private void RelockObjects()
            {
                foreach (UndoPropertyModification modification in Modifications)
                {
                    Component component = modification.currentValue.target as Component;
                    GameObject gameObject = component == null ?
                        modification.currentValue.target as GameObject : component.gameObject;
                    if (gameObject == null)
                    {
                        continue;
                    }
                    sfObject obj = sfObjectMap.Get().GetSFObject(gameObject);
                    if (obj != null && obj.IsLocked)
                    {
                        sfUnityUtils.AddFlags(gameObject, HideFlags.NotEditable);
                    }
                }
            }

            /**
             * Game objects modified by an undo operation will have their hideflags reset to the state they were
             * recorded with. This relocks or unlocks game objects to match their sfObject lock state.
             * 
             * @param   GameObject[] gameObjects to relock or unlock.
             */
            private void RelockObjects(GameObject[] gameObjects)
            {
                if (gameObjects == null)
                {
                    return;
                }
                foreach (GameObject gameObject in gameObjects)
                {
                    sfObject obj = sfObjectMap.Get().GetSFObject(gameObject);
                    if (obj != null)
                    {
                        if (obj.IsLocked)
                        {
                            sfUnityUtils.AddFlags(gameObject, HideFlags.NotEditable);
                        }
                        else
                        {
                            sfUnityUtils.RemoveFlags(gameObject, HideFlags.NotEditable);
                        }
                    }
                }
            }
        }

        /**
         * Register undo handler.
         */
        public delegate void RegisterUndoHandler();

        /**
         * Invoked when an operation is registered on Unity's undo stack.
         */
        public event RegisterUndoHandler OnRegisterUndo;

        /**
         * Are we syncing changes made by an undo/redo operation?
         */
        public bool IsHandlingUndoRedo
        {
            get { return m_handlingUndoRedo; }
        }

        private List<Transaction> m_undoStack = new List<Transaction>();
        private List<Transaction> m_redoStack = new List<Transaction>();
        private int m_undoCount = 0;
        private int m_redoCount = 0;
        private int m_lastGroupIndex;
        private bool m_handlingUndoRedo = false;
        private UndoPropertyModification[] m_pendingModifications;
        private List<sfBaseUndoOperation> m_pendingOperations;
        private ksReflectionObject m_getRecordsMethod;

        /**
         * Constructor
         */
        private sfUndoManager()
        {
            m_getRecordsMethod = new ksReflectionObject(typeof(Undo)).GetMethod("GetRecords", 
                paramTypes: new Type[] { typeof(List<string>), typeof(List<string>) });
        }

        /**
         * Starts monitoring undo operations.
         */
        public void Start()
        {
            // We don't allow undoing transactions made before a session started since we don't know what changed in
            // those transactions.
            Undo.ClearAll();
            Undo.postprocessModifications += OnRegister;
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneFusion.Get().OnUpdate += Update;
            m_lastGroupIndex = -1;
        }

        /**
         * Stops monitoring undo operations.
         */
        public void Stop()
        {
            Undo.postprocessModifications -= OnRegister;
            Undo.undoRedoPerformed -= OnUndoRedo;
            SceneFusion.Get().OnUpdate -= Update;
            m_undoStack.Clear();
            m_redoStack.Clear();
        }

        /**
         * Records an operation on the undo stack. If we are currently handling an undo/redo, the operation will not be
         * recorded.
         * 
         * @param   sfBaseUndoOperation operation
         */
        public void Record(sfBaseUndoOperation operation)
        {
            if (m_handlingUndoRedo)
            {
                return;
            }
            // All operations are added to a list of pending operations. When a new undo is recorded on Unity's undo
            // stack, we create our own undo transaction with all pending operations.
            if (m_pendingOperations == null)
            {
                m_pendingOperations = new List<sfBaseUndoOperation>();
                m_pendingOperations.Add(operation);
            }
            else
            {
                AddOperation(m_pendingOperations, operation);
            }
        }

        /**
         * Called when properties are modified. Sets the pending property modifications.
         * 
         * @param   UndoPropertyModifications[] modifications
         * @return  UndoPropertyModifications[] modifications
         */
        private UndoPropertyModification[] OnRegister(UndoPropertyModification[] modifications)
        {
            m_pendingModifications = modifications;
            return modifications;
        }

        /**
         * Adds an operation to a list of operations. Tries to combine the operation with one of the operations in the
         * list if it can.
         * 
         * @param   List<sfBaseUndoOperation> operations to add operation to.
         * @param   sfBaseUndoOperation operation to add.
         */
        private void AddOperation(List<sfBaseUndoOperation> operations, sfBaseUndoOperation operation)
        {
            if (operation.CanCombine)
            {
                for (int i = 0; i < operations.Count; i++)
                {
                    if (operations[i].CombineWith(operation))
                    {
                        return;
                    }
                }
            }
            operations.Add(operation);
        }

        /**
         * Called every frame. Checks for changes to Unity's undo/redo stacks and updates ours accordingly.
         * 
         * @param   float deltaTime since the last frame.
         */
        private void Update(float deltaTime)
        {
            List<string> undoList = new List<string>();
            List<string> redoList = new List<string>();
            m_getRecordsMethod.Invoke(undoList, redoList);
            if (undoList.Count == 0 && redoList.Count == 0 && m_undoCount + m_redoCount > 0)
            {
                m_undoStack.Clear();
                m_redoStack.Clear();
                m_pendingModifications = null;
                m_pendingOperations = null;
            }
            int groupIndex = Undo.GetCurrentGroup();
            if (undoList.Count > m_undoCount && m_lastGroupIndex != groupIndex)
            {
                // A new transaction was recorded on Unity's undo stack. Record one on ours.
                m_undoStack.Add(new Transaction(m_pendingModifications, m_pendingOperations));
                // It's possible to record an operation on Unity's undo stack without clearing the redo stack if you
                // deselect an object, so we need to check if the redo stack was cleared before clearing ours.
                if (redoList.Count == 0)
                {
                    m_redoStack.Clear();
                }
                m_pendingModifications = null;
                m_pendingOperations = null;
                m_lastGroupIndex = groupIndex;

                if (OnRegisterUndo != null)
                {
                    OnRegisterUndo();
                }
            }
            m_undoCount = undoList.Count;
            m_redoCount = redoList.Count;
            if (m_pendingModifications == null && m_pendingOperations == null)
            {
                return;
            }
            if (m_undoStack.Count == 0)
            {
                ksLog.Warning(this, "pending changes with empty undo stack");
                m_pendingModifications = null;
                m_pendingOperations = null;
                return;
            }
            Transaction transaction = m_undoStack[m_undoStack.Count - 1];
            if (m_pendingModifications != null)
            {
                UndoPropertyModification[] modifications = transaction.Modifications;
                if (modifications == null)
                {
                    transaction.Modifications = m_pendingModifications;
                }
                else
                {
                    // Add pending property modifications to the latest undo transaction. Update the value of existing
                    // modifications that changed.
                    List<UndoPropertyModification> toAdd = new List<UndoPropertyModification>();
                    foreach (UndoPropertyModification pending in m_pendingModifications)
                    {
                        bool found = false;
                        for (int i = 0; i < modifications.Length; i++)
                        {
                            UndoPropertyModification mod = modifications[i];
                            if (mod.currentValue.propertyPath == pending.currentValue.propertyPath &&
                                mod.currentValue.target == pending.currentValue.target)
                            {
                                mod.currentValue = pending.currentValue;
                                modifications[i] = mod;
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            toAdd.Add(pending);
                        }
                    }
                    if (toAdd.Count > 0)
                    {
                        modifications = modifications.Concat(toAdd).ToArray();
                        transaction.Modifications = modifications;
                    }
                }
                m_pendingModifications = null;
            }
            if (m_pendingOperations != null)
            {
                // Add pending operations to the latest undo transaction
                if (transaction.Operations == null)
                {
                    transaction.Operations = m_pendingOperations;
                }
                else
                {
                    for (int i = 0; i < m_pendingOperations.Count; i++)
                    {
                        AddOperation(transaction.Operations, m_pendingOperations[i]);
                    }
                }
                m_pendingOperations = null;
            }
        }

        /**
         * Called after an undo or redo occurs. Syncs the changes made by the undo/redo.
         */
        private void OnUndoRedo()
        {
            List<string> undoList = new List<string>();
            List<string> redoList = new List<string>();
            m_getRecordsMethod.Invoke(undoList, redoList);
            if (redoList.Count > m_redoCount)
            {
                // This is an undo
                if (undoList.Count >= m_undoCount)
                {
                    // The transaction is a new one we haven't recorded on the undo stack yet. Record it.
                    m_undoStack.Add(new Transaction(m_pendingModifications, m_pendingOperations));
                    m_pendingModifications = null;
                    m_pendingOperations = null;
                }
                if (m_undoStack.Count == 0)
                {
                    m_redoStack.Add(null);
                    ksLog.Warning(this, "undo with empty stack");
                    return;
                }
                // pop the top undo transaction and push it on the redo stack
                Transaction transaction = m_undoStack[m_undoStack.Count - 1];
                m_undoStack.RemoveAt(m_undoStack.Count - 1);
                m_redoStack.Add(transaction);
                m_handlingUndoRedo = true;
                try
                {
                    transaction.HandleUndoRedo(true);
                }
                catch (Exception e)
                {
                    ksLog.Error(this, "Error handling undo", e);
                }
                m_handlingUndoRedo = false;
            }
            else if (redoList.Count < m_redoCount)
            {
                // This is a redo
                if (m_redoStack.Count == 0)
                {
                    return;
                }
                // pop the top redo transaction and push it on the undo stack
                Transaction transaction = m_redoStack[m_redoStack.Count - 1];
                m_redoStack.RemoveAt(m_redoStack.Count - 1);
                m_undoStack.Add(transaction);
                m_handlingUndoRedo = true;
                try
                {
                    transaction.HandleUndoRedo(false);
                }
                catch (Exception e)
                {
                    ksLog.Error(this, "Error handling redo", e);
                }
                m_handlingUndoRedo = false;
            }
            m_undoCount = undoList.Count;
            m_redoCount = redoList.Count;
        }
    }
}
