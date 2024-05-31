using System.Collections.Generic;
using UnityEngine;
using KS.Reactor;

namespace KS.SceneFusion2.Unity.Editor
{
    class sfTreePrototypeSync
    {
        private delegate void PrototypeSetter(TreePrototype prototype, sfBaseProperty property);
        private Dictionary<string, PrototypeSetter> m_setters = new Dictionary<string, PrototypeSetter>();

        public sfTreePrototypeSync()
        {
            RegisterPrototypeSetters();
        }

        private void RegisterPrototypeSetters()
        {
            m_setters[sfProp.Prefab] = (TreePrototype prototype, sfBaseProperty property) =>
            {
                prototype.prefab = sfLoader.Get().Load<GameObject>((string)property);
            };
            m_setters[sfProp.BendFactor] = (TreePrototype prototype, sfBaseProperty property) =>
            {
                prototype.bendFactor = (float)property;
            };
        }

        public sfDictionaryProperty Serialize(TreePrototype prototype)
        {
            sfDictionaryProperty dict = new sfDictionaryProperty();
            dict[sfProp.Prefab] = new sfStringProperty(sfLoader.Get().GetAssetPath(prototype.prefab));
            dict[sfProp.BendFactor] = new sfValueProperty(prototype.bendFactor);
            return dict;
        }

        public TreePrototype Deserialize(sfDictionaryProperty dict)
        {
            TreePrototype prototype = new TreePrototype();
            UpdatePrototype(prototype, dict);
            return prototype;
        }

        /**
         * Update the prototype fields from a property
         */
        public void UpdatePrototype(TreePrototype prototype, sfBaseProperty property)
        {
            PrototypeSetter setter;
            if (property.Type == sfBaseProperty.Types.DICTIONARY)
            {
                sfDictionaryProperty dict = property as sfDictionaryProperty;
                foreach (KeyValuePair<string, sfBaseProperty> pair in dict)
                {
                    if (m_setters.TryGetValue(pair.Value.Name, out setter))
                    {
                        setter(prototype, pair.Value);
                    }
                }
            }
            else if (m_setters.TryGetValue(property.Name, out setter))
            {
                setter(prototype, property);
            }
        }

        /**
         * Update properties from a prototype
         **/
        public void UpdateProperties(sfDictionaryProperty dict, TreePrototype prototype)
        {
            UpdateProperty(dict[sfProp.Prefab], sfLoader.Get().GetAssetPath(prototype.prefab));
            UpdateProperty(dict[sfProp.BendFactor], prototype.bendFactor);
        }

        public void UpdateProperty(sfBaseProperty property, ksMultiType value)
        {
            if (property.Type == sfBaseProperty.Types.VALUE)
            {
                if (!(property as sfValueProperty).Value.Equals(value))
                {
                    (property as sfValueProperty).Value = value;
                }
                return;
            }

            if (property.Type == sfBaseProperty.Types.STRING)
            {
                if ((property as sfStringProperty).String != value.String)
                {
                    (property as sfStringProperty).String = value.String;
                }
                return;
            }
        }
    }
}
