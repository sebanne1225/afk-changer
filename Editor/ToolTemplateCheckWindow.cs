using UnityEditor;
using UnityEngine;

namespace Sebanne.ToolTemplate.Editor
{
    public sealed class ToolTemplateCheckWindow : EditorWindow
    {
        private const string WindowTitle = "Sebanne Tool Template";
        private const string PackageName = "com.sebanne.tool-template";
        private const string DisplayName = "Sebanne Tool Template";

        [MenuItem("Tools/Sebanne Tool Template/Template Check Window")]
        private static void Open()
        {
            var window = GetWindow<ToolTemplateCheckWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(420f, 220f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(WindowTitle, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("これはテンプレ確認用ウィンドウです。複製後に Unity で読み込み、Tools メニューから開けることを確認するための最小実装です。", MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("package名", PackageName);
            EditorGUILayout.LabelField("displayName", DisplayName);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("今後ここに機能を足していく想定です。", EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space();
            if (GUILayout.Button("確認ログを出す"))
            {
                Debug.Log("[Sebanne Tool Template] Template check window is working.");
            }
        }
    }
}
