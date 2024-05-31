using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KS.SceneFusion2.Client;
using KS.Reactor;
using UObject = UnityEngine.Object;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * The object event dispatcher listens for object events and calls the corresponding functions on the translator
     * registered for the object's type.
     */
    public class sfObjectEventDispatcher
    {
        /**
         * @return  sfObjectEventDispatcher singleton instance.
         */
        public static sfObjectEventDispatcher Get()
        {
            return m_instance;
        }
        private static sfObjectEventDispatcher m_instance = new sfObjectEventDispatcher();

        /**
         * Is the object event dispatcher running?
         */
        public bool IsActive
        {
            get { return m_active; }
        }

        private Dictionary<string, sfBaseTranslator> m_translatorMap = new Dictionary<string, sfBaseTranslator>();
        private List<sfBaseTranslator> m_translators = new List<sfBaseTranslator>();
        private bool m_active = false;

        /**
         * Registers a translator to handle events for a given object type.
         *
         * @param   string objectType the translator should handle events for.
         * @param   sfBaseTranslator translator to register.
         */
        public void Register(string objectType, sfBaseTranslator translator)
        {
            if (m_translatorMap.ContainsKey(objectType))
            {
                ksLog.Error(this, "Cannot register translator for '" + objectType +
                    "' because another translator is already registered for that type");
                return;
            }
            m_translatorMap[objectType] = translator;
            if (!m_translators.Contains(translator))
            {
                m_translators.Add(translator);
            }
        }

        /**
         * Calls Initialize on all translators.
         */
        public void InitializeTranslators()
        {
            foreach (sfBaseTranslator translator in m_translators)
            {
                translator.Initialize();
            }
        }

        /**
         * Starts listening for events and calls OnSessionConnect on all registered translators.
         * 
         * @param   sfSession session to listen to events on.
         */
        public void Start(sfSession session)
        {
            if (m_active)
            {
                return;
            }
            m_active = true;
            if (session != null)
            {
                session.OnCreate += OnCreate;
                session.OnConfirmCreate += OnConfirmCreate;
                session.OnDelete += OnDelete;
                session.OnConfirmDelete += OnConfirmDelete;
                session.OnLock += OnLock;
                session.OnUnlock += OnUnlock;
                session.OnLockOwnerChange += OnLockOwnerChange;
                session.OnDirectLockChange += OnDirectLockChange;
                session.OnParentChange += OnParentChange;
                session.OnPropertyChange += OnPropertyChange;
                session.OnRemoveField += OnRemoveField;
                session.OnListAdd += OnListAdd;
                session.OnListRemove += OnListRemove;
            }
            foreach (sfBaseTranslator translator in m_translators)
            {
                translator.OnSessionConnect();
            }
        }

        /**
         * Stops listening for events and calls OnSessionDisconnect on all registered translators.
         * 
         * @param   sfSession session to stop listening to events on.
         */
        public void Stop(sfSession session)
        {
            if (!m_active)
            {
                return;
            }
            m_active = false;
            if (session != null)
            {
                session.OnCreate -= OnCreate;
                session.OnConfirmCreate -= OnConfirmCreate;
                session.OnDelete -= OnDelete;
                session.OnConfirmDelete -= OnConfirmDelete;
                session.OnLock -= OnLock;
                session.OnUnlock -= OnUnlock;
                session.OnLockOwnerChange -= OnLockOwnerChange;
                session.OnDirectLockChange -= OnDirectLockChange;
                session.OnParentChange -= OnParentChange;
                session.OnPropertyChange -= OnPropertyChange;
                session.OnRemoveField -= OnRemoveField;
                session.OnListAdd -= OnListAdd;
                session.OnListRemove -= OnListRemove;
            }
            foreach (sfBaseTranslator translator in m_translators)
            {
                translator.OnSessionDisconnect();
            }
        }

        /**
         * Creates an sfObject for a uobject by calling Create on each translator until one of them handles the request.
         *
         * @param   UObject uobj to create sfObject for.
         * @return  sfObject for the uobject. May be null.
         */
        public sfObject Create(UObject uobj)
        {
            if (uobj == null)
            {
                return null;
            }
            sfObject obj = null;
            foreach (sfBaseTranslator translator in m_translators)
            {
                if (translator.TryCreate(uobj, out obj))
                {
                    break;
                }
            }
            return obj;
        }

        /**
         * Gets the translator for an object.
         *
         * @param   sfObject obj to get translator for.
         * @return  sfBaseTranslator translator for the object, or null if there is no translator for the object's
         *          type.
         */
        public sfBaseTranslator GetTranslator(sfObject obj)
        {
            return obj == null ? null : GetTranslator(obj.Type);
        }

        /**
         * Gets the translator for the given type.
         *
         * @param   string type
         * @return  sfBaseTranslator translator for the type, or null if there is no translator for the given type.
         */
        public sfBaseTranslator GetTranslator(string type)
        {
            sfBaseTranslator translator;
            if (!m_translatorMap.TryGetValue(type, out translator))
            {
                ksLog.Error(this, "Unknown object type '" + type + "'.");
            }
            return translator;
        }

        /**
         * Gets the translator for an object.
         *
         * @param   sfObject obj to get translator for.
         * @return  T translator for the object, or null if there is no translator for the object's type.
         */
        public T GetTranslator<T>(sfObject obj) where T : sfBaseTranslator
        {
            return GetTranslator(obj) as T;
        }

        /**
         * Gets the translator for the given type.
         *
         * @param   string type
         * @return  T translator for the type, or null if there is no translator for the given type.
         */
        public T GetTranslator<T>(string type) where T : sfBaseTranslator
        {
            return GetTranslator(type) as T;
        }

        /**
         * Calls OnCreate on the translator for an object.
         *
         * @param   sfObject obj that was created.
         * @param   int childIndex the object was created at.
         */
        public void OnCreate(sfObject obj, int childIndex)
        {
            sfBaseTranslator translator = GetTranslator(obj);
            if (translator != null)
            {
                translator.OnCreate(obj, childIndex);
            }
        }

        /**
         * Calls OnPropertyChange on the translator for an object.
         *
         * @param   sfBaseProperty property that changed.
         */
        public void OnPropertyChange(sfBaseProperty property)
        {
            sfBaseTranslator translator = GetTranslator(property.GetContainerObject());
            if (translator != null)
            {
                translator.OnPropertyChange(property);
            }
        }

        /**
         * Calls OnConfirmCreate on the translator for an object.
         * 
         * @param   sfObject obj that whose creation was confirmed.
         */
        private void OnConfirmCreate(sfObject obj)
        {
            sfBaseTranslator translator = GetTranslator(obj);
            if (translator != null)
            {
                translator.OnConfirmCreate(obj);
            }
        }

        /**
         * Calls OnDelete on the translator for an object.
         *
         * @param   sfObject obj that was deleted.
         */
        private void OnDelete(sfObject obj)
        {
            sfBaseTranslator translator = GetTranslator(obj);
            if (translator != null)
            {
                translator.OnDelete(obj);
            }
        }

        /**
         * Calls OnConfirmDelete on the translator for an object.
         * 
         * @param   sfObject obj that whose deletion was confirmed.
         */
        private void OnConfirmDelete(sfObject obj)
        {
            sfBaseTranslator translator = GetTranslator(obj);
            if (translator != null)
            {
                translator.OnConfirmDelete(obj);
            }
        }

        /**
         * Calls OnLock on the translator for an object.
         *
         * @param   sfObject obj that was locked.
         */
        private void OnLock(sfObject obj)
        {
            sfBaseTranslator translator = GetTranslator(obj);
            if (translator != null)
            {
                translator.OnLock(obj);
            }
        }

        /**
         * Calls OnUnlock on the translator for an object.
         *
         * @param   sfObject obj that was unlocked.
         */
        private void OnUnlock(sfObject obj)
        {
            sfBaseTranslator translator = GetTranslator(obj);
            if (translator != null)
            {
                translator.OnUnlock(obj);
            }
        }

        /**
         * Calls OnLockOwnerChange on the translator for an object.
         *
         * @param   sfObject obj whose lock owner changed.
         */
        private void OnLockOwnerChange(sfObject obj)
        {
            sfBaseTranslator translator = GetTranslator(obj);
            if (translator != null)
            {
                translator.OnLockOwnerChange(obj);
            }
        }

        /**
         * Calls OnDirectLockChange on the translator for an object.
         *
         * @param   sfObject obj whose direct lock state changed.
         */
        private void OnDirectLockChange(sfObject obj)
        {
            sfBaseTranslator translator = GetTranslator(obj);
            if (translator != null)
            {
                translator.OnDirectLockChange(obj);
            }
        }

        /**
         * Calls OnParentChange on the translator for an object.
         *
         * @param   sfObject obj whose parent changed.
         * @param   int childIndex of the object. -1 if the object is a root.
         */
        private void OnParentChange(sfObject obj, int childIndex)
        {
            sfBaseTranslator translator = GetTranslator(obj);
            if (translator != null)
            {
                translator.OnParentChange(obj, childIndex);
            }
        }

        /**
         * Calls OnRemoveField on the translator for an object.
         *
         * @param   sfDictionaryProperty dict the field was removed from.
         * @param   string name of removed field.
         */
        private void OnRemoveField(sfDictionaryProperty dict, string name)
        {
            sfBaseTranslator translator = GetTranslator(dict.GetContainerObject());
            if (translator != null)
            {
                translator.OnRemoveField(dict, name);
            }
        }

        /**
         * Calls OnListAdd on the translator for an object.
         *
         * @param   sfListProperty list that elements were added to.
         * @param   int index elements were inserted at.
         * @param   int count - number of elements added.
         */
        private void OnListAdd(sfListProperty list, int index, int count)
        {
            sfBaseTranslator translator = GetTranslator(list.GetContainerObject());
            if (translator != null)
            {
                translator.OnListAdd(list, index, count);
            }
        }

        /**
         * Calls OnListRemove on the translator for an object.
         *
         * @param   sfListProperty list that elements were removed from.
         * @param   int index elements were removed from.
         * @param   int count - number of elements removed.
         */
        private void OnListRemove(sfListProperty list, int index, int count)
        {
            sfBaseTranslator translator = GetTranslator(list.GetContainerObject());
            if (translator != null)
            {
                translator.OnListRemove(list, index, count);
            }
        }
    }
}
