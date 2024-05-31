using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
     * Utility class for computing a checksum from an sfBaseProperty.
     */
    public class sfChecksum
    {
        /**
         * Filter for excluding dictionary fields from the checksum.
         * 
         * @param   string name of the dictionary field.
         * @return  false to exclude the field from the checksum.
         */
        public delegate bool Filter(string name);

        /**
         * Computes a fletcher 64 checksum for a property.
         * 
         * @param   sfBaseProperty property to compute checksum for.
         * @param   Filter filter for excluding dictionary fields from the checksum.
         */
        public static ulong Fletcher64(sfBaseProperty property, Filter filter = null)
        {
            // Fletcher-64 computes two 32-bit checksums and combines them to form a 64-bit checksum. The first is the modular
            // sum of each value, and the second is computed from the first by adding the first to the the second every time a
            // value is added to the first.

            // When both sums are 0, the algorithm cannot distinguish between varying lengths of zeros, so we start
            // the first sum at 1.
            ulong checksum1 = 1;
            ulong checksum2 = 0;
            Checksum(property, ref checksum1, ref checksum2, filter);
            return checksum1 + (checksum2 << 32);
        }

        /**
         * Updates two checksum values by adding a uint to the first checksum, then adding the first checksum to the second.
         * 
         * @param   uint value
         * @param   ref ulong checksum1
         * @param   ref ulong checksum2
         */
        private static void Checksum(uint value, ref ulong checksum1, ref ulong checksum2)
        {
            checksum1 += value;
            checksum1 %= (long)uint.MaxValue + 1;
            checksum2 += checksum1;
            checksum2 %= (long)uint.MaxValue + 1;
        }

        /**
         * Computes updated checksum values from a property and the existing checksum values.
         * 
         * @param   sfBaseProperty property to compute checksum for.
         * @param   ref ulong checksum1
         * @param   ref ulong checksum2
         * @param   Filter filter for excluding dictionary fields.
         */
        private static void Checksum(sfBaseProperty property, ref ulong checksum1, ref ulong checksum2, Filter filter)
        {
            Checksum((uint)property.Type, ref checksum1, ref checksum2);
            switch (property.Type)
            {
                case sfBaseProperty.Types.DICTIONARY:
                {
                    Checksum((sfDictionaryProperty)property, ref checksum1, ref checksum2, filter);
                    break;
                }
                case sfBaseProperty.Types.LIST:
                {
                    Checksum((sfListProperty)property, ref checksum1, ref checksum2, filter);
                    break;
                }
                case sfBaseProperty.Types.VALUE:
                {
                    Checksum((sfValueProperty)property, ref checksum1, ref checksum2);
                    break;
                }
                case sfBaseProperty.Types.REFERENCE:
                {
                    Checksum((sfReferenceProperty)property, ref checksum1, ref checksum2);
                    break;
                }
                case sfBaseProperty.Types.STRING:
                {
                    Checksum(((sfStringProperty)property).String, ref checksum1, ref checksum2);
                    break;
                }
            }
        }

        /**
         * Computes updated checksum values from a dictionary property and the existing checksum values.
         * 
         * @param   sfDictionaryProperty dict to compute checksum for.
         * @param   ref ulong checksum1
         * @param   ref ulong checksum2
         * @param   Filter filter for excluding dictionary fields.
         */
        private static void Checksum(sfDictionaryProperty dict, ref ulong checksum1, ref ulong checksum2, Filter filter)
        {
            // Dictionary key order is not defined, so get the keys and sort them
            List<string> names = new List<string>();
            foreach (string key in dict.Keys)
            {
                // Use the filter to exclude keys we don't want
                if (filter == null || filter(key))
                {
                    names.Add(key);
                }
            }
            names.Sort();

            foreach (string name in names)
            {
                Checksum(name, ref checksum1, ref checksum2);
                Checksum(dict[name], ref checksum1, ref checksum2, filter);
            }
        }

        /**
         * Computes updated checksum values from a list property and the existing checksum values.
         * 
         * @param   sfListProperty list to compute checksum for.
         * @param   ref ulong checksum1
         * @param   ref ulong checksum2
         * @param   Filter filter for excluding dictionary fields.
         */
        private static void Checksum(sfListProperty list, ref ulong checksum1, ref ulong checksum2, Filter filter)
        {
            foreach (sfBaseProperty prop in list)
            {
                Checksum(prop, ref checksum1, ref checksum2, filter);
            }
        }

        /**
         * Computes updated checksum values from a value property and the existing checksum values.
         * 
         * @param   sfValueProperty value to compute checksum for.
         * @param   ref ulong checksum1
         * @param   ref ulong checksum2
         */
        private static void Checksum(sfValueProperty value, ref ulong checksum1, ref ulong checksum2)
        {
            ksMultiType multiType = value.Value;
            Checksum((uint)multiType.Type, ref checksum1, ref checksum2);
            if (multiType.IsArray)
            {
                if (multiType.ArrayLength < 0)
                {
                    return;
                }
                Checksum((uint)multiType.ArrayLength, ref checksum1, ref checksum2);
            }
            Checksum(multiType.Data, ref checksum1, ref checksum2);
        }

        /**
         * Computes updated checksum values from a reference property and the existing checksum values.
         * 
         * @param   sfReferenceProperty reference to compute checksum for.
         * @param   ref ulong checksum1
         * @param   ref ulong checksum2
         */
        private static void Checksum(sfReferenceProperty reference, ref ulong checksum1, ref ulong checksum2)
        {
            Checksum(reference.ObjectId, ref checksum1, ref checksum2);
        }

        /**
         * Computes updated checksum values from a string and the existing checksum values.
         * 
         * @param   sfStringProperty str to compute checksum for.
         * @param   ref ulong checksum1
         * @param   ref ulong checksum2
         */
        private static void Checksum(string str, ref ulong checksum1, ref ulong checksum2)
        {
            if (str != null)
            {
                Checksum(System.Text.Encoding.UTF8.GetBytes(str), ref checksum1, ref checksum2);
            }
        }

        /**
         * Computes updated checksum values from a byte array and the existing checksum values.
         * 
         * @param   byte[] data to compute checksum for.
         * @param   ref ulong checksum1
         * @param   ref ulong checksum2
         */
        private static void Checksum(byte[] data, ref ulong checksum1, ref ulong checksum2)
        {
            Checksum((uint)data.Length, ref checksum1, ref checksum2);
            uint val = 0;
            for (int i = 0; i < data.Length; i++)
            {
                int n = i % 4;
                if (n == 0)
                {
                    val = data[i];
                }
                else
                {
                    val += (uint)data[i] << (8 * n);
                }
                if (n == 3 || i == data.Length - 1)
                {
                    Checksum(val, ref checksum1, ref checksum2);
                }
            }
        }
    }
}
