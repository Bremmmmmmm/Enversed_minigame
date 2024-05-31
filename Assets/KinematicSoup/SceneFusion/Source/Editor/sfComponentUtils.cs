﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using KS.Unity.Editor;
using KS.Reactor;

namespace KS.SceneFusion2.Unity.Editor
{
    /**
     * Component utility functions.
     */
    public class sfComponentUtils
    {
        private static readonly char ASSEMBLY_SEPARATOR = '#';
        private static readonly string DEFAULT_ASSEMBLY = "Assembly-CSharp";
        private static readonly string DEFAULT_NAMESPACE = "UnityEngine.";
        private static readonly int DEFAULT_NAMESPACE_LENGTH = DEFAULT_NAMESPACE.Length;
        private static readonly string LOG_CHANNEL = typeof(sfComponentUtils).ToString();

        private static ksReflectionObject m_roAddComponent;

        /**
         * Static constructor
         */
        static sfComponentUtils()
        {
            m_roAddComponent = new ksReflectionObject(typeof(GameObject)).GetMethod("AddComponentInternal");
        }

        /**
         * Gets the name of a component. For Unity components the namespace is only included if it is not part of
         * UnityEngine. For Monobehaviours this is the assembly name + class name with name space seperated by a '#'.
         * If the assembly is Assembly-CSharp (the default for scripts) the assembly name is not included and the name
         * begins with a '#'.
         * 
         * @param   Component component to get name for.
         * @return  string name
         */
        public static string GetName(Component component)
        {
            if (component == null)
            {
                return null;
            }
            string name;
            if (component is MonoBehaviour)
            {
                sfMissingComponent missingComponent = component as sfMissingComponent;
                if (missingComponent != null)
                {
                    return missingComponent.Name;
                }

                Type type = component.GetType();
                string assemblyName = type.Assembly.FullName.Split(',')[0];
                if (assemblyName == DEFAULT_ASSEMBLY)
                {
                    assemblyName = "";
                }
                name = assemblyName + ASSEMBLY_SEPARATOR + type.ToString();
            }
            else
            {
                // Some Unity components such as Halo do not have a corresponding C# class and instead are of type
                // Behaviour, so we cannot get the type using GetType(). Instead we get it from ToString which puts the
                // type name in brackets at the end of the string.
                string str = component.ToString();
                int index = str.LastIndexOf("(");
                name = str.Substring(index + 1, str.Length - index - 2);
                if (name.StartsWith(DEFAULT_NAMESPACE))
                {
                    name = name.Substring(DEFAULT_NAMESPACE_LENGTH);
                }
            }
            return name;
        }

        /**
         * Adds a component to a game object by its type name. You can get the name using GetName.
         * 
         * @param   GameObject gameObject to add component to.
         * @param   string name of component to add.
         */
        public static Component AddComponent(GameObject gameObject, string name)
        {
            try
            {
                Component component = null;
                int index = name.LastIndexOf(ASSEMBLY_SEPARATOR);
                if (index == -1)
                {
                    // This is a Unity component. Add by name.
                    component = m_roAddComponent.InstanceInvoke(gameObject, name) as Component;
                }
                else
                {
                    // This is a Monobehaviour. Add by type.
                    Type type = GetMonobehaviourTypeByName(name);
                    if (type == null)
                    {
                        return null;
                    }
                    component = gameObject.AddComponent(type);
                }
                if (component != null)
                {
                    EditorUtility.SetDirty(component);
                }
                return component;
            }
            catch (Exception e)
            {
                LogAddComponentWarning(name, null, e);
                return null;
            }
        }

        /**
         * Converts a component name from GetName to a display name. If the name begins with a '#' it is removed, and
         * all other '#'s are changed to '.'s.
         * 
         * @param   string name
         * @return  string display name.
         */
        public static string GetDisplayName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }
            if (name.StartsWith('#'))
            {
                return name.Substring(1);
            }
            return name.Replace('#', '.');
        }

        /**
         * Gets the class name and assembly name from a component name, and returns true if the component is a
         * Monobehaviour. The assembly name will is null for non-Monobehaviours, and empty string for Monobehaviours in
         * the default assembly Assembly-CSharp. Use GetName to get the component name.
         * 
         * @param   name of the component returned from GetName.
         * @param   out string className including namespace.
         * @param   out assemblyName. Null for non-Monobehaviours and empty string for Monobehaviours in the default
         *          assembly.
         * @return  bool true if the component is a Monobehaviour.
         */
        public static bool GetClassAndAssemblyName(string name, out string className, out string assemblyName)
        {
            if (name == null)
            {
                className = null;
                assemblyName = null;
                return false;
            }
            int index = name.IndexOf(ASSEMBLY_SEPARATOR);
            if (index < 0)
            {
                className = name;
                assemblyName = null;
                return false;
            }
            className = name.Substring(index + 1);
            assemblyName = name.Substring(0, index);
            return true;
        }

        /**
         * Gets a monobehaviour's type by its name.
         * 
         * @param   string typeName - assembly name and class name separated by a '#'. If the assembly name is empty,
         *          uses the default assembly.
         * @return  Type
         */
        private static Type GetMonobehaviourTypeByName(string typeName)
        {
            int index = typeName.LastIndexOf(ASSEMBLY_SEPARATOR);
            string assemblyName = typeName.Substring(0, index);
            string className = typeName.Substring(index + 1);
            if (assemblyName == "")
            {
                assemblyName = DEFAULT_ASSEMBLY;
            }
            Assembly assembly = Assembly.Load(assemblyName);
            if (assembly == null)
            {
                LogAddComponentWarning(typeName, "Could not load assembly '" + assemblyName + "'.");
                return null;
            }

            Type type = assembly.GetType(className);
            if (type == null)
            {
                LogAddComponentWarning(typeName, "Could not find type '" + className + "'.");
                return null;
            }
            return type;
        }

        /**
         * Logs a warning that a component failed to load.
         * 
         * @param   string name of the component.
         * @param   string reason for the failure.
         * @param   Exception exception that caused the failure.
         */
        private static void LogAddComponentWarning(string name, string reason = null, Exception exception = null)
        {
            string message = "Error adding component '" + name + "'";
            if (reason != null)
            {
                message += ": " + reason;
            }
            else
            {
                message += ".";
            }
            if (exception == null)
            {
                ksLog.Warning(LOG_CHANNEL, message);
            }
            else
            {
                ksLog.Error(LOG_CHANNEL, message, exception);
            }
        }
    }
}
