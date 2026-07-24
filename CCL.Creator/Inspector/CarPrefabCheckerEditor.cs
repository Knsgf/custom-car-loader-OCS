using CCL.Creator.Utility;
using UnityEditor;
using UnityEngine;

namespace CCL.Creator.Inspector
{
    [CustomEditor(typeof(CarPrefabChecker))]
    internal class CarPrefabCheckerEditor : Editor
    {
        private bool _dirty = false;
        private CarPrefabChecker _comp = null!;
        private SerializedProperty _gauge = null!;

        private void OnEnable()
        {
            _dirty = true;
            _gauge = serializedObject.FindProperty(nameof(CarPrefabChecker.Gauge));
        }

        public override void OnInspectorGUI()
        {
            _comp = (CarPrefabChecker)target;

            EditorGUILayout.PropertyField(_gauge);
            serializedObject.ApplyModifiedProperties();

            EditorHelpers.DrawHeader("Bogies");
            CheckTransform(_comp.BogieF, "Front");
            CheckTransform(_comp.BogieR, "Rear");

            EditorHelpers.DrawHeader("Couplers");
            CheckTransform(_comp.CouplerF, "Front");
            CheckTransform(_comp.CouplerR, "Rear");

            EditorHelpers.DrawHeader("Optional");
            OptionalTransform(_comp.CoM, "Centre of Mass");

            EditorGUILayout.Space();

            if (GUILayout.Button("Recheck") || _dirty)
            {
                _dirty = false;
                _comp.OnValidate();
                AssetHelper.SaveAsset(_comp);
            }
        }

        private void CheckTransform(Transform? t, string name)
        {
            if (t == null)
            {
                EditorGUILayout.LabelField(name, "Missing", EditorHelpers.StyleWithTextColour(EditorHelpers.Colors.DELETE_ACTION, GUI.skin.label));
            }
            else
            {
                EditorGUILayout.LabelField(name, "Checked", EditorHelpers.StyleWithTextColour(EditorHelpers.Colors.CONFIRM_ACTION, GUI.skin.label));
            }
        }

        private void OptionalTransform(Transform? t, string name)
        {
            if (t == null)
            {
                EditorGUILayout.LabelField(name, "Skipped", EditorHelpers.StyleWithTextColour(Color.grey, GUI.skin.label));
            }
            else
            {
                EditorGUILayout.LabelField(name, "Checked", EditorHelpers.StyleWithTextColour(EditorHelpers.Colors.CONFIRM_ACTION, GUI.skin.label));
            }
        }
    }
}
