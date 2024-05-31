using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEditor;
using KS.SceneFusion2.Client;
using KS.SceneFusion;
using KS.Reactor;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Utility class for converting between sfProperties and common types.
     */
    public class sfPropertyUtils
    {
        private static readonly string LOG_CHANNEL = typeof(sfPropertyUtils).ToString();

        /**
         * Applies serialized property changes without registering an undo operation, and marks the target object dirty
         * if there were any changes.
         * 
         * @param   SerializedObject so - serialized object to apply properties to.
         * @param   bool rebuildInspectors - if true and properties changed, rebuilds inspectors, which is slower but
         *          necessary for it to display properly if components were added or deleted.
         * @return  bool true if there were any changes applied.
         */
        public static bool ApplyProperties(SerializedObject so, bool rebuildInspectors = false)
        {
            if (so.ApplyModifiedPropertiesWithoutUndo())
            {
                EditorUtility.SetDirty(so.targetObject);
                sfUI.Get().MarkInspectorStale(so.targetObject, rebuildInspectors);
                return true;
            }
            return false;
        }

        /**
         * Converts a string to an sfValueProperty using the string table.
         * 
         * @param   string value to convert.
         * @return  sfValueProperty the string as a property.
         */
        public static sfValueProperty FromString(string value)
        {
            if (SceneFusion.Get().Service.Session == null)
            {
                ksLog.Error(LOG_CHANNEL, "Cannot convert string to property; session is null");
                return new sfValueProperty(0);
            }
            uint id = SceneFusion.Get().Service.Session.GetStringTableId(value);
            return new sfValueProperty(id);
        }

        /**
         * Converts an sfProperty to a string using the string table.
         * 
         * @param   sfBaseProperty property to convert.
         * @return  string value of the property. Null if the property could not be converted to a string.
         */
        public static string ToString(sfBaseProperty property)
        {
            if (SceneFusion.Get().Service.Session == null)
            {
                ksLog.Error(LOG_CHANNEL, "Cannot convert property to string; session is null");
                return null;
            }
            if (property == null || property.Type != sfBaseProperty.Types.VALUE)
            {
                return null;
            }
            sfValueProperty value = (sfValueProperty)property;
            if (value.Value.Type == ksMultiType.Types.STRING)
            {
                return value.Value;
            }
            uint id = value.Value;
            return SceneFusion.Get().Service.Session.GetStringFromTable(id);
        }

        /**
         * Converts an AnimationCurve to an sfValueProperty.
         * 
         * @param   AnimationCurve value to convert.
         * @return  sfValueProperty the AnimationCurve as a property.
         */
        public static sfValueProperty FromAnimationCurve(AnimationCurve value)
        {
            byte[] data = new byte[Marshal.SizeOf(typeof(Keyframe)) * value.keys.Length + 1];
            int offset = 0;
            foreach (Keyframe frame in value.keys)
            {
                offset += Reactor.ksFixedDataWriter.WriteData(data, data.Length, offset, frame);
            }
            data[offset] = (byte)((byte)value.preWrapMode | ((byte)value.postWrapMode << 3));
            return new sfValueProperty(data);
        }

        /**
         * Converts an sfProperty to an AnimationCurve.
         * 
         * @param   sfBaseProperty property to convert.
         * @return  AnimationCurve value of the property.
         *          Null if the property could not be converted to an AnimationCurve.
         */
        public static AnimationCurve ToAnimationCurve(sfBaseProperty property)
        {
            if (property == null || property.Type != sfBaseProperty.Types.VALUE)
            {
                return null;
            }

            try
            {
                byte[] data = ((sfValueProperty)property).Value.ByteArray;
                int offset = 0;
                int sizeOfKeyframe = Marshal.SizeOf(typeof(Keyframe));
                int length = (data.Length - 1) / sizeOfKeyframe;
                Keyframe[] frames = new Keyframe[length];
                for (int i = 0; i < length; i++)
                {
                    frames[i] = ksFixedDataParser.ParseFromBytes<Keyframe>(data, offset);
                    offset += sizeOfKeyframe;
                }
                AnimationCurve animationCurve = new AnimationCurve(frames);
                byte preWrapMode = (byte)(data[offset] & 7);
                byte postWrapMode = (byte)((data[offset] >> 3) & 7);
                animationCurve.preWrapMode = (WrapMode)preWrapMode;
                animationCurve.postWrapMode = (WrapMode)postWrapMode;
                return animationCurve;
            }
            catch (Exception e)
            {
                ksLog.Error(
                    LOG_CHANNEL,
                    "Error syncing AnimationCurve. Your script source code may be out of sync.", e);
                return new AnimationCurve();
            }
        }

        /**
         * Converts a Gradient to an sfValueProperty.
         * 
         * @param   Gradient value to convert.
         * @return  sfValueProperty the Gradient as a property.
         */
        public static sfValueProperty FromGradient(Gradient value)
        {
            byte[] data = new byte[sizeof(int) +
                Marshal.SizeOf(typeof(GradientColorKey)) * value.colorKeys.Length +
                Marshal.SizeOf(typeof(GradientAlphaKey)) * value.alphaKeys.Length + 1];
            int offset = 0;
            offset += ksFixedDataWriter.WriteData(data, data.Length, offset, value.colorKeys.Length);
            foreach (GradientColorKey colourKey in value.colorKeys)
            {
                offset += ksFixedDataWriter.WriteData(data, data.Length, offset, colourKey);
            }
            foreach (GradientAlphaKey alphaKey in value.alphaKeys)
            {
                offset += ksFixedDataWriter.WriteData(data, data.Length, offset, alphaKey);
            }
            offset += ksFixedDataWriter.WriteData(data, data.Length, offset, (byte)value.mode);
            return new sfValueProperty(data);
        }

        /**
         * Converts an sfProperty to a Gradient.
         * 
         * @param   sfBaseProperty property to convert.
         * @return  Gradient value of the property.
         *          Null if the property could not be converted to an Gradient.
         */
        public static Gradient ToGradient(sfBaseProperty property)
        {
            if (property == null || property.Type != sfBaseProperty.Types.VALUE)
            {
                return null;
            }

            try
            {
                int sizeOfColorKey = Marshal.SizeOf(typeof(GradientColorKey));
                int sizeOfAlphaKey = Marshal.SizeOf(typeof(GradientAlphaKey));
                byte[] data = ((sfValueProperty)property).Value.ByteArray;
                Gradient gradient = new Gradient();
                int offset = 0;
                int colorKeyNum = ksFixedDataParser.ParseFromBytes<int>(data, offset);
                GradientColorKey[] colourKeys = new GradientColorKey[colorKeyNum];
                offset += sizeof(int);
                int alphaKeyNum = (data.Length - sizeof(int) - colorKeyNum * sizeOfColorKey - 1) / sizeOfAlphaKey;
                GradientAlphaKey[] alphaKeys = new GradientAlphaKey[alphaKeyNum];
                for (int i = 0; i < colourKeys.Length; i++)
                {
                    colourKeys[i] = ksFixedDataParser.ParseFromBytes<GradientColorKey>(data, offset);
                    offset += sizeOfColorKey;
                }
                for (int i = 0; i < alphaKeys.Length; i++)
                {
                    alphaKeys[i] = ksFixedDataParser.ParseFromBytes<GradientAlphaKey>(data, offset);
                    offset += sizeOfAlphaKey;
                }
                gradient.colorKeys = colourKeys;
                gradient.alphaKeys = alphaKeys;
                gradient.mode = (GradientMode)data[offset];
                return gradient;
            }
            catch (Exception e)
            {
                ksLog.Error(LOG_CHANNEL, "Error syncing Gradient. Your script source code may be out of sync.", e);
                return new Gradient();
            }
        }
    }
}
