// #define DEBUG_MESH_SYNC
#pragma warning disable 0168

using UnityEngine;
using System.Linq;
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine.ProBuilder;
using UnityEngine.Rendering;
using UObject = UnityEngine.Object;
using UnityEditor.SettingsManagement;
using UnityEditorInternal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

using PrefabStageUtility = UnityEditor.SceneManagement.PrefabStageUtility;

namespace UnityEditor.ProBuilder
{
    /// <summary>
    /// Contains utilities for working in the Unity Editor.
    /// </summary>
    public static class EditorUtility
    {
        const float k_DefaultNotificationDuration = 1f;
        static float s_NotificationTimer;
        static EditorWindow s_NotificationWindow;
        static bool s_IsNotificationDisplayed;

        [UserSetting("General", "Show Action Notifications", "Enable or disable notification popups when performing actions.")]
        static Pref<bool> s_ShowNotifications = new Pref<bool>("editor.showEditorNotifications", false);

        [UserSetting("Mesh Settings", "Static Editor Flags", "Default static flags to apply to new shapes.")]
        static Pref<StaticEditorFlags> s_StaticEditorFlags = new Pref<StaticEditorFlags>("mesh.defaultStaticEditorFlags", 0);

        [UserSetting("Mesh Settings", "Mesh Collider is Convex", "If a MeshCollider is set as the default collider component, this sets the convex setting.")]
        static Pref<bool> s_MeshColliderIsConvex = new Pref<bool>("mesh.meshColliderIsConvex", false);

        [UserSetting("Mesh Settings", "Snap New Shape To Grid", "When enabled, new shapes will snap to the closest point on grid.")]
        static Pref<bool> s_SnapNewShapesToGrid = new Pref<bool>("mesh.newShapesSnapToGrid", true);

        [UserSetting("Mesh Settings", "Shadow Casting Mode", "The default ShadowCastingMode to apply to MeshRenderer components.")]
        static Pref<ShadowCastingMode> s_ShadowCastingMode = new Pref<ShadowCastingMode>("mesh.shadowCastingMode", ShadowCastingMode.On);

        [UserSetting("Mesh Settings", "Collider Type", "What type of Collider to apply to new Shapes.")]
        static Pref<ColliderType> s_ColliderType = new Pref<ColliderType>("mesh.newShapeColliderType", ColliderType.MeshCollider);

        /// <summary>
        /// Raised when a new mesh has been created and initialized through ProBuilder.
        /// </summary>
        /// <remarks>
        /// This is only called when a user creates an object in the Editor using a ProBuilder menu item.
        /// </remarks>
        public static event Action<ProBuilderMesh> meshCreated = null;

        /// <summary>
        /// Set the selected render state for an object. In Unity 5.4 and lower, this just toggles wireframe on or off.
        /// </summary>
        /// <param name="renderer"></param>
        /// <param name="state"></param>
        internal static void SetSelectionRenderState(Renderer renderer, EditorSelectedRenderState state)
        {
            UnityEditor.EditorUtility.SetSelectedRenderState(renderer, state);
        }

        internal static void ShowNotification(ActionResult result)
        {
            ShowNotification(result.notification);
        }

        /// <summary>
        /// Show a timed (1 second) notification in the SceneView window.
        /// </summary>
        /// <param name="message">The text to display in the notification.</param>
        /// <seealso cref="RemoveNotification"/>
        internal static void ShowNotification(string message)
        {
            SceneView scnview = SceneView.lastActiveSceneView;
            if (scnview == null)
                scnview = EditorWindow.GetWindow<SceneView>();

            ShowNotification(scnview, message);
        }

        /// <inheritdoc cref="ShowNotification(string)"/>
        /// <param name="window">The EditorWindow to display this notification in.</param>
        /// <param name="message">The text to display in the notification.</param>
        /// <exception cref="ArgumentNullException">Window is null.</exception>
        internal static void ShowNotification(EditorWindow window, string message)
        {
            if (!s_ShowNotifications)
                return;

            if (window == null)
                throw new ArgumentNullException("window");

            window.ShowNotification(new GUIContent(message, ""));
            window.Repaint();

            if (EditorApplication.update != NotifUpdate)
                EditorApplication.update += NotifUpdate;

            s_NotificationTimer = Time.realtimeSinceStartup + k_DefaultNotificationDuration;
            s_NotificationWindow = window;
            s_IsNotificationDisplayed = true;
        }

        /// <summary>
        /// Remove any currently displaying notifications from an <see cref="UnityEditor.EditorWindow"/>.
        /// </summary>
        /// <param name="window">The EditorWindow from which all currently displayed notifications will be removed.</param>
        /// <exception cref="ArgumentNullException">Thrown if window is null.</exception>
        internal static void RemoveNotification(EditorWindow window)
        {
            if (window == null)
                throw new ArgumentNullException("window");

            EditorApplication.update -= NotifUpdate;

            window.RemoveNotification();
            window.Repaint();
        }

        static void NotifUpdate()
        {
            if (s_IsNotificationDisplayed && Time.realtimeSinceStartup > s_NotificationTimer)
            {
                s_IsNotificationDisplayed = false;
                RemoveNotification(s_NotificationWindow);
            }
        }

        internal static bool IsPrefab(ProBuilderMesh mesh)
        {
            return PrefabUtility.GetPrefabAssetType(mesh.gameObject) != PrefabAssetType.NotAPrefab;
        }

        /// <summary>
        /// Returns true if this object is a prefab instanced in the scene.
        /// </summary>
        /// <param name="go"></param>
        /// <returns></returns>
        internal static bool IsPrefabInstance(GameObject go)
        {
            var status = PrefabUtility.GetPrefabInstanceStatus(go);
            #pragma warning disable 612, 618
            return status == PrefabInstanceStatus.Connected || status == PrefabInstanceStatus.Disconnected;
            #pragma warning restore 612, 618
        }

        /**
         * Returns true if this object is a prefab in the Project view.
         */
        internal static bool IsPrefabAsset(GameObject go)
        {
            return PrefabUtility.IsPartOfPrefabAsset(go);
        }

        /**
         *  Returns true if Asset Store window is open, false otherwise.
         */
        internal static bool AssetStoreWindowIsOpen()
        {
            return Resources.FindObjectsOfTypeAll<EditorWindow>().Any(x => x.GetType().ToString().Contains("AssetStoreWindow"));
        }

        [Conditional("DEBUG_MESH_SYNC")]
        static void LogMeshSyncEvent(ProBuilderMesh mesh, MeshSyncState state, string msg)
        {
            Debug.Log($"{mesh} {mesh.GetInstanceID()} {state} {msg}");
        }

        /// <summary>
        /// Checks whether this object has a valid mesh reference, and the geometry is current. If the check fails,
        /// this function attempts to repair the sync state.
        /// </summary>
        /// <param name="mesh">The mesh component to test.</param>
        /// <seealso cref="ProBuilderMesh.meshSyncState"/>
        public static void SynchronizeWithMeshFilter(ProBuilderMesh mesh)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));

            mesh.EnsureMeshFilterIsAssigned();
            mesh.EnsureMeshColliderIsAssigned();
            MeshSyncState state = mesh.meshSyncState;
            bool meshesAreAssets = Experimental.meshesAreAssets;

            if (state != MeshSyncState.InSync)
            {
                if (state == MeshSyncState.Null || state == MeshSyncState.NeedsRebuild)
                {
                    LogMeshSyncEvent(mesh, state, "Rebuild");
                    mesh.Rebuild();
                    mesh.Optimize();
                }
                else
                {
                    // old mesh didn't exist, so this is probably a prefab being instanced
                    if (IsPrefabAsset(mesh.gameObject))
                    {
                        mesh.mesh.hideFlags = (HideFlags)(1 | 2 | 4 | 8);
                        LogMeshSyncEvent(mesh, state, "Prefab, set HideFlags");
                    }

                    mesh.Optimize();
                }
            }
            else
            {
                if (meshesAreAssets)
                    EditorMeshUtility.TryCacheMesh(mesh);
            }
        }

        /// <summary>
        /// Returns true if GameObject contains flags.
        /// </summary>
        /// <param name="go"></param>
        /// <param name="flags"></param>
        /// <returns></returns>
        internal static bool HasStaticFlag(this GameObject go, StaticEditorFlags flags)
        {
            return (GameObjectUtility.GetStaticEditorFlags(go) & flags) == flags;
        }

        /// <summary>
        /// Move a GameObject to the proper active root.
        /// Checks if a default parent exists, otherwise it adds the object as a root of the active scene, which can be a prefab stage.
        /// </summary>
        /// <param name="gameObject"></param>
        internal static void MoveToActiveRoot(GameObject gameObject)
        {
            var parent = SceneView.GetDefaultParentObjectIfSet();
            if (parent != null)
            {
                gameObject.transform.SetParent(parent);
                return;
            }
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            var activeScene = SceneManager.GetActiveScene();

            if (prefabStage != null)
            {
                if (gameObject.scene != prefabStage.scene)
                {
                    SceneManager.MoveGameObjectToScene(gameObject, prefabStage.scene);

                    // Prefabs cannot have multiple roots
                    gameObject.transform.SetParent(prefabStage.prefabContentsRoot.transform, true);
                }
            }
            else if(gameObject.scene != activeScene)
            {
                gameObject.transform.SetParent(null);
                SceneManager.MoveGameObjectToScene(gameObject, activeScene);
            }
        }

        /// <summary>
        /// Initialize this object with the various editor-only parameters, and invoke the object creation callback.
        /// </summary>
        /// <param name="pb"></param>
        internal static void InitObject(ProBuilderMesh pb)
        {
            MoveToActiveRoot(pb.gameObject);

            GameObjectUtility.EnsureUniqueNameForSibling(pb.gameObject);
            ScreenCenter(pb.gameObject);
            SnapInstantiatedObject(pb);

            ComponentUtility.MoveComponentRelativeToComponent(pb, pb.transform, false);
            pb.renderer.shadowCastingMode = s_ShadowCastingMode;
            pb.renderer.sharedMaterial = EditorMaterialUtility.GetUserMaterial();

            GameObjectUtility.SetStaticEditorFlags(pb.gameObject, s_StaticEditorFlags);

            switch (s_ColliderType.value)
            {
                case ColliderType.BoxCollider:
                    if(!pb.gameObject.TryGetComponent<BoxCollider>(out _))
                        Undo.AddComponent(pb.gameObject, typeof(BoxCollider));
                    break;

                case ColliderType.MeshCollider:
                    MeshCollider collider;
                    if (!pb.gameObject.TryGetComponent<MeshCollider>(out collider))
                        collider = Undo.AddComponent<MeshCollider>(pb.gameObject);
                    // This little dance is required to prevent the Prefab system from detecting an overridden property
                    // before ProBuilderMesh.RefreshCollisions has a chance to mark the MeshCollider.sharedMesh property
                    // as driven. "AddComponent<MeshCollider>" constructs the MeshCollider and simultaneously assigns
                    // the "m_Mesh" property, marking the property dirty. So we undo that change here, then assign the
                    // mesh through our own method.
                    collider.sharedMesh = null;
                    collider.convex = s_MeshColliderIsConvex;
                    pb.Refresh(RefreshMask.Collisions);
                    break;
            }

            pb.unwrapParameters = new UnwrapParameters(Lightmapping.s_UnwrapParameters);
            pb.Optimize();
            
            // PBLD-137 - if Resident Drawer is on, it will start throwing errors if submeshCount != materialCount
            if (pb.mesh.subMeshCount == 0)
                pb.renderer.sharedMaterial = null;

            if (meshCreated != null)
                meshCreated(pb);
        }

        // If s_SnapNewShapesToGrid is enabled, always snap to the grid size. If it is not, use the active snap  settings
        internal static void SnapInstantiatedObject(ProBuilderMesh mesh)
        {
            mesh.transform.position = ProBuilderSnapping.Snap(
                mesh.transform.position,
                s_SnapNewShapesToGrid
                    ? EditorSnapping.worldSnapMoveValue
                    : EditorSnapping.activeMoveSnapValue);
        }

        /**
         * Puts the selected gameObject at the pivot point of the SceneView camera.
         */
        internal static void ScreenCenter(GameObject _gameObject)
        {
            if (_gameObject == null)
                return;

            // If in the unity editor, attempt to center the object the sceneview or main camera, in that order
            _gameObject.transform.position = ScenePivot();

            Selection.activeObject = _gameObject;
        }

        /**
         * Gets the current SceneView's camera's pivot point.
         */
        internal static Vector3 ScenePivot()
        {
            return GetSceneView().pivot;
        }

        /**
         * Returns the last active SceneView window, or creates a new one if no last SceneView is found.
         */
        internal static SceneView GetSceneView()
        {
            return SceneView.lastActiveSceneView == null ? EditorWindow.GetWindow<SceneView>() : SceneView.lastActiveSceneView;
        }

        internal static bool IsUnix()
        {
            System.PlatformID platform = System.Environment.OSVersion.Platform;
            return platform == System.PlatformID.MacOSX ||
                platform == System.PlatformID.Unix ||
                (int)platform == 128;
        }

        /// <summary>
        /// Is this mode one of the mesh element modes (vertex, edge, face, texture).
        /// </summary>
        /// <param name="mode"></param>
        /// <returns></returns>
        internal static bool IsMeshElementMode(this SelectMode mode)
        {
            return mode.ContainsFlag(
                SelectMode.Vertex
                | SelectMode.Edge
                | SelectMode.Face
                | SelectMode.TextureEdge
                | SelectMode.TextureFace
                | SelectMode.TextureVertex
                );
        }

        internal static bool IsTextureMode(this SelectMode mode)
        {
            return mode.ContainsFlag(
                SelectMode.TextureEdge
                | SelectMode.TextureFace
                | SelectMode.TextureVertex
                );
        }

        internal static bool IsPositionMode(this SelectMode mode)
        {
            return mode.ContainsFlag(
                SelectMode.Edge
                | SelectMode.Face
                | SelectMode.Vertex
                );
        }

        internal static SelectMode GetPositionMode(this SelectMode mode)
        {
            if (mode.ContainsFlag(SelectMode.TextureFace))
                mode = (mode & ~SelectMode.TextureFace) | SelectMode.Face;

            if (mode.ContainsFlag(SelectMode.TextureEdge))
                mode = (mode & ~SelectMode.TextureEdge) | SelectMode.Edge;

            if (mode.ContainsFlag(SelectMode.TextureVertex))
                mode = (mode & ~SelectMode.TextureVertex) | SelectMode.Vertex;

            return mode;
        }

        internal static SelectMode GetTextureMode(this SelectMode mode)
        {
            if (mode.ContainsFlag(SelectMode.Face))
                mode = (mode & ~SelectMode.Face) | SelectMode.TextureFace;

            if (mode.ContainsFlag(SelectMode.Edge))
                mode = (mode & ~SelectMode.Edge) | SelectMode.TextureEdge;

            if (mode.ContainsFlag(SelectMode.Vertex))
                mode = (mode & ~SelectMode.Vertex) | SelectMode.TextureVertex;

            return mode;
        }

        /// <summary>
        /// Test if SelectMode contains any of the value bits.
        /// </summary>
        /// <remarks>
        /// HasFlag doesn't exist in .NET 3.5
        /// </remarks>
        /// <param name="target"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool ContainsFlag(this SelectMode target, SelectMode value)
        {
            return (target & value) != 0;
        }

        internal static bool IsDeveloperMode()
        {
            return EditorPrefs.GetBool("DeveloperMode", false);
        }

        /// <summary>
        /// Attaches a scene gizmo to the mesh in the Unity Editor.
        /// </summary>
        /// <param name="script">The Type representing the class of the item to attach the gizmo to (for example, the ProBuilderMesh).</param>
        /// <param name="enabled">True to render the gizmo (for example, while selected); false to not render it.</param>
        public static void SetGizmoIconEnabled(Type script, bool enabled)
        {
            var annotations = AnnotationUtility.GetAnnotations();
            var annotation = annotations.FirstOrDefault(x => x.scriptClass.Contains(script.Name));
            AnnotationUtility.SetIconEnabled(annotation.classID, annotation.scriptClass, enabled ? 1 : 0);
        }

        internal static T[] FindObjectsByType<T>() where T : UObject
        {
            return UObject.FindObjectsByType<T>(FindObjectsSortMode.None);
        }

        internal static string GetActiveSceneAssetsPath()
        {
            const string k_SavedMeshPath = "Assets/ProBuilder Data/Saved Meshes";
            var scene = SceneManager.GetActiveScene();
            var path = string.IsNullOrEmpty(scene.path)
                ? k_SavedMeshPath
                : $"{Path.GetDirectoryName(scene.path)}/{scene.name}/ProBuilder Meshes";
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
