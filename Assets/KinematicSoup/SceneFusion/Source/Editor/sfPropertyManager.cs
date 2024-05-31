using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using KS.SceneFusion2.Client;
using KS.SceneFusion;
using KS.Reactor;
using KS.Unity.Editor;
using UObject = UnityEngine.Object;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Manages syncing of serialized properties. Converts between sfProperties and serialized properties.
     */
    public class sfPropertyManager
    {
        /**
         * @return  sfPropertyManager singleton instance.
         */
        public static sfPropertyManager Get()
        {
            return m_instance;
        }
        private static sfPropertyManager m_instance = new sfPropertyManager();

        /**
         * Serialized property change event handler.
         * 
         * @param   UObject uobj whose property changed.
         */
        public delegate void SPropertyChangeHandler(UObject uobj);

        /**
         * Missing object reference event handler.
         * 
         * @param   UObject uobj that is referencing a missing object.
         */
        public delegate void MissingObjectHandler(UObject uobj);

        /**
         * Invoked when a reference to a missing object that cannot be synced is found.
         */
        public event MissingObjectHandler OnMissingObject;

        /**
         * Delegate for getting an sfProperty from a SerializedProperty.
         * 
         * @param   SerializedProperty sproperty
         * @return  sfBaseProperty the property as an sfProperty.
         */
        private delegate sfBaseProperty Getter(SerializedProperty sproperty);

        /**
         * Delegate for setting a SerializedProperty from an sfProperty.
         * 
         * @param   SerializedProperty sproperty to set.
         * @param   sfBaseProperty property value to set sproperty to.
         * @return  bool true if the property value changed.
         */
        private delegate bool Setter(SerializedProperty sproperty, sfBaseProperty property);

        private Dictionary<SerializedPropertyType, Getter> m_getters =
            new Dictionary<SerializedPropertyType, Getter>();
        private Dictionary<SerializedPropertyType, Setter> m_setters =
            new Dictionary<SerializedPropertyType, Setter>();
        private GameObject m_defaultObject;
        private ksReflectionObject m_roGradient;

        private Dictionary<string, sfTypeEventMap<SPropertyChangeHandler>> m_spropertyChangeHandlers =
            new Dictionary<string, sfTypeEventMap<SPropertyChangeHandler>>();

        private List<sfBaseProperty> m_changedProperties = new List<sfBaseProperty>();
        private List<string> m_changedDefaultProperties = new List<string>();
        private UndoPropertyModification[] m_delayedModifications;

        /**
         * Properties to exclude from syncing.
         */
        public sfTypePropertyNameMap Blacklist
        {
            get { return m_blacklist; }
        }
        sfTypePropertyNameMap m_blacklist = new sfTypePropertyNameMap();

        /**
         * Hidden properties to sync.
         */
        public sfTypePropertyNameMap SyncedHiddenProperties
        {
            get { return m_syncedHiddenProperties; }
        }
        private sfTypePropertyNameMap m_syncedHiddenProperties = new sfTypePropertyNameMap();

        /**
         * Constructor
         */
        private sfPropertyManager()
        {
            m_roGradient = new ksReflectionObject(typeof(SerializedProperty)).GetProperty("gradientValue");

            sfUnityEventDispatcher.Get().OnModifyProperties += OnModifyProperties;

            RegisterSerializer(SerializedPropertyType.AnimationCurve,
                (SerializedProperty sprop) => sfPropertyUtils.FromAnimationCurve(sprop.animationCurveValue),
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    AnimationCurve value = sfPropertyUtils.ToAnimationCurve(prop);
                    if (!sprop.animationCurveValue.Equals(value))
                    {
                        sprop.animationCurveValue = value;
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.Boolean,
                (SerializedProperty sprop) => sprop.boolValue,
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    bool value = (bool)prop;
                    if (sprop.boolValue != value)
                    {
                        sprop.boolValue = value;
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.Bounds,
                (SerializedProperty sprop) => sfValueProperty.From(sprop.boundsValue),
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    Bounds value = prop.As<Bounds>();
                    if (sprop.boundsValue != value)
                    {
                        sprop.boundsValue = value;
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.BoundsInt,
                (SerializedProperty sprop) => sfValueProperty.From(sprop.boundsIntValue),
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    BoundsInt value = prop.As<BoundsInt>();
                    if (sprop.boundsIntValue != value)
                    {
                        sprop.boundsIntValue = value;
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.Character,
                (SerializedProperty sprop) => sprop.intValue,
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    int value = (int)prop;
                    if (sprop.intValue != value)
                    {
                        sprop.intValue = value;
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.Color,
                (SerializedProperty sprop) => sfValueProperty.From(sprop.colorValue),
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    Color value = prop.As<Color>();
                    if (sprop.colorValue != value)
                    {
                        sprop.colorValue = value;
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.Enum,
                (SerializedProperty sprop) => sprop.intValue,
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    int value = (int)prop;
                    if (sprop.intValue != value)
                    {
                        sprop.intValue = value;
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.ExposedReference, GetExposedReference, SetExposedReference);
            RegisterSerializer(SerializedPropertyType.Float, GetFloat, SetFloat);
            RegisterSerializer(SerializedPropertyType.Generic, GetGeneric, SetGeneric);
            RegisterSerializer(SerializedPropertyType.Gradient,
                (SerializedProperty sprop) => sfPropertyUtils.FromGradient((Gradient)m_roGradient.GetValue(sprop)),
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    Gradient value = sfPropertyUtils.ToGradient(prop);
                    if (!((Gradient)m_roGradient.GetValue(sprop)).Equals(value))
                    {
                        m_roGradient.SetValue(sprop, value);
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.Integer, GetInteger, SetInteger);
            RegisterSerializer(SerializedPropertyType.LayerMask,
                (SerializedProperty sprop) => sprop.intValue,
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    int value = (int)prop;
                    if (sprop.intValue != value)
                    {
                        sprop.intValue = value;
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.ObjectReference, GetObject, SetObject);
            RegisterSerializer(SerializedPropertyType.Quaternion,
                (SerializedProperty sprop) => sfValueProperty.From(sprop.quaternionValue),
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    Quaternion value = prop.As<Quaternion>();
                    if (sprop.quaternionValue != value)
                    {
                        sprop.quaternionValue = value;
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.Rect,
                (SerializedProperty sprop) => sfValueProperty.From(sprop.rectValue),
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    Rect value = prop.As<Rect>();
                    if (sprop.rectValue != value)
                    {
                        sprop.rectValue = value;
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.RectInt,
                (SerializedProperty sprop) => sfValueProperty.From(sprop.rectIntValue),
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    RectInt value = prop.As<RectInt>();
                    if (!sprop.rectIntValue.Equals(value))
                    {
                        sprop.rectIntValue = value;
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.String,
                (SerializedProperty sprop) => sprop.stringValue,
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    string value = (string)prop;
                    if (sprop.stringValue != value)
                    {
                        sprop.stringValue = value;
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.Vector2,
                (SerializedProperty sprop) => sfValueProperty.From(sprop.vector2Value),
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    Vector2 value = prop.As<Vector2>();
                    if (sprop.vector2Value != value)
                    {
                        sprop.vector2Value = value;
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.Vector2Int,
                (SerializedProperty sprop) => sfValueProperty.From(sprop.vector2IntValue),
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    Vector2Int value = prop.As<Vector2Int>();
                    if (sprop.vector2IntValue != value)
                    {
                        sprop.vector2IntValue = value;
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.Vector3,
                (SerializedProperty sprop) => sfValueProperty.From(sprop.vector3Value),
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    Vector3 value = prop.As<Vector3>();
                    if (sprop.vector3Value != value)
                    {
                        sprop.vector3Value = value;
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.Vector3Int,
                (SerializedProperty sprop) => sfValueProperty.From(sprop.vector3IntValue),
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    Vector3Int value = prop.As<Vector3Int>();
                    if (sprop.vector3IntValue != value)
                    {
                        sprop.vector3IntValue = value;
                        return true;
                    }
                    return false;
                });
            RegisterSerializer(SerializedPropertyType.Vector4,
                (SerializedProperty sprop) => sfValueProperty.From(sprop.vector4Value),
                (SerializedProperty sprop, sfBaseProperty prop) =>
                {
                    Vector4 value = prop.As<Vector4>();
                    if (sprop.vector4Value != value)
                    {
                        sprop.vector4Value = value;
                        return true;
                    }
                    return false;
                });
        }

        /**
         * Registers delegates for converting between sfProperty and serialized property for a serialized property
         * type.
         * 
         * @param   SerializedPropertyType type to register delegates for.
         * @param   Getter getter for getting an sfProperty from a serialized property.
         * @param   Setter setter for setting a serialized property from an sfProperty.
         */
        private void RegisterSerializer(SerializedPropertyType type, Getter getter, Setter setter)
        {
            m_getters[type] = getter;
            m_setters[type] = setter;
        }

        /**
         * Converts a serialized property to an sfProperty.
         * 
         * @param   SerializedProperty sprop
         * @return  sfBaseProperty
         */
        public sfBaseProperty GetValue(SerializedProperty sprop)
        {
            if (sprop == null)
            {
                return null;
            }
            Getter getter;
            return m_getters.TryGetValue(sprop.propertyType, out getter) ? getter(sprop) : null;
        }

        /**
         * Sets a serialized property to a value from an sfProperty.
         * 
         * @param   SerializedProperty sprop to set.
         * @oaram   sfBaseProperty prop to get value from.
         * @return  bool true if the value changed.
         */
        public bool SetValue(SerializedProperty sprop, sfBaseProperty prop)
        {
            if (sprop == null || prop == null)
            {
                return false;
            }
            Setter setter;
            if (m_setters.TryGetValue(sprop.propertyType, out setter))
            {
                return setter(sprop, prop);
            }
            return false;
        }

        /**
         * Checks if a serialized property has its default value. For non-prefab properties, this checks against the
         * default script value.
         * 
         * @param   SerializedProperty sprop
         * @return  bool true if the property has its default value.
         */
        public bool IsDefaultValue(SerializedProperty sprop)
        {
            return IsDefaultValue(sprop, true);
        }

        /**
         * Checks if a serialized property has its default value. For non-prefab properties, this checks against the
         * default script value.
         * 
         * @param   SerializedProperty sprop
         * @param   bool destroyDefaultComponent - if the property is not a prefab property, we compare the value
         *          against a default instance of the component. If we don't already have a default instance, we create
         *          one. If this is true, we will destroy the default component after comparing its value. If this is
         *          false, we keep the default component around so we can reuse it.
         * @return  bool true if the property has its default value.
         */
        private bool IsDefaultValue(SerializedProperty sprop, bool destroyDefaultComponent)
        {
            if (PrefabUtility.GetPrefabInstanceHandle(sprop.serializedObject.targetObject) != null)
            {
                return !sprop.prefabOverride;
            }
            UObject defaultObject = GetDefaultObject(sprop.serializedObject.targetObject);
            if (defaultObject == null)
            {
                //TODO: check default type value, eg. int == 0, object reference == null.
                return false;
            }
            SerializedProperty defaultProperty = new SerializedObject(defaultObject).FindProperty(
                sprop.propertyPath);

            bool result =
#if !UNITY_2020_1_OR_NEWER
                // Unity 2019 has an uncatchable error that crashes Unity when you call DataEquals on generic module
                // types for the ParticleSystem component and possibly other components.
                !(sprop.propertyType == SerializedPropertyType.Generic && sprop.type.Contains("Module") &&
                !(sprop.serializedObject.targetObject is MonoBehaviour) &&
                !(sprop.serializedObject.targetObject is ScriptableObject)) &&
#endif
                SerializedProperty.DataEquals(sprop, defaultProperty);
            if (destroyDefaultComponent && defaultObject is Component && !(defaultObject is Transform))
            {
                DestroyDefaultComponents();
            }
            return result;
        }

        /**
         * Sets a serialized property to its default value. For non-prefab properties, this sets it to the default
         * script value.
         * 
         * @param   SerializedProperty sprop to set to default value.
         */
        public void SetToDefaultValue(SerializedProperty sprop)
        {
            SetToDefaultValue(sprop, true);
        }

        /**
         * Sets a serialized property to its default value. For non-prefab properties, this sets it to the default
         * script value.
         * 
         * @param   SerializedProperty sprop to set to default value.
         * @param   bool destroyDefaultComponent - if the property is not a prefab property, we get the default value
         *          from a default instance of the component. If we don't already have a default instance, we create
         *          one. If this is true, we will destroy the default component after getting its value. If this is
         *          false, we keep the default component around so we can resuse it.
         * @return  bool true if the value of the property changed.
         */
        private bool SetToDefaultValue(SerializedProperty sprop, bool destroyDefaultComponent)
        {
            if (!m_getters.ContainsKey(sprop.propertyType))
            {
                return false;
            }
            if (PrefabUtility.GetPrefabInstanceHandle(sprop.serializedObject.targetObject) != null)
            {
                if (sprop.prefabOverride)
                {
                    sprop.prefabOverride = false;
                    return true;
                }
                return false;
            }
            UObject defaultObject = GetDefaultObject(sprop.serializedObject.targetObject);
            if (defaultObject == null)
            {
                return false;
            }
            SerializedProperty defaultProperty = new SerializedObject(defaultObject).FindProperty(
                sprop.propertyPath);
            bool changed = sprop.serializedObject.CopyFromSerializedPropertyIfDifferent(defaultProperty);
            if (destroyDefaultComponent && defaultObject is Component && !(defaultObject is Transform))
            {
                DestroyDefaultComponents();
            }
            return changed;
        }

        /**
         * Gets a default instance of an object.
         * 
         * @param   UObject uobj to get default instance of.
         * @return  UObject default instance of the object.
         */
        private UObject GetDefaultObject(UObject uobj)
        {
            if (m_defaultObject == null)
            {
                m_defaultObject = new GameObject("#SF Default Object");
                m_defaultObject.hideFlags = HideFlags.HideAndDontSave;
                m_defaultObject.SetActive(false);
            }
            if (uobj is GameObject)
            {
                return m_defaultObject;
            }
            if (uobj is Component)
            {
                Component component = m_defaultObject.GetComponent(uobj.GetType());
                if (component == null)
                {
                    component = m_defaultObject.AddComponent(uobj.GetType());
                }
                return component;
            }
            //TODO: ScriptableObject
            return null;
        }

        /**
         * Iterates all properties of an object using Unity serialization and creates sfProperties for properties with
         * non-default values as fields in an sfDictionaryProperty.
         * 
         * @param   UObject uobj to create properties for.
         * @param   sfDictionaryProperty dict to add properties to.
         */
        public void CreateProperties(UObject uobj, sfDictionaryProperty dict)
        {
            if (uobj == null || dict == null)
            {
                return;
            }
            SerializedObject so = new SerializedObject(uobj);
            foreach (SerializedProperty sprop in Iterate(so.GetIterator()))
            {
                if (IsDefaultValue(sprop, false))
                {
                    continue;
                }
                string propertyName = sprop.name;
                sfBaseProperty property = GetValue(sprop);
                if (property != null)
                {
                    dict[propertyName] = property;
                }
            }
            if (uobj is Component && m_defaultObject != null && !(uobj is Transform))
            {
                DestroyDefaultComponents();
            }
        }

        /**
         * Applies property values from an sfDictionaryProperty to an object using Unity serialization.
         * 
         * @param   UObject uobj to apply property values to.
         * @param   sfDictionaryProperty dict to get property values from. If a value for a property is not in the
         *          dictionary, sets the property to its default value.
         */
        public void ApplyProperties(UObject uobj, sfDictionaryProperty dict)
        {
            if (uobj == null || dict == null)
            {
                return;
            }
            SerializedObject so = new SerializedObject(uobj);
            m_changedProperties.Clear();
            m_changedDefaultProperties.Clear();
            foreach (SerializedProperty sprop in Iterate(so.GetIterator()))
            {
                sfBaseProperty property;
                if (dict.TryGetField(sprop.name, out property))
                {
                    if (SetValue(sprop, property))
                    {
                        m_changedProperties.Add(property);
                    }
                }
                else if (SetToDefaultValue(sprop, false))
                {
                    m_changedDefaultProperties.Add(sprop.name);
                }
            }
            if (uobj is Component && m_defaultObject != null && !(uobj is Transform))
            {
                DestroyDefaultComponents();
            }
            if (!sfPropertyUtils.ApplyProperties(so))
            {
                return;
            }

            // Call the post property/uobject change handlers
            sfBaseUObjectTranslator translator = sfObjectEventDispatcher.Get().GetTranslator<sfBaseUObjectTranslator>(
                dict.GetContainerObject());
            if (translator == null)
            {
                return;
            }
            foreach (sfBaseProperty property in m_changedProperties)
            {
                translator.CallPostPropertyChangeHandlers(uobj, property);
            }
            foreach (string name in m_changedDefaultProperties)
            {
                translator.CallPostPropertyChangeHandlers(uobj, null, name);
            }
            m_changedProperties.Clear();
            m_changedDefaultProperties.Clear();
            translator.CallPostUObjectChangeHandlers(uobj);
        }

        /**
         * Iterates all properties of an object and updates an sfDictionaryProperty when its values are different from
         * those on the object. Removes fields from the dictionary for properties that have their default value.
         *
         * @param   UObject uobj to iterate properties on.
         * @param   sfDictionaryProperty dict to update.
         */
        public void SendPropertyChanges(UObject uobj, sfDictionaryProperty dict)
        {
            if (uobj == null || dict == null)
            {
                return;
            }
            SerializedObject so = new SerializedObject(uobj);
            foreach (SerializedProperty sprop in Iterate(so.GetIterator()))
            {
                if (IsDefaultValue(sprop, false))
                {
                    dict.RemoveField(sprop.name);
                }
                else
                {
                    sfBaseProperty property = GetValue(sprop);
                    if (property == null)
                    {
                        return;
                    }

                    sfBaseProperty oldProperty;
                    if (!dict.TryGetField(sprop.name, out oldProperty) || !Copy(oldProperty, property))
                    {
                        dict[sprop.name] = property;
                    }
                }
            }
            if (uobj is Component && m_defaultObject != null && !(uobj is Transform))
            {
                DestroyDefaultComponents();
            }
        }

        /**
         * Copies the data from one property into another if they are the same property type.
         *
         * @param   sfBaseProperty dest to copy into.
         * @param   sfBaseProperty src to copy from.
         * @return  bool false if the properties were not the same type.
         */
        public bool Copy(sfBaseProperty dest, sfBaseProperty src)
        {
            if (dest == null || src == null || dest.Type != src.Type)
            {
                return false;
            }
            switch (dest.Type)
            {
                case sfBaseProperty.Types.VALUE:
                {
                    if (!dest.Equals(src))
                    {
                        ((sfValueProperty)dest).Value = ((sfValueProperty)src).Value;
                    }
                    break;
                }
                case sfBaseProperty.Types.REFERENCE:
                {
                    if (!dest.Equals(src))
                    {
                        ((sfReferenceProperty)dest).ObjectId = ((sfReferenceProperty)src).ObjectId;
                    }
                    break;
                }
                case sfBaseProperty.Types.STRING:
                {
                    if (!dest.Equals(src))
                    {
                        ((sfStringProperty)dest).String = ((sfStringProperty)src).String;
                    }
                    break;
                }
                case sfBaseProperty.Types.LIST:
                {
                    CopyList((sfListProperty)dest, (sfListProperty)src);
                    break;
                }
                case sfBaseProperty.Types.DICTIONARY:
                {
                    CopyDict((sfDictionaryProperty)dest, (sfDictionaryProperty)src);
                    break;
                }
            }
            return true;
        }

        /**
         * Destroys the default object used for getting default property values.
         */
        public void CleanUp()
        {
            if (m_defaultObject != null)
            {
                UObject.DestroyImmediate(m_defaultObject, true);
                m_defaultObject = null;
            }
        }

        /**
         * Gets the serialized property change event map for a property.
         * 
         * @param   string name of property to get event map for.
         * @return  sfTypeEventMap<SPropertyChangeHandler>
         */
        public sfTypeEventMap<SPropertyChangeHandler> OnSPropertyChange(string name)
        {
            sfTypeEventMap<SPropertyChangeHandler> map;
            if (!m_spropertyChangeHandlers.TryGetValue(name, out map))
            {
                map = new sfTypeEventMap<SPropertyChangeHandler>();
                m_spropertyChangeHandlers[name] = map;
            }
            return map;
        }

        /**
         * Adds, removes, and/or sets elements in a destination list to make it the same as a source list.
         *
         * @param   sfListProperty dest to modify.
         * @param   sfListProperty src to make dest a copy of.
         */
        private void CopyList(sfListProperty dest, sfListProperty src)
        {
            // Compares the src list values in lock step with the dest list values. When there is a discrepancy and the
            // list sizes are different, we first check for an element removal (Current src value = Next dest value).
            // Next we check for an element insertion (Next src value = Current dest value). Finally if neither of the
            // above cases were found, we replace the current dest value with the current src value.
            List<sfBaseProperty> toAdd = new List<sfBaseProperty>();
            for (int i = 0; i < src.Count; i++)
            {
                sfBaseProperty element = src[i];
                if (dest.Count <= i)
                {
                    toAdd.Add(element);
                    continue;
                }
                if (element.Equals(dest[i]))
                {
                    continue;
                }
                if (src.Count != dest.Count)
                {
                    // if the current src element matches the next next element, remove the current dest element.
                    if (dest.Count > i + 1 && element.Equals(dest[i + 1]))
                    {
                        dest.RemoveAt(i);
                        continue;
                    }
                    // if the current dest element matches the next src element, insert the current src element.
                    if (src.Count > i + 1 && dest[i].Equals(src[i + 1]))
                    {
                        dest.Insert(i, element);
                        i++;
                        continue;
                    }
                }
                if (!Copy(dest[i], element))
                {
                    dest[i] = element;
                }
            }
            if (toAdd.Count > 0)
            {
                dest.AddRange(toAdd);
            }
            else if (dest.Count > src.Count)
            {
                dest.RemoveRange(src.Count, dest.Count - src.Count);
            }
        }

        /**
         * Adds, removes, and/or sets fields in a destination dictionary so to make it the same as a source dictionary.
         *
         * @param   sfDictionaryProperty dest to modify.
         * @param   sfDictionaryProperty src to make destPtr a copy of.
         */
        private void CopyDict(sfDictionaryProperty dest, sfDictionaryProperty src)
        {
            List<string> toRemove = new List<string>();
            foreach (KeyValuePair<string, sfBaseProperty> pair in dest)
            {
                if (!src.HasField(pair.Key))
                {
                    toRemove.Add(pair.Key);
                }
            }
            foreach (string key in toRemove)
            {
                dest.RemoveField(key);
            }
            foreach (KeyValuePair<string, sfBaseProperty> pair in src)
            {
                sfBaseProperty destProp;
                if (!dest.TryGetField(pair.Key, out destProp) || !Copy(destProp, pair.Value))
                {
                    dest[pair.Key] = pair.Value;
                }
            }
        }

        /**
         * Invokes the serialized property change event for a property.
         * 
         * @param   UObject uobj whose property changed.
         * @param   string name of changed property.
         */
        private void CallSPropertyChangeHandlers(string name, UObject uobj)
        {
            sfTypeEventMap<SPropertyChangeHandler> map;
            if (m_spropertyChangeHandlers.TryGetValue(name, out map))
            {
                SPropertyChangeHandler handlers = map.GetHandlers(uobj.GetType());
                if (handlers != null)
                {
                    handlers(uobj);
                }
            }
        }

        /**
         * Called when Unity serialized properties change. If the user is not dragging an asset, processes the changes
         * now. Otherwise processes the changes at the end of the frame unless an undo occurs between now in then, in
         * which case the changes are discarded. This is to prevent sending spam changes when dragging assets such as
         * materials into the scene which can trigger a property change and an undo every single frame.
         * 
         * @param   UndoPropertyModification[] modifications
         * @return  UndoPropertyModification[] modifications that are allowed.
         */
        public UndoPropertyModification[] OnModifyProperties(UndoPropertyModification[] modifications)
        {
            if (modifications == null)
            {
                return null;
            }
            if (DragAndDrop.objectReferences.Length > 0)
            {
                // The user is dragging an asset such as a material. Delay processing until the end of the frame.
                // Cancel processing if an undo is triggered before then.
                if (!sfUndoManager.Get().IsHandlingUndoRedo)
                {
                    if (m_delayedModifications == null)
                    {
                        m_delayedModifications = modifications;
                        EditorApplication.delayCall += ProcessDelayedModifications;
                        Undo.undoRedoPerformed += ClearDelayedModifications;
                    }
                    else
                    {
                        m_delayedModifications = m_delayedModifications.Concat(modifications).ToArray();
                    }
                }
            }
            else
            {
                ProcessModifiedProperties(modifications);
            }
            return modifications;
        }

        /**
         * Clears the delayed property modifications.
         */
        private void ClearDelayedModifications()
        {
            m_delayedModifications = null;
        }

        /**
         * Processes property modifications that were delayed because the user was dragging an asset.
         */
        private void ProcessDelayedModifications()
        {
            Undo.undoRedoPerformed -= ClearDelayedModifications;
            ProcessModifiedProperties(m_delayedModifications);
            m_delayedModifications = null;
        }

        /**
         * Processes modifed Unity properties. If the uobject is locked, prevents the property from changing. Otherwise 
         * calls OnSPropertyChange on the translator for the uobject whose property changed. If that returns false,
         * syncs the property change.
         * 
         * @param   UndoPropertyModification[] modifications
         */
        private void ProcessModifiedProperties(UndoPropertyModification[] modifications)
        {
            if (modifications == null)
            {
                return;
            }
            // Maps uobjects to modified property paths
            Dictionary<UObject, HashSet<string>> propertyMap = new Dictionary<UObject, HashSet<string>>();
            foreach (UndoPropertyModification modification in modifications)
            {
                if (modification.currentValue.target == null)
                {
                    continue;
                }
                //ksLog.Debug($"UObject {modification.currentValue.target.GetType().Name} modified {modification.currentValue.propertyPath}");
                CallSPropertyChangeHandlers(modification.currentValue.propertyPath, modification.currentValue.target);
                sfObject obj = sfObjectMap.Get().GetSFObject(modification.currentValue.target);
                if (obj == null || !obj.IsSyncing)
                {
                    continue;
                }
                // Get the root property path. We reconstruct the sfProperty for the whole root property instead of the
                // subproperty.
                string path = modification.currentValue.propertyPath;
                int index = path.IndexOf(".");
                if (index >= 0)
                {
                    path = path.Substring(0, index);
                }
                // Add the path to the modified property paths set.
                HashSet<string> properties;
                if (!propertyMap.TryGetValue(modification.currentValue.target, out properties))
                {
                    properties = new HashSet<string>();
                    propertyMap[modification.currentValue.target] = properties;
                }
                properties.Add(path);
            }
            // For each uobject with modified properties
            foreach (KeyValuePair<UObject, HashSet<string>> pair in propertyMap)
            {
                sfObject obj = sfObjectMap.Get().GetSFObject(pair.Key);
                HashSet<string> paths = pair.Value;// paths of properties that were modified
                SerializedObject so = new SerializedObject(pair.Key);
                // Iterate the properties looking for properties with paths in the modified property paths set.
                foreach (SerializedProperty sprop in Iterate(so.GetIterator()))
                {
                    if (paths.Count == 0)
                    {
                        break;
                    }
                    if (!paths.Remove(sprop.propertyPath))
                    {
                        // This property was not modified
                        continue;
                    }
                    sfBaseTranslator translator = sfObjectEventDispatcher.Get().GetTranslator(obj);
                    if (translator != null && translator.OnSPropertyChange(obj, sprop))
                    {
                        // The translator handled the property change event, so we don't need to sync it.
                        continue;
                    }
                    // Sync the property
                    sfDictionaryProperty dict = (sfDictionaryProperty)obj.Property;
                    if (obj.IsLocked)
                    {
                        // Revert to the server value
                        sfBaseProperty property;
                        if (dict.TryGetField(sprop.name, out property))
                        {
                            SetValue(sprop, property);
                        }
                        else
                        {
                            SetToDefaultValue(sprop, false);
                        }
                        sfPropertyUtils.ApplyProperties(so);
                        if (pair.Key is Component && m_defaultObject != null && !(pair.Key is Transform))
                        {
                            DestroyDefaultComponents();
                        }
                    }
                    else if (IsDefaultValue(sprop))
                    {
                        dict.RemoveField(sprop.propertyPath);
                    }
                    else
                    {
                        sfBaseProperty property = GetValue(sprop);
                        if (property != null)
                        {
                            sfBaseProperty oldProperty;
                            if (!dict.TryGetField(sprop.name, out oldProperty) || !Copy(oldProperty, property))
                            {
                                dict[sprop.name] = property;
                            }
                        }
                    }
                }
            }
        }

        /**
         * Destroys all components on the default object.
         */
        private void DestroyDefaultComponents()
        {
            // Iterate components backwards to ensure dependent components are destroyed first.
            Component[] components = m_defaultObject.GetComponents<Component>();
            for (int i = components.Length - 1; i >= 0; i--)
            {
                Component component = components[i];
                if (!(component is Transform))
                {
                    UObject.DestroyImmediate(component);
                }
            }
        }

        /**
         * Gets a float serialized property value converted to an sfProperty.
         * 
         * @param   SerializedProperty sprop to convert.
         * @return  sfBaseProperty converted property.
         */
        private sfBaseProperty GetFloat(SerializedProperty sprop)
        {
            return sprop.type == "double" ?
                sfValueProperty.From(sprop.doubleValue) : new sfValueProperty(sprop.floatValue);
        }

        /**
         * Sets a float serialized property to a value from an sfProperty.
         * 
         * @param   SerializedProperty sprop to set.
         * @param   sfBaseProperty prop to get value from.
         * @param   return bool true if the property changed.
         */
        private bool SetFloat(SerializedProperty sprop, sfBaseProperty prop)
        {
            if (sprop.type == "double")
            {
                double value = prop.As<double>();
                if (sprop.doubleValue != value)
                {
                    sprop.doubleValue = value;
                    return true;
                }
            }
            else
            {
                float value = (float)prop;
                if (sprop.floatValue != value)
                {
                    sprop.floatValue = value;
                    return true;
                }
            }
            return false;
        }

        /**
         * Gets an int serialized property value converted to an sfProperty.
         * 
         * @param   SerializedProperty sprop to convert.
         * @return  sfBaseProperty converted property.
         */
        private sfBaseProperty GetInteger(SerializedProperty sprop)
        {
            switch (sprop.type)
            {
                default:
                case "int":
                case "short":
                {
                    return sprop.intValue;
                }
                case "uint":
                case "ushort":
                {
                    return (uint)sprop.longValue;
                }
                case "byte":
                case "sbyte":
                {
                    return (byte)sprop.intValue;
                }
                case "long":
                case "ulong":
                {
                    return sprop.longValue;
                }
            }
        }

        /**
         * Sets an int serialized property to a value from an sfProperty.
         * 
         * @param   SerializedProperty sprop to set.
         * @param   sfBaseProperty prop to get value from.
         * @return  bool true if the property changed.
         */
        private bool SetInteger(SerializedProperty sprop, sfBaseProperty prop)
        {
            switch (sprop.type)
            {
                default:
                case "int":
                case "short":
                {
                    int value = (int)prop;
                    if (sprop.intValue != value)
                    {
                        sprop.intValue = value;
                        return true;
                    }
                    return false;
                }
                case "uint":
                case "ushort":
                {
                    uint value = (uint)prop;
                    if (sprop.longValue != value)
                    {
                        sprop.longValue = value;
                        return true;
                    }
                    return false;
                }
                case "byte":
                case "sbyte":
                {
                    byte value = (byte)prop;
                    if (sprop.intValue != value)
                    {
                        sprop.intValue = value;
                        return true;
                    }
                    return false;
                }
                case "long":
                case "ulong":
                {
                    long value = (long)prop;
                    if (sprop.longValue != value)
                    {
                        sprop.longValue = value;
                        return true;
                    }
                    return false;
                }
            }
        }

        /**
         * Gets an object reference serialized property value converted to an sfProperty.
         * 
         * @param   SerializedProperty sprop to convert.
         * @return  sfBaseProperty converted property.
         */
        private sfBaseProperty GetObject(SerializedProperty sprop)
        {
            UObject reference = sprop.objectReferenceValue;
            if (reference == null)
            {
                if (sprop.objectReferenceInstanceIDValue != 0)
                {
                    ksLog.Warning(this, "Reference to missing object found on " +
                        sprop.serializedObject.targetObject.name + " (" +
                        sprop.serializedObject.targetObject.GetType().Name + "). Path: " + sprop.propertyPath + 
                        ". Missing objects cannot be synced and the reference will be set to null for other users.", 
                        sprop.serializedObject.targetObject);
                    if (OnMissingObject != null)
                    {
                        OnMissingObject(sprop.serializedObject.targetObject);
                    }
                }
                return new sfNullProperty();
            }

            if (sfLoader.Get().IsAsset(reference))
            {
                // Asset
                string assetPath = sfLoader.Get().GetAssetPath(reference);
                if (assetPath == "UnityEngine.Texture2D/Soft" && sprop.serializedObject.targetObject is RenderSettings &&
                    sprop.propertyPath == "m_SpotCookie")
                {
                    // Setting RenderSettings.SpotCookie to null/none will set it to a built-in "Soft" texture that
                    // we cannot load, so we treat it as null.
                    return new sfNullProperty();
                }

                // Sync the asset if it can be synced and isn't already synced.
                if (sfLoader.Get().IsCreatableAssetType(reference) && !sfObjectMap.Get().Contains(reference))
                {
                    sfObjectEventDispatcher.Get().Create(reference);
                }

                return new sfStringProperty(assetPath);
            }

            // Scene object
            sfObject obj = sfObjectMap.Get().GetSFObject(reference);
            if (obj == null)
            {
                obj = sfObjectEventDispatcher.Get().Create(reference);
                if (obj == null)
                {
                    ksLog.Warning("Unable to sync reference to " + reference.GetType().Name + " " + reference.name);
                    // Empty string means keep your current value
                    return new sfValueProperty("");
                }
            }
            return new sfReferenceProperty(obj.Id);
        }

        /**
         * Sets an object reference serialized property to a value from an sfProperty.
         * 
         * @param   SerializedProperty sprop to set.
         * @param   sfBaseProperty prop to get value from.
         * @return  bool true if the property changed.
         */
        private bool SetObject(SerializedProperty sprop, sfBaseProperty prop)
        {
            if (prop.Type == sfBaseProperty.Types.NULL)
            {
                if (sprop.objectReferenceValue != null)
                {
                    sprop.objectReferenceValue = null;
                    return true;
                }
                return false;
            }
            if (prop.Type == sfBaseProperty.Types.REFERENCE)
            {
                // Scene object
                uint objId = ((sfReferenceProperty)prop).ObjectId;
                sfObject obj = SceneFusion.Get().Service.Session.GetObject(objId);
                UObject uobj = sfObjectMap.Get().GetUObject(obj);
                if (sprop.objectReferenceValue!= uobj)
                {
                    sprop.objectReferenceValue = uobj;
                    return true;
                }
                return false;
            }
            if (prop.Type == sfBaseProperty.Types.STRING)
            {
                // Asset
                string path = (string)prop;
                if (string.IsNullOrEmpty(path))
                {
                    return false;
                }
                UObject asset = sfLoader.Get().Load(path);
                if (asset != null && sprop.objectReferenceValue != asset)
                {
                    sprop.objectReferenceValue = asset;
                    return true;
                }
            }
            return false;
        }

        /**
         * Gets an exposed reference serialized property value converted to an sfProperty.
         * 
         * @param   SerializedProperty sprop to convert.
         * @return  sfBaseProperty converted property.
         */
        private sfBaseProperty GetExposedReference(SerializedProperty sprop)
        {
            UObject reference = sprop.exposedReferenceValue;
            if (reference == null)
            {
                return new sfNullProperty();
            }

            if (sfLoader.Get().IsAsset(reference))
            {
                // Asset
                return new sfStringProperty(sfLoader.Get().GetAssetPath(reference));
            }

            // Scene object
            sfObject obj = sfObjectMap.Get().GetSFObject(reference);
            if (obj == null)
            {
                ksLog.Warning("Unable to sync reference to " + reference.GetType().Name + " " + reference.name);
                // Empty string means keep your current value
                return new sfValueProperty("");
            }
            return new sfReferenceProperty(obj.Id);
        }

        /**
         * Sets an exposed reference serialized property to a value from an sfProperty.
         * 
         * @param   SerializedProperty sprop to set.
         * @param   sfBaseProperty prop to get value from.
         * @return  bool true if the property changed.
         */
        private bool SetExposedReference(SerializedProperty sprop, sfBaseProperty prop)
        {
            if (prop.Type == sfBaseProperty.Types.NULL)
            {
                if (sprop.exposedReferenceValue != null)
                {
                    sprop.exposedReferenceValue = null;
                    return true;
                }
                return false;
            }
            if (prop.Type == sfBaseProperty.Types.REFERENCE)
            {
                // Scene object
                uint objId = ((sfReferenceProperty)prop).ObjectId;
                sfObject obj = SceneFusion.Get().Service.Session.GetObject(objId);
                UObject uobj = sfObjectMap.Get().GetUObject(obj);
                if (sprop.exposedReferenceValue != uobj)
                {
                    sprop.exposedReferenceValue = uobj;
                    return true;
                }
                return false;
            }
            if (prop.Type == sfBaseProperty.Types.STRING)
            {
                // Asset
                string path = (string)prop;
                if (string.IsNullOrEmpty(path))
                {
                    return false;
                }
                UObject asset = sfLoader.Get().Load(path);
                if (asset != null && asset != sprop.exposedReferenceValue)
                {
                    sprop.exposedReferenceValue = asset;
                    return true;
                }
            }
            return false;
        }

        /**
         * Sets an array of references to the given uobject.
         *
         * @param   UObject uobj to set references to.
         * @param   sfReferenceProperty[] references to set to the uobject.
         */
        public void SetReferences(UObject uobj, sfReferenceProperty[] references)
        {
            foreach (sfReferenceProperty reference in references)
            {
                bool handled = false;
                sfBaseProperty current = reference;
                while (current != null)
                {
                    if (!string.IsNullOrEmpty(current.Name) && current.Name[0] == '#')
                    {
                        // If the property starts with '#' it is one of our custom properties. Call the object event dispatcher
                        // to run the appropriate property change handler.
                        sfObjectEventDispatcher.Get().OnPropertyChange(reference);
                        handled = true;
                        break;
                    }
                    current = current.ParentProperty;
                }
                if (handled)
                {
                    continue;
                }

                UObject referencingObject = sfObjectMap.Get().GetUObject(reference.GetContainerObject());
                if (referencingObject == null)
                {
                    continue;
                }
                // Set reference
                SerializedObject so = new SerializedObject(referencingObject);
                SerializedProperty sprop = so.FindProperty(GetSerializedPropertyPath(reference));
                if (sprop != null)
                {
                    if (sprop.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        sprop.objectReferenceValue = uobj;
                    }
                    else if (sprop.propertyType == SerializedPropertyType.ExposedReference)
                    {
                        sprop.exposedReferenceValue = uobj;
                    }
                }
                sfPropertyUtils.ApplyProperties(so);
            }
        }

        /**
         * Gets the serialized property for an sfBaseProperty.
         * 
         * @param   SerializedObject to get serialized property from.
         * @param   sfBaseProperty prop to get serialized property for.
         * @return  SerializedProperty
         */
        public SerializedProperty GetSerializedProperty(SerializedObject so, sfBaseProperty prop)
        {
            return so.FindProperty(GetSerializedPropertyPath(prop));
        }

        /**
         * Gets the parent property of a serialized property.
         * 
         * @param   SerializedProperty prop to get parent for.
         * @return  SerializedProperty parent property.
         */
        public SerializedProperty GetParentProperty(SerializedProperty prop)
        {
            string path = prop.propertyPath;
            int index = path.LastIndexOf('.');
            if (index <= 0)
            {
                return null;
            }
            if (path.Substring(index + 1).StartsWith("data["))
            {
                // Subtract ".Array"
                index -= 6;
                if (index <= 0)
                {
                    return null;
                }
            }
            return prop.serializedObject.FindProperty(path.Substring(0, index));
        }

        /**
         * Gets the serialized property for field of a sfDictionaryProperty.
         * 
         * @param   SerializedObject to get serialized property from.
         * @param   sfDictionaryProperty dict to get serialized property for.
         * @param   string name of the field to get serialized property for.
         * @return  SerializedProperty
         */
        public SerializedProperty GetSerializedProperty(SerializedObject so, sfDictionaryProperty dict, string name)
        {
            string path;
            if (dict.ParentProperty == null)
            {
                path = name;
            }
            else
            {
                path = GetSerializedPropertyPath(dict) + "." + name;
            }
            return so.FindProperty(path);
        }

        /**
         * Gets the serialized property path for the given sfBaseProperty.
         * Builds the full property path that connects the given property and all its ancestor's property name.
         * If the property is an array element, then the property name will be its index. Add "Array.data[]" around
         * it to match with the Unity array element property path.
         * 
         * @param   sfBaseProperty prop
         * @return  string
         */
        private string GetSerializedPropertyPath(sfBaseProperty prop)
        {
            if (prop == null || prop.ParentProperty == null)
            {
                return "";
            }

            string path = "";
            sfBaseProperty current = prop;
            while (true)
            {
                if (current.ParentProperty is sfDictionaryProperty)
                {
                    path = current.Name + path;
                }
                else if (current.ParentProperty is sfListProperty)
                {
                    path = "Array.data[" + current.Index + "]" + path;
                }
                else
                {
                    path = current.Name + path;
                }
                current = current.ParentProperty;
                if (current.ParentProperty != null)
                {
                    path = "." + path;
                }
                else
                {
                    break;
                }
            }
            return path;
        }

        /**
         * Gets a generic serialized property value converted to an sfProperty.
         * 
         * @param   SerializedProperty sprop to convert.
         * @return  sfBaseProperty converted property.
         */
        public sfBaseProperty GetGeneric(SerializedProperty sprop)
        {
            sfDictionaryProperty dict = null;
            sfListProperty list = null;
            if (sprop.isArray)
            {
                list = new sfListProperty();
            }
            else
            {
                dict = new sfDictionaryProperty();
            }
            foreach (SerializedProperty subProp in Iterate(sprop))
            {
                if (!sprop.isArray && IsDefaultValue(subProp, false))
                {
                    continue;
                }
                sfBaseProperty property = GetValue(subProp);
                if (property != null)
                {
                    if (sprop.isArray)
                    {
                        list.Add(property);
                    }
                    else
                    {
                        dict[subProp.name] = property;
                    }
                }
            }
            if (sprop.serializedObject.targetObject is Component &&
                m_defaultObject != null &&
                !(sprop.serializedObject.targetObject is Transform))
            {
                DestroyDefaultComponents();
            }
            return sprop.isArray ? (sfBaseProperty)list : (sfBaseProperty)dict;
        }

        /**
         * Sets a generic serialized property to a value from an sfProperty.
         * 
         * @param   SerializedProperty sprop to set.
         * @param   sfBaseProperty prop to get value from.
         * @return  bool true if the property changed.
         */
        public bool SetGeneric(SerializedProperty sprop, sfBaseProperty prop)
        {
            sfDictionaryProperty dict =
                prop.Type == sfBaseProperty.Types.DICTIONARY ? (sfDictionaryProperty)prop : null;
            sfListProperty list =
                prop.Type == sfBaseProperty.Types.LIST ? (sfListProperty)prop : null;
            bool changed = false;
            if (sprop.isArray && list != null && sprop.arraySize != list.Count)
            {
                sprop.arraySize = list.Count;
                changed = true;
            }
            int index = 0;
            foreach (SerializedProperty subProp in Iterate(sprop))
            {
                if (sprop.isArray && subProp.propertyType != SerializedPropertyType.ArraySize && list != null)
                {
                    if (list.Count > index)
                    {
                        changed = SetValue(subProp, list[index]) || changed;
                    }
                    else
                    {
                        changed = SetToDefaultValue(subProp, false) || changed;
                    }
                    index++;
                }
                else if (dict != null)
                {
                    sfBaseProperty property;
                    if (dict.TryGetField(subProp.name, out property))
                    {
                        changed = SetValue(subProp, property) || changed;
                    }
                    else
                    {
                        changed = SetToDefaultValue(subProp, false) || changed;
                    }
                }
            }
            if (sprop.serializedObject.targetObject is Component &&
                m_defaultObject != null &&
                !(sprop.serializedObject.targetObject is Transform))
            {
                DestroyDefaultComponents();
            }
            return changed;
        }

        /**
         * Iterates all syncable root properties or subproperties of a property. To iterate root properties, call this
         * the iterator returned by serializedObject.GetIterator().
         * 
         * @param   SerializedProperty property to iterate the syncable subproperties of. If this is the iterator
         *          returned by serializedObject.GetIterator(), it will iterate the root properties.
         */
        public IEnumerable<SerializedProperty> Iterate(SerializedProperty property)
        {
            // Get the black list and hidden sync properties for this type.
            SerializedProperty iter;
            Type type = null;
            bool iteratingRootProperties = property.depth == -1;
            if (iteratingRootProperties)
            {
                iter = property;
                // If iterating root properties, get the type from the serialized object.
                type = iter.serializedObject.targetObject.GetType();
            }
            else
            {
                iter = property.Copy();
#if UNITY_2022_1_OR_NEWER
                try
                {
                    // Try to get the C# type from the boxed value if we can.
                    object obj = iter.boxedValue;
                    if (obj != null)
                    {
                        type = obj.GetType();
                    }
                }
                catch (Exception)
                {

                }
#endif
            }
            HashSet<string> blackList;
            HashSet<string> hiddenProperties;
            if (type == null)
            {
                // We could not determine the type, so use the type name from the property.
                blackList = m_blacklist.GetProperties(iter.type);
                hiddenProperties = m_syncedHiddenProperties.GetProperties(iter.type);
            }
            else
            {
                blackList = m_blacklist.GetProperties(type);
                hiddenProperties = m_syncedHiddenProperties.GetProperties(type);
            }

            // Iterate the visible properties that aren't in the black list.
            bool enterChildren = true;
            int depth = iter.depth;
            while (iter.NextVisible(enterChildren) && iter.depth > depth)
            {
                enterChildren = false;
                if (blackList == null || !blackList.Contains(iter.name))
                {
                    if (hiddenProperties != null && hiddenProperties.Contains(iter.name))
                    {
                        string typeName = iter.depth == -1 ? iter.serializedObject.targetObject.GetType().Name : iter.type;
                        ksLog.Warning(this, "Property " + iter.name + " on " + typeName +
                            " is in the synced hidden properties set, but it was not hidden.");
                    }
                    else
                    {
                        yield return iter;
                    }
                }
            }

            SerializedObject so = iter.serializedObject;

            // Iterate the synced hidden properties. We have to do this after iterating the visible properties because
            // if we do it before, calling serializedObject.FindProperty and setting the value of the returned property
            // will invalidate our property iterator because of a Unity bug.
            if (hiddenProperties != null)
            {
                foreach (string name in hiddenProperties)
                {
                    SerializedProperty sprop = iteratingRootProperties ?
                        so.FindProperty(name) : property.FindPropertyRelative(name);
                    if (sprop != null)
                    {
                        yield return sprop;
                    }
                    else
                    {
                        string typeName = iteratingRootProperties ?
                            so.targetObject.GetType().Name : property.type;
                        ksLog.Warning(this, "Could not find hidden property " + name + " on " + typeName);
                    }
                }
            }

            // Check if the object has an enabled flag
            if (iteratingRootProperties && EditorUtility.GetObjectEnabled(so.targetObject) >= 0)
            {
                SerializedProperty sprop = so.FindProperty("m_Enabled");
                if (sprop != null)
                {
                    yield return sprop;
                }
            }
        }
    }
}
