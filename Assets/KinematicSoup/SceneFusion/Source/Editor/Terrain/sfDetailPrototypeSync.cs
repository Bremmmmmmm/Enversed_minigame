using System.Collections.Generic;
using UnityEngine;
using KS.Reactor;

namespace KS.SceneFusion2.Unity.Editor
{
    // Manages syncing of DetailPrototypes and Properties
    class sfDetailPrototypeSync
    {
        private delegate void PrototypeSetter(DetailPrototype prototype, sfBaseProperty property);
        private Dictionary<string, PrototypeSetter> m_setters = new Dictionary<string, PrototypeSetter>();

        public sfDetailPrototypeSync()
        {
            RegisterPrototypeSetters();
        }

        private void RegisterPrototypeSetters()
        {
            m_setters[sfProp.HealthyColor] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.healthyColor = property.As<Color>();
            };

            m_setters[sfProp.DryColor] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.dryColor = property.As<Color>();
            };

            m_setters[sfProp.MinWidth] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.minWidth = (float)property;
            };

            m_setters[sfProp.MaxWidth] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.maxWidth = (float)property;
            };

            m_setters[sfProp.MinHeight] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.minHeight = (float)property;
            };

            m_setters[sfProp.MaxHeight] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.maxHeight = (float)property;
            };

            m_setters[sfProp.NoiseSpread] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.noiseSpread = (float)property;
            };

            m_setters[sfProp.RenderMode] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.renderMode = (DetailRenderMode)(int)property;
            };

            m_setters[sfProp.Prototype] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.prototype = sfLoader.Get().Load<GameObject>((string)property);
            };

            m_setters[sfProp.PrototypeTexture] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.prototypeTexture = sfLoader.Get().Load<Texture2D>((string)property);
            };

            m_setters[sfProp.UsePrototypeMesh] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.usePrototypeMesh = (bool)property;
            };
#if UNITY_2021_1_OR_NEWER
            m_setters[sfProp.NoiseSeed] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.noiseSeed = (int)property;
            };

            m_setters[sfProp.HoleEdgePadding] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.holeEdgePadding = (float)property;
            };

            m_setters[sfProp.UseInstancing] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.useInstancing = (bool)property;
            };
#endif
#if UNITY_2022_1_OR_NEWER
            m_setters[sfProp.AlignToGround] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.alignToGround = (float)property;
            };

            m_setters[sfProp.PositionJitter] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.positionJitter = (float)property;
            };

            m_setters[sfProp.UseDensityScale] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.useDensityScaling = (bool)property;
            };

            m_setters[sfProp.Density] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.density = (float)property;
            };
#else
            m_setters[sfProp.BendFactor] = (DetailPrototype prototype, sfBaseProperty property) =>
            {
                prototype.bendFactor = (float)property;
            };
#endif
        }

        public sfDictionaryProperty Serialize(DetailPrototype prototype)
        {
            sfDictionaryProperty dict = new sfDictionaryProperty();
            dict[sfProp.HealthyColor] = new sfValueProperty(prototype.healthyColor);
            dict[sfProp.DryColor] = new sfValueProperty(prototype.dryColor);
            dict[sfProp.MinWidth] = new sfValueProperty(prototype.minWidth);
            dict[sfProp.MaxWidth] = new sfValueProperty(prototype.maxWidth);
            dict[sfProp.MinHeight] = new sfValueProperty(prototype.minHeight);
            dict[sfProp.MaxHeight] = new sfValueProperty(prototype.maxHeight);
            dict[sfProp.NoiseSpread] = new sfValueProperty(prototype.noiseSpread);
            dict[sfProp.RenderMode] = new sfValueProperty((int)prototype.renderMode);
            dict[sfProp.Prototype] = new sfStringProperty(sfLoader.Get().GetAssetPath(prototype.prototype));
            dict[sfProp.PrototypeTexture] = new sfStringProperty(sfLoader.Get().GetAssetPath(prototype.prototypeTexture));
            dict[sfProp.UsePrototypeMesh] = new sfValueProperty(prototype.usePrototypeMesh);
#if UNITY_2021_1_OR_NEWER
            dict[sfProp.NoiseSeed] = new sfValueProperty(prototype.noiseSeed);
            dict[sfProp.HoleEdgePadding] = new sfValueProperty(prototype.holeEdgePadding);
            dict[sfProp.UseInstancing] = new sfValueProperty(prototype.useInstancing);
#endif
#if UNITY_2022_1_OR_NEWER
            dict[sfProp.AlignToGround] = new sfValueProperty(prototype.alignToGround);
            dict[sfProp.PositionJitter] = new sfValueProperty(prototype.positionJitter);
            dict[sfProp.UseDensityScale] = new sfValueProperty(prototype.useDensityScaling);
            dict[sfProp.Density] = new sfValueProperty(prototype.density);
#else
            dict[sfProp.BendFactor] = new sfValueProperty(prototype.bendFactor);
#endif
            return dict;
        }

        public DetailPrototype Deserialize(sfDictionaryProperty dict)
        {
            DetailPrototype prototype = new DetailPrototype();
            UpdatePrototype(prototype, dict);
            return prototype;
        }

        /**
         * Update the prototype fields from a property
         */
        public void UpdatePrototype(DetailPrototype prototype, sfBaseProperty property)
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
         * 
         * @param   sfDictionaryProperty - Detail prototype property
         * @param   DetailPrototype - Prototype to check and update
         **/
        public bool UpdateProperties(sfDictionaryProperty dict, DetailPrototype prototype)
        {
            bool updated = false;
            updated |= UpdateProperty(dict[sfProp.HealthyColor], prototype.healthyColor);
            updated |= UpdateProperty(dict[sfProp.DryColor], prototype.dryColor);
            updated |= UpdateProperty(dict[sfProp.MinWidth], prototype.minWidth);
            updated |= UpdateProperty(dict[sfProp.MaxWidth], prototype.maxWidth);
            updated |= UpdateProperty(dict[sfProp.MinHeight], prototype.minHeight);
            updated |= UpdateProperty(dict[sfProp.MaxHeight], prototype.maxHeight);
            updated |= UpdateProperty(dict[sfProp.NoiseSpread], prototype.noiseSpread);
            updated |= UpdateProperty(dict[sfProp.RenderMode], (int)prototype.renderMode);
            updated |= UpdateProperty(dict[sfProp.Prototype], sfLoader.Get().GetAssetPath(prototype.prototype));
            updated |= UpdateProperty(dict[sfProp.PrototypeTexture], sfLoader.Get().GetAssetPath(prototype.prototypeTexture));
            updated |= UpdateProperty(dict[sfProp.UsePrototypeMesh], prototype.usePrototypeMesh);
#if UNITY_2021_1_OR_NEWER
            updated |= UpdateProperty(dict[sfProp.NoiseSeed], prototype.noiseSeed);
            updated |= UpdateProperty(dict[sfProp.HoleEdgePadding], prototype.holeEdgePadding);
            updated |= UpdateProperty(dict[sfProp.UseInstancing], prototype.useInstancing);
#endif
#if UNITY_2022_1_OR_NEWER
            updated |= UpdateProperty(dict[sfProp.AlignToGround], prototype.alignToGround);
            updated |= UpdateProperty(dict[sfProp.PositionJitter], prototype.positionJitter);
            updated |= UpdateProperty(dict[sfProp.UseDensityScale], prototype.useDensityScaling);
            updated |= UpdateProperty(dict[sfProp.Density], prototype.density);
#else
            updated |= UpdateProperty(dict[sfProp.BendFactor], prototype.bendFactor);
#endif
            return updated;
        }

        public bool UpdateProperty(sfBaseProperty property, ksMultiType value)
        {
            if (property.Type == sfBaseProperty.Types.VALUE)
            {
                if (!(property as sfValueProperty).Value.Equals(value))
                {
                    (property as sfValueProperty).Value = value;
                    return true;
                }
                return false;
            }

            if (property.Type == sfBaseProperty.Types.STRING)
            {
                if ((property as sfStringProperty).String != value.String)
                {
                    (property as sfStringProperty).String = value.String;
                    return true;
                }
                return false;
            }
            return false;
        }
    }
}
