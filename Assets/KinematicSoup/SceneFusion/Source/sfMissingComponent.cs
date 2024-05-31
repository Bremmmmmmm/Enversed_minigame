﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KS.Unity;
using UObject = UnityEngine.Object;

namespace KS.SceneFusion2.Unity
{
    /**
     * Stand-in component attached in place of missing components. Stores the missing component name and property data
     * so that when copied, the data for the component can be synced to other users. Will be replaced by the correct
     * component when it becomes available during a session.
     */
    public class sfMissingComponent : MonoBehaviour, sfIMissingScript
    {
        /**
         * Name of the missing component. See sfComponentUtils.GetName.
         */
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }
        [SerializeField]
        private string m_name;

        /**
         * Map of property names to serialized property data.
         */
        public ksSerializableDictionary<string, byte[]> SerializedProperties
        {
            get { return m_serializedProperties; }
        }
        [SerializeField]
        private ksSerializableDictionary<string, byte[]> m_serializedProperties = 
            new ksSerializableDictionary<string, byte[]>();

        /**
         * Map of sfobject ids to uobjects referenced in the serialized data. Because sfobject ids can change between
         * sessions, this is needed to ensure the object references are correct when deserializing data that was
         * serialized in a different session.
         */
        public ksSerializableDictionary<uint, UObject> ReferenceMap
        {
            get { return m_referenceMap; }
        }
        [SerializeField]
        private ksSerializableDictionary<uint, UObject> m_referenceMap = new ksSerializableDictionary<uint, UObject>();

        /**
         * The id of the session the serialized property data is from.
         */
        public uint SessionId
        {
            get { return m_sessionId; }
            set { m_sessionId = value; }
        }
        [SerializeField]
        private uint m_sessionId;
    }
}
