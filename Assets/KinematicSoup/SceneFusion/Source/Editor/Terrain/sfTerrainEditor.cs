using System;
using System.Collections.Generic;
using System.Reflection;
using KS.Reactor;
using KS.SceneFusion;
using KS.SceneFusion2.Client;
using KS.Unity.Editor;
using UnityEditor;
using UnityEngine;
#if UNITY_2021_3_OR_NEWER
using UnityEngine.TerrainTools;
#else
using UnityEngine.Experimental.TerrainAPI;
#endif

namespace KS.SceneFusion2.Unity.Editor
{
    [CustomEditor(typeof(Terrain))]
    public class sfTerrainEditor : sfOverrideEditor
    {
        // Corresponds to a specific tool in a tool category
        public class Tools
        {
            public const int NONE = -1;
            public const int PAINT_HEIGHT = 0;
            public const int SET_HEIGHT = 1;
            public const int SMOOTH_HEIGHT = 2;
            public const int PAINT_TEXTURE = 3;
            public const int PLACE_TREE = 4;
            public const int PLACE_DETAIL = 5;
            public const int TERRAIN_SETTINGS = 6;
            public const int TRANSFORM = 7;
            public const int PAINT_HOLE = 8;
        }

        // Corresponds to one of the five tool category buttons in the terrain editor
        public class ToolCategories
        {
            public const int NONE = -1;
            public const int CREATE_NEIGHBOR = 0;
            public const int PAINT = 1;
            public const int PLACE_TREE = 2;
            public const int PAINT_DETAIL = 3;
            public const int TERRAIN_SETTINGS = 4;
            public const int TERRAIN_TOOL_COUNT = 5;
        }

        private static readonly List<string> m_heightPaintingTools = new List<string>()
        {
            "PaintHeightTool",
            "SetHeightTool",
            "SmoothHeightTool",
            "StampTool",
            "BridgeTool",
            "CloneBrushTool",
            "NoiseHeightTool",
            "TerraceErosion",
            "ContrastTool",
            "SharpenPeaksTool",
            "SlopeFlattenTool",
            "HydroErosionTool",
            "ThermalErosionTool",
            "WindErosionTool",
            "MeshStampTool"
        };

        private static readonly List<string> m_transformTools = new List<string>()
        {
            "PinchHeightTool",
            "SmudgeHeightTool",
            "TwistHeightTool"
        };

#if UNITY_2021_3_OR_NEWER
        private const string TERRAIN_TOOLS_NAMESPACE = "UnityEditor.TerrainTools";
#else
        private const string TERRAIN_TOOLS_NAMESPACE = "UnityEditor.Experimental.TerrainAPI";
#endif

        public static int ToolCategory
        {
            get { return m_toolCategory; }
        }
        private static int m_toolCategory = ToolCategories.NONE;

        public static int Tool
        {
            get { return m_tool; }
        }
        private static int m_tool = Tools.NONE;

        public static object ActiveTool
        {
            get { return m_activeTool; }
        }
        private static object m_activeTool = null;

        private float m_brushSize = 0f;
        private float m_brushRotation = 0f;

        private Terrain m_terrain = null;
        private static ksReflectionObject m_roActiveTerrainInspectorField = null;
        private static ksReflectionObject m_roActiveTerrainInspectorInstanceField = null;
        private static ksReflectionObject m_roTerrainColliderRaycastMethod = null;
        private static ksReflectionObject m_roCalcPixelRectFromBoundsMethod = null;
        private static ksReflectionObject m_roGetWindowTerrain = null;

        private ksReflectionObject m_roSelectedTool = null;
        private ksReflectionObject m_roGetActiveToolMethod = null;

        private EventType m_sceneGuiEventType = EventType.Used;
        private Vector2 m_sceneGuiMousePosition = ksVector2.Zero;
        private int m_sceneGuiMouseButton = -1;
        private bool m_sceneGuiShift = false;
        private bool m_sceneGuiCtrl = false;

        // The RectInt is the change area, and bool is true if shift was held at any point, meaning all detail layers
        // need to be checked for changes.
        private Dictionary<Terrain, KeyValuePair<RectInt, bool>> m_detailEdits =
            new Dictionary<Terrain, KeyValuePair<RectInt, bool>>();
        // Keys are true if we need to check for deleted trees, false for added trees only.
        private Dictionary<Terrain, bool> m_treeEdits = new Dictionary<Terrain, bool>();

        private const float TERRAIN_CHECK_INTERVAL = 0.1f;
        private float m_timeToPaletteCheck = 0;
        private bool m_isPlaceTreeWizardOpen = false;

        static sfTerrainEditor()
        {
        }

        /// <summary>
        /// Load the Unity Editor as the base editor when this editor is enabled.
        /// </summary>
        protected override void OnEnable()
        {
            LoadBaseEditor("TerrainInspector");

            // Set this editor instance as the active terrain inspector
            m_roSelectedTool = ReflectionEditor.GetProperty("selectedTool");
            if (m_roActiveTerrainInspectorField == null || m_roActiveTerrainInspectorInstanceField == null)
            {
                m_roActiveTerrainInspectorField = ReflectionEditor.GetField("s_activeTerrainInspector");
            }
            if (m_roActiveTerrainInspectorInstanceField == null)
            {
                m_roActiveTerrainInspectorInstanceField = ReflectionEditor.GetField("s_activeTerrainInspectorInstance");
            }
            if (m_roGetActiveToolMethod == null)
            {
                m_roGetActiveToolMethod = ReflectionEditor.GetMethod("GetActiveTool");
            }
            if (m_roTerrainColliderRaycastMethod == null)
            {
                m_roTerrainColliderRaycastMethod = new ksReflectionObject(typeof(TerrainCollider)).GetMethod(
                    "Raycast",
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    new Type[] { typeof(Ray), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(bool) }
                );
            }
            if (m_roCalcPixelRectFromBoundsMethod == null)
            {
                m_roCalcPixelRectFromBoundsMethod = new ksReflectionObject(typeof(TerrainPaintUtility)).GetMethod("CalcPixelRectFromBounds");
            }
            if (m_roGetWindowTerrain == null)
            {
                m_roGetWindowTerrain = new ksReflectionObject(typeof(EditorWindow).Assembly, "UnityEditor.TerrainWizard")
#if UNITY_2022_1_OR_NEWER
                    .GetField("terrain");
#else
                    .GetField("m_Terrain");
#endif
            }
            m_roActiveTerrainInspectorField.SetValue(BaseEditor.GetInstanceID());
            m_roActiveTerrainInspectorInstanceField.SetValue(BaseEditor);

            base.OnEnable();
            m_terrain = target as Terrain;

            m_isPlaceTreeWizardOpen = false;
            SceneFusion.Get().OnUpdate += CheckTerrain;
            SceneView.duringSceneGui += OnSceneGUI;
            SceneView.beforeSceneGui += BeforeSceneGUI;
        }

        protected override void OnDisable()
        {
            SceneFusion.Get().OnUpdate -= CheckTerrain;
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.beforeSceneGui -= BeforeSceneGUI;
            CheckTerrain(TERRAIN_CHECK_INTERVAL);

            if (IsEditable())
            {
                EditorWindow treePlaceWizard = GetPlaceTreeWizard();
                if (treePlaceWizard != null)
                {
                    treePlaceWizard.Close();
                }
            }
            m_isPlaceTreeWizardOpen = false;
            m_terrain = null;
            base.OnDisable();
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField("SF Override Editor");
            base.OnInspectorGUI();

            // Update tool
            int toolCategory = -1;
            int tool = -1;
            GetTool(out toolCategory, out tool);
            if (m_toolCategory != toolCategory || m_tool != tool)
            {
                m_toolCategory = toolCategory;
                m_tool = tool;
            }

            // Update brush
            float brushSize = 0f;
            float brushRotation = 0f;
            GetBrush(out brushSize, out brushRotation);
            if (m_brushSize != brushSize || m_brushRotation != brushRotation)
            {
                m_brushSize = brushSize;
                m_brushRotation = brushRotation;
            }
        }

        protected override void OnPreSceneGUI()
        {
            // Unity will throw null reference exceptions if there is no TerrainCollider
            if (m_terrain != null && m_terrain.gameObject.GetComponent<TerrainCollider>() != null)
            {
                base.OnPreSceneGUI();
            }
        }

        /**
         * Records the event type before it is changed to USED in the OnSceneGUI event hanlders.
         */
        private void BeforeSceneGUI(SceneView sceneView)
        {
            m_sceneGuiEventType = Event.current.type;
        }

        private bool m_checkedTerrain = false;
        protected override void OnSceneGUI(SceneView sceneView)
        {
            if (m_terrain == null || m_terrain.gameObject.GetComponent<TerrainCollider>() == null)
            {
                return;
            }

            m_sceneGuiMouseButton = Event.current.button;
            m_sceneGuiMousePosition = Event.current.mousePosition;
            m_sceneGuiShift = Event.current.shift;
            m_sceneGuiCtrl = Event.current.control;
            base.OnSceneGUI(sceneView);

            if (!IsEditable())
            {
                m_checkedTerrain = false;
                return;
            }

            if (!m_checkedTerrain)
            {
                m_checkedTerrain = true;
            }
            CheckTerrainEdits();
            m_sceneGuiMouseButton = -1;
        }

        // The terrain is editable if the current user holds the lock on the terrain
        private bool IsEditable()
        {
            sfObject obj = sfObjectMap.Get().GetSFObject(target as Terrain);
            return obj != null && obj.Session != null && obj.Session.LocalUser == obj.LockOwner;
        }

        /**
         * Periodically invokes a check for terrain data changes which do not have unity events. These include changes to:
         * terrain layers, detail prototypes, tree prototypes, and detail resolution 
         */
        public void CheckTerrain(float deltaTime)
        {
            if (m_terrain == null)
            {
                return;
            }
            m_timeToPaletteCheck -= deltaTime;
            if (m_timeToPaletteCheck <= 0f)
            {
                m_timeToPaletteCheck = TERRAIN_CHECK_INTERVAL;
                if (IsEditable())
                {
                    sfUnityEventDispatcher.Get().InvokeTerrainCheck(m_terrain);
                    SyncMassPlaceTrees();
                }
            }
        }

        private void CheckTerrainEdits()
        {
            Terrain terrain;
            // Trigger change events once painting finishes (mouse is released or leaves the window).
            if (m_sceneGuiEventType == EventType.MouseUp || m_sceneGuiEventType == EventType.MouseLeaveWindow)
            {
                switch (m_tool)
                {
                    case Tools.PLACE_DETAIL:
                    {
                        InvokeDetailEdit();
                        break;
                    }
                    case Tools.PLACE_TREE:
                    {
                        InvokeTreeEdit();
                        break;
                    }
                }
            }

            Vector2 relativePoint;
            if ((m_tool != Tools.PLACE_DETAIL && m_tool != Tools.PLACE_TREE) ||
                !TryGetEditTerrain(out terrain, out relativePoint))
            {
                return;
            }

            switch (m_tool)
            {
                case Tools.PLACE_DETAIL:
                {
                    TrackDetailEdit(terrain, relativePoint);
                    break;
                }
                case Tools.PLACE_TREE:
                {
                    TrackTreeEdit(terrain);
                    break;
                }
            }
        }

        /**
         * Tracks detail edit info to invoke a details change event with once the user stops editing details.
         * 
         * @param   Terrain terrain being edited
         * @param   Vector2 relativePoint on the terrain where the mouse is.
         */
        private void TrackDetailEdit(Terrain terrain, Vector2 relativePoint)
        {
            int width, height;
            GetTerrainDataDimensions(terrain.terrainData, out width, out height);
            RectInt bounds = GetBrushBounds(terrain, relativePoint, width, height);
            bounds.ClampToBounds(new RectInt(0, 0, width - 1, height - 1));
            // Change area and bool indicating if shift was held.
            KeyValuePair<RectInt, bool> editInfo;
            if (m_detailEdits.TryGetValue(terrain, out editInfo))
            {
                // Update the existing change area to include the new bounds.
                RectInt changeArea = new RectInt();
                changeArea.min = Vector2Int.Min(bounds.min, editInfo.Key.min);
                changeArea.max = Vector2Int.Max(bounds.max, editInfo.Key.max);
                m_detailEdits[terrain] = new KeyValuePair<RectInt, bool>(
                    changeArea, m_sceneGuiShift | editInfo.Value);
            }
            else
            {
                m_detailEdits[terrain] = new KeyValuePair<RectInt, bool>(bounds, m_sceneGuiShift);
            }
        }

        /**
         * Invokes detail change events from stored detail edits infos.
         */
        private void InvokeDetailEdit()
        {
            m_activeTool = m_roGetActiveToolMethod.InstanceInvoke(BaseEditor);
            foreach (KeyValuePair<Terrain, KeyValuePair<RectInt, bool>> edit in m_detailEdits)
            {
                Terrain terrain = edit.Key;
                RectInt changeArea = edit.Value.Key;
                int detailLayer = edit.Value.Value
                    ? -1 // -1 is used to indicate all layers are dirty
                    : (int)new ksReflectionObject(m_activeTool).GetProperty("selectedDetail").GetValue();
                sfUnityEventDispatcher.Get().InvokeOnTerrainDetailChange(terrain, changeArea, detailLayer);
            }
            m_detailEdits.Clear();
        }

        /**
         * Tracks tree edit info to invoke a details change event with once the user stops editing trees.
         * 
         * @param   Terrain terrain being edited
         */
        private void TrackTreeEdit(Terrain terrain)
        {
            bool removedTrees;
            if ((m_treeEdits.TryGetValue(terrain, out removedTrees) && removedTrees) ||
                terrain.terrainData == null)
            {
                return;
            }
            sfObject obj = sfObjectMap.Get().GetSFObject(terrain.terrainData);
            if (obj == null)
            {
                return;
            }
            sfListProperty treesProp = (sfListProperty)((sfDictionaryProperty)obj.Property)[sfProp.Trees];
            int numTrees = ((sfValueProperty)treesProp[0]).Value;
            if (numTrees > terrain.terrainData.treeInstanceCount)
            {
                m_treeEdits[terrain] = true;
            }
            else if (numTrees < terrain.terrainData.treeInstanceCount)
            {
                m_treeEdits[terrain] = true;
            }
        }

        /**
         * Invokes tree change events from stored tree edits infos.
         */
        private void InvokeTreeEdit()
        {
            foreach (KeyValuePair<Terrain, bool> edit in m_treeEdits)
            {
                sfUnityEventDispatcher.Get().InvokeOnTerrainTreeChange(edit.Key, edit.Value);
            }
            m_treeEdits.Clear();
        }

        // Get the terrain effected by a scene gui mouse event
        public bool TryGetEditTerrain(out Terrain terrain, out Vector2 relativePoint)
        {
            terrain = null;
            relativePoint = Vector2.zero;

            if ((m_sceneGuiEventType != EventType.MouseDown && m_sceneGuiEventType != EventType.MouseDrag) || m_sceneGuiMouseButton != 0)
            {
                return false;
            }
            float distance = float.MaxValue;
            Ray ray = HandleUtility.GUIPointToWorldRay(m_sceneGuiMousePosition);
            foreach (Terrain activeTerrain in Terrain.activeTerrains)
            {
                TerrainCollider collider = activeTerrain.GetComponent<TerrainCollider>();
                object[] parameters = new object[] { ray, null, distance, true };
                bool hit = (bool)m_roTerrainColliderRaycastMethod.InstanceInvoke(collider, parameters);
                RaycastHit hitInfo = (RaycastHit)parameters[1];

                if (hit && hitInfo.distance < distance)
                {
                    distance = hitInfo.distance;
                    terrain = activeTerrain;
                    relativePoint = hitInfo.textureCoord;
                }
            }
            return terrain != null;
        }

        private RectInt GetBrushBounds(Terrain terrain, Vector2 relativePoint, int width, int height)
        {
            RectInt bounds = CalculateEditBounds(terrain, width, height, relativePoint);
            if (m_activeTool != null)
            {
                ksReflectionObject commonUI = new ksReflectionObject(m_activeTool).GetProperty("commonUI", true);
                if (commonUI != ksReflectionObject.Void)
                {
                    ksReflectionObject scatterController = new ksReflectionObject(commonUI.GetValue()).GetField("m_BrushScatterController", true);
                    if (scatterController != ksReflectionObject.Void && scatterController.GetValue() != null)
                    {
                        float brushScatter = (float)scatterController.GetProperty("brushScatter").GetValue();
                        int xOffset = (int)(brushScatter * 0.5f * width);
                        int yOffset = (int)(brushScatter * 0.5f * height);
                        bounds.xMin -= xOffset;
                        bounds.xMax += xOffset;
                        bounds.yMin -= yOffset;
                        bounds.yMax += yOffset;
                    }
                }
            }
            return bounds;
        }

        private void GetTerrainDataDimensions(TerrainData terrainData, out int width, out int height)
        {
            width = -1;
            height = -1;
            switch (m_tool)
            {
                case Tools.PAINT_HEIGHT:
                case Tools.SET_HEIGHT:
                case Tools.SMOOTH_HEIGHT:
                {
                    width = terrainData.heightmapResolution;
                    height = terrainData.heightmapResolution;
                    break;
                }
                case Tools.PAINT_TEXTURE:
                {
                    width = terrainData.alphamapWidth;
                    height = terrainData.alphamapHeight;
                    break;
                }
                case Tools.PLACE_DETAIL:
                {
                    width = terrainData.detailWidth;
                    height = terrainData.detailHeight;
                    break;
                }
                case Tools.PAINT_HOLE:
                {
                    width = terrainData.holesResolution;
                    height = terrainData.holesResolution;
                    break;
                }
            }
        }

        private RectInt CalculateEditBounds(Terrain terrain, int width, int height, Vector2 relativePoint)
        {
            BrushTransform brushTransform = TerrainPaintUtility.CalculateBrushTransform(
                terrain,
                relativePoint,
                m_brushSize,
                m_brushRotation
            );

            return (RectInt)m_roCalcPixelRectFromBoundsMethod.Invoke(
                terrain,
                brushTransform.GetBrushXYBounds(),
                width,
                height,
                0,
                false
            );
        }

        /**
         * Gets selected tool index.
         * 
         * @return  int
         */
        private void GetTool(out int toolCategory, out int tool)
        {
            toolCategory = (int)m_roSelectedTool.GetValue();

            m_activeTool = null;
            switch (toolCategory)
            {
                case ToolCategories.CREATE_NEIGHBOR:
                {
                    m_activeTool = m_roGetActiveToolMethod.InstanceInvoke(BaseEditor);
                    tool = Tools.NONE;
                    break;
                }
                case ToolCategories.PAINT:
                {
                    m_activeTool = m_roGetActiveToolMethod.InstanceInvoke(BaseEditor);
                    string toolName = m_activeTool.ToString();
                    toolName = toolName.Substring((" (" + TERRAIN_TOOLS_NAMESPACE + ".").Length);
                    toolName = toolName.TrimEnd(')');

                    if (toolName == "PaintTextureTool")
                    {
                        tool = Tools.PAINT_TEXTURE;
                    }
                    else if (m_heightPaintingTools.Contains(toolName))
                    {
                        tool = Tools.PAINT_HEIGHT;
                    }
                    else if (m_transformTools.Contains(toolName))
                    {
                        tool = Tools.TRANSFORM;
                    }
                    else if (toolName == "PaintHolesTool")
                    {
                        tool = Tools.PAINT_HOLE;
                    }
                    else { 
                        tool = Tools.NONE;
                    }
                    break;
                }
                case ToolCategories.PLACE_TREE:
                {
                    m_activeTool = m_roGetActiveToolMethod.InstanceInvoke(BaseEditor);
                    tool = Tools.PLACE_TREE;
                    break;
                }
                case ToolCategories.PAINT_DETAIL:
                {
                    m_activeTool = m_roGetActiveToolMethod.InstanceInvoke(BaseEditor);
                    tool = Tools.PLACE_DETAIL;
                    break;
                }
                case ToolCategories.TERRAIN_SETTINGS:
                default:
                {
                    tool = Tools.NONE;
                    break;
                }
            }
        }

        /**
         * Get the current brush size and rotation.
         * 
         * @param   float [out] - Brush size
         * @param   float [out] - Brush rotation
         */
        private void GetBrush(out float brushSize, out float brushRotation)
        {
            brushSize = 0f;
            brushRotation = 0f;

            if (m_tool == Tools.PLACE_TREE)
            {
                brushSize = (float)new ksReflectionObject(m_activeTool).GetProperty("brushSize").GetValue();
            }
            else
            {
#if UNITY_2020_3_OR_NEWER
                brushSize = (float)ReflectionEditor.GetProperty("brushSize").GetValue();
#else
                brushSize = (float)ReflectionEditor.GetField("m_Size").GetValue();
#endif
                if (m_activeTool != null)
                {
                    ksReflectionObject commonUI = new ksReflectionObject(m_activeTool).GetProperty("commonUI", true);
                    if (commonUI != ksReflectionObject.Void)
                    {
                        brushSize = (float)commonUI.GetProperty("brushSize").GetValue();
                        brushRotation = (float)commonUI.GetProperty("brushRotation").GetValue();
                    }
                }
            }
        }

        /**
         * Detect if the mass place trees window was closed since the last time we checked. If it was closed
         * then resync all trees.
         */
        private void SyncMassPlaceTrees()
        {
            // If the PlaceTreeWizard window was closed then check if trees were added to the terrain.
            bool isTreePlaceWizardOpen = GetPlaceTreeWizard() != null;
            if (m_isPlaceTreeWizardOpen != isTreePlaceWizardOpen)
            {
                m_isPlaceTreeWizardOpen = isTreePlaceWizardOpen;
                if (!m_isPlaceTreeWizardOpen)
                {
                    sfUnityEventDispatcher.Get().InvokeOnTerrainTreeChange(m_terrain, true);
                }
            }
        }

        /**
         * Check if the current selected terrain has an active PlaceTreeWizard window open.
         * 
         * @return  EditorWindow - PlaceTreeWizard associated with the current terrain.
         */
        private EditorWindow GetPlaceTreeWizard()
        {
            UnityEngine.Object[] windows = ksEditorUtils.FindWindows("PlaceTreeWizard");
            if (windows.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < windows.Length; ++i)
            {
                if (windows[i] == null)
                {
                    continue;
                }

                Terrain component = m_roGetWindowTerrain.GetValue(windows[i]) as Terrain;
                if (component == target)
                {
                    return windows[i] as EditorWindow;
                }
            }
            return null;
        }
    }
}
