## Goal

VRChat アバターの AFK アニメーションを非破壊で管理する NDMF プラグイン。

初心者でも簡単に AFK モーションの入れ替え・削除・追加ができることを目的とする。

## Current State

2.0.0 コア実装完了（Step 1-5 + バグ修正 2 件 + 追加機能 6 件 + クローズアウト調整 5 件すべて完了）。ツール名変更: AFK Changer → AFK Manager。
UI モデル転換 段階 1（データ層 + ビルド時処理層）完了。`originalAfkOrder` + effectiveSlots 前処理層 + ProcessAction effectiveSlots ベース再設計 + 1-based スロット値スキーム。
UI モデル転換 段階 2（Inspector UI 刷新）完了。単一 ReorderableList 型 UI（VirtualRow モデル + dispatch 型描画。元 AFK 行 + 追加スロット行を同一リストに統合）、「元の AFK を含める」Toggle 常時表示、元 AFK 行の 2 行レイアウト（固定ラベル「元の AFK」+ スキャン結果 miniLabel + メニュー名 PropertyField）、★ バッジ（effectiveSlotCount >= 2 の先頭行）、P3 空時ピッカー（「アバター一覧から選ぶ ▼」+ サブテキスト）、Drop Area 統合（ReorderableList 全体を D&D 受付 + ホバー時に薄青オーバーレイ）、fallback hint miniLabel 文言に ★ 付加。
残り: Step 6（local dir リネーム / CLAUDE.md 以外のドキュメント更新（README / TOOL_INFO / CHANGELOG / BOOTH_PACKAGE）/ sync-check → release → listing → BOOTH）。

Step 1（土台）完了: namespace 変更（Sebanne.AfkChanger → Sebanne.AfkManager）、ファイルリネーム、Component フィールド刷新（付け外し型 UI のデータモデル）、asmdef に MA Version Defines 追加。Plugin は最小適応（actionSources[0] 読み出し + removeFxAfk）。
Step 2（Inspector）完了: 付け外し型 UI 実装。Action セクション（ReorderableList でスロット一覧、スキャン結果表示、MA 必須判定 + Warning/Info）、FX セクション（スキャン結果 + 削除チェックボックス）。
Step 3（Engine）完了: AfkStateReplacer + AfkFxProcessor → AfkOperationEngine に統合。AfkOperationContext で Action/FX の差異を吸収（NeedsBlendOut / NeedsBehaviours）。Delete（standalone）+ Replace（content 入れ替え）の 2 操作。Plugin を ProcessAction / ProcessFx に分離。
Step 4（MenuGenerator + 2パス + Add）完了: AfkMenuGenerator 新規（MA Menu Item + Parameters 生成、#if HAS_MODULAR_AVATAR）。Engine に Add + AddSlotConditionToExistingEntries + EnsureSlotParameter 追加。Plugin を Generating + Transforming.AfterPlugin("MA") の 2 パス構成に移行。複数スロット対応（Delete→Add×N / AddSlotCondition+Add×N）。
Step 5（GoGoLoco）完了: Inspector に GoGoLoco 検出 + Warning（MA Merge Animator の Controller 名に "GoLoco" を含むか）。ビルド時に多重ネスト SubSM 検出 → エラーログ + Action 処理スキップ。Scanner に HasNestedSubStateMachines 追加。

バグ修正 2 件:
- AfkMenuGenerator の MenuInstaller + 親 SubMenu(Children) MenuItem 欠落修正（MA の MenuInstallHook が MenuInstaller を発見できず Expression Menu が生成されなかった）
- Add 操作の AnyState 再発火修正（per-state 入口遷移に統一。AnyState → AFK_Intro が AFK 中に他ステートから再発火して無限ループになる問題）

追加機能 6 件:
- メニューインストール先指定（ObjectField + MA AvMenuTreeViewWindow リフレクションによるツリーブラウザボタン）
- プレハブリスト最適化（Action+FX コントローラペアでグルーピング、代表 + "(N variants)" 表示）
- slot 0「元の AFK」メニュー項目生成（removeActionAfk=false 時）
- 先頭スロット = デフォルトスロット方式（並び替えで変更可能。EnsureSlotParameter の defaultInt は removeActionAfk に応じて 0 or 1）
- メニュー名カスタマイズ（originalAfkMenuName フィールド、slot 0 の表示名を変更可能）
- SubMenu(Children) 非対応 Warn ログ（ModularAvatarMenuInstallTarget が internal クラスのため、ツリーブラウザで SubMenu(Children) 選択時に警告）

クローズアウト調整 5 件:
- MissingScript 検出警告 UI（v1.x→v2.0.0 マイグレーション補助。OnInspectorGUI 冒頭に HelpBox + パス + Ping ボタン + 再スキャン）
- Slot 1 フォールバック排他制御（removeActionAfk=ON + sources>=2 で VRChat メニュー全 OFF 時の AFK 不発火バグ修正）
- defaultSlotIndex 削除（「並び替えで先頭 = デフォルト = ★ スロット」に一本化。UI とデータ構造を単純化）
- Inspector ★ バッジ + miniLabel 説明文（removeActionAfk=ON + sources>=2 時のみ。先頭スロットが fallback であることを視覚化）
- package.json name 戻し（com.sebanne.afk-changer 維持）+ displayName AFK Manager + version 2.0.0 + Plugin QualifiedName 修正

v1.0.2 公開済み（GitHub Release / VPM listing / VCC / BOOTH）。
BOOTH 商品ページ公開済み（説明文・タグ・zip・クレジット・サムネは後日）。

設計ドキュメントは Notion に記録済み（AFK Manager 構想ページ）。
Component/Inspector 詳細設計確定（付け外し型 UI）。
技術検証 2 点クリア: NDMF 2パス構成（Generating + Transforming.AfterPlugin）/ AnyState transition conditions 追加。

### 実装済み
- AfkStateScanner: AFK ステートを BFS 走査 + content/skeleton 分類
  - BFS 停止条件: entrySourceStates（逆流防止）と isExit のみ
  - HasAfkFalseCondition は停止条件に使わない（出口チェーンを切らないため）
- AfkOperationEngine: Delete + Replace + Add の 3 操作。AfkOperationContext で Action/FX 差異を吸収
  - Delete: 全 AFK ステート削除（standalone。BlendOut なし）
  - Replace: SubSM / flat パターンの content 入れ替え（skeleton 保持）
  - Add: ソース content を並列追加（ターゲットの元入口遷移を複製 + AfkManagerSlot 条件で入口分岐）
  - AddSlotConditionToExistingEntries: 既存 AFK 入口に AfkManagerSlot 条件を追加
  - EnsureSlotParameter: AfkManagerSlot Int パラメータ追加
  - 入口: ターゲットの元の入口遷移を複製して遷移先を付け替え + AfkManagerSlot 条件（Replace / Add 共通）
  - 出口: コンテンツ境界ベース（NeedsBlendOut で制御。複数 Add では共有 BlendOut）
  - TrackingControl / PlayableLayerControl 自動付与（NeedsBehaviours で制御）
- AfkOperationContext: ForAction / ForFxLayer ファクトリ。NeedsBlendOut / NeedsBehaviours / EntryBlendDuration を保持
- AfkMenuGenerator: MA Menu Item + Parameters をビルド時生成（#if HAS_MODULAR_AVATAR。Generating フェーズ）
- AfkManagerPlugin: NDMF 2パス（Pass 1: Generating で MA 生成、Pass 2: Transforming.AfterPlugin("MA") で実操作）。ProcessAction / ProcessFx で操作を分離。複数スロット対応
- ControllerDumper: Tools メニュー / Assets 右クリック / Hierarchy 右クリックの3箇所起動。毎回新規ファイル生成（dump_{name}_{timestamp}.txt）
- AfkManagerEditor: Custom Editor。単一 ReorderableList 型 UI（VirtualRow モデル + dispatch 型描画）。Action セクション（元 AFK 行統合、★ バッジ、P3 空時ピッカー、全体 D&D + ホバー視覚フィードバック）+ FX セクション。スロットごとスキャンキャッシュ、MA 必須判定
- ActionControllerResolver: Descriptor → 指定レイヤー → AnimatorController 取得ロジック共通化（AnimLayerType パラメータ化済み）
- AfkStateScanner.ScanFxLayers: FX コントローラーの全レイヤーを走査し、AFK ステートを持つレイヤーの結果をリストで返す

### 検証済みパターン
- flat × SubSM（りりか × Eku）✓
- SubSM × flat（Eku × りりか）✓
- flat × flat（SDK 標準 × りりか）✓
- SubSM × SubSM（Eku）✓

### 設計判断

#### content / skeleton 分離
- AFK SubStateMachine 内の全ステートを content として扱う（BFS で見つからない AFK_Outro も含む）
- Prepare AFK / BlendOut AFK / Restore Tracking AFK は skeleton として保持（アバター固有の State Behaviour を維持）
- 境界 Transition（skeleton↔content）を記録し、入れ替え後に再接続

#### AnyState と入口再接続
- AnyState → content 入口ステートは使わない。canSelf=False でも AFK_Intro 以外のステート（AFK, AFK_Loop 等）から再発火して無限ループになる
- ターゲットの元の入口遷移（WaitForActionOrAFK → Afk Init 等）の遷移先をソースの入口ステートに付け替える方式で統一

#### BFS 停止条件
- 停止条件は entrySourceStates（逆流防止）と isExit のみ
- HasAfkFalseCondition（AFK IfNot の遷移）は停止条件にしない。出口チェーン（BlendOut アニメーション等）が content から切り離されるため
- AFK IfNot の遷移先も content に含め、コンテンツ境界ベースの出口再接続で AFK BlendOut に付け替える

#### TrackingControl / PlayableLayerControl 自動付与
- ソースの content ステートに TrackingControl / PlayableLayerControl がない場合でもツール側で保証する
- 入口: VRCPlayableLayerControl(goalWeight=1) + VRCAnimatorTrackingControl(全部 Animation)
- 出口: AFK BlendOut ステートを生成し、VRCPlayableLayerControl(goalWeight=0) + VRCAnimatorTrackingControl(全部 Tracking) を付与
- 既にソースが持っている場合は二重付与しない

#### GoGoLoco 等の多重ネスト SubSM
- 現行は単層 SubSM + flat のみ対応。多重ネスト SubSM（GoGoLoco 等）は次フェーズ

#### FX レイヤー AFK ステート削除（Clean）
- FX Clean は遷移再接続不要（AFK ステートへの遷移を除去 + ステート削除のみ）
- 削除後にレイヤーが空になってもレイヤーは残す
- AFK パラメータも残す（Action 側で使うため）
- Scanner の ScanStateMachine() を抽出し、ScanFxLayers() で全レイヤーを走査
- 削除処理は AfkOperationEngine.Delete() に統合（旧 AfkFxProcessor.Clean / AfkStateReplacer.RemoveAfkStatesFlat を統合）
- FX Replace（入れ替え）は次フェーズ

## 入力

- Action: AfkSlot のリストで指定。各スロットは Avatar/Prefab または Controller 入力。originalAfkOrder で元 AFK の位置制御（-1 = 削除、0+ = リスト内位置）

- FX: removeFxAfk で FX レイヤーの AFK パラメータ関連ステートを削除。「付ける」側は 2.1+ で Object Toggle 構想と合わせて設計

- モード1（次フェーズ）: AnimationClip を入力。既存 AFK ステートの Motion を差し替え（構造は維持）

## アーキテクチャ

- MonoBehaviour コンポーネント（AfkManagerComponent。アバタールートに設置）

- NDMF プラグイン（2パス構成。Pass 1 = Generating で MA コンポーネント生成、Pass 2 = Transforming.AfterPlugin("MA") で実操作）

- 非破壊: ビルド時にクローン上で処理。元の Animator は変更しない

## ファイル構成

- `Runtime/AfkManagerComponent.cs` — MonoBehaviour + AfkSlot + AfkSourceInputType。originalAfkOrder / actionSources / menuInstallTarget / originalAfkMenuName / removeFxAfk
- `Editor/AfkManagerPlugin.cs` — NDMF Plugin。Generating フェーズで AFK 処理実行
- `Editor/AfkManagerEditor.cs` — CustomEditor。付け外し型 UI（Action / FX セクション、ReorderableList、スキャンキャッシュ）
- `Editor/Core/AfkStateScanner.cs` — BFS 走査 + content/skeleton 分類
- `Editor/Core/AfkOperationEngine.cs` — Delete / Replace / Add 操作 + SlotParameter 管理。旧 Replacer + FxProcessor 統合
- `Editor/Core/EffectiveSlot.cs` — originalAfkOrder + actionSources から合成した effectiveSlots の型 + Build 静的メソッド
- `Editor/Core/AfkScanResult.cs` — スキャン結果データクラス
- `Editor/Core/ActionControllerResolver.cs` — Descriptor → 指定レイヤー → AnimatorController 取得ロジック共通化（AnimLayerType パラメータ化）
- `Editor/Core/AfkOperationContext.cs` — 操作コンテキスト（ForAction / ForFxLayer ファクトリ）
- `Editor/Core/AfkMenuGenerator.cs` — MA Menu Item + Parameters 生成（#if HAS_MODULAR_AVATAR）
- `Editor/Core/AfkLog.cs` — ログユーティリティ（[AFK Manager] プレフィックス）
- `Editor/Debug/ControllerDumper.cs` — AnimatorController 構造ダンプ（Tools / Assets / Hierarchy メニュー）

## AFK ステート構造の実態

VRChat の AFK は Action Layer で動作。`AFK` Bool パラメータ（VRChat ビルトイン）を Transition 条件に使う。

バリエーション:

- SDK 標準（3ステート）: Afk Init → AFK → BlendOut

- VRSuya テンプレ（4ステート）: Prepare AFK → AFK_Intro → AFK/AFK_Loop → AFK_Outro

- BOOTH 汎用（3ステート × パラメータ分岐）: Init → Loop → Out（+ 追加パラメータで上下分岐）

- 最小構成: 1ステートのみ

共通点: どのパターンも `AFK` Bool の Transition で出入り。

## ビルド時の処理フロー

1. アバターの Action Controller を取得（VRC Avatar Descriptor → Playable Layers → Action）

2. ターゲット / ソース両方を走査:
   - BFS で AFK パラメータ関連ステートを検出
   - 停止条件: entrySourceStates（AFK If 遷移の発信元）と isExit のみ
   - SubStateMachine 内のステートを content（入れ替え対象）、root SM のステートを skeleton（保持）に分類
   - AFK SubStateMachine 内の全ステートを content に含める（BFS で未検出のステートも）

3. SubSM パターン（content が SubSM 内にある場合）:
   - ターゲットの content SubStateMachine を丸ごと削除
   - ソースの content ステートをターゲット root SM にコピー（State Behaviour 含む）
   - skeleton → content の入口 Transition を再接続（name-match）
   - 入口ステートに TrackingControl + PlayableLayerControl を自動付与
   - AFK BlendOut ステートを生成し、content 外への出口遷移を BlendOut に付け替え

4. flat パターン（全ステートが root SM にある場合）:
   - ターゲットの全 AFK ステートを削除
   - ソースの AFK ステートをコピー
   - ターゲットの元の入口遷移（WaitForActionOrAFK 等）をソースの入口ステートに付け替え
   - 入口ステートに TrackingControl + PlayableLayerControl を自動付与
   - AFK BlendOut ステートを生成し、content 外への出口遷移を BlendOut に付け替え

5. FX Clean（removeFxAfk が true の場合、Action 処理の後に実行）:
   - アバターの FX Controller を取得（VRC Avatar Descriptor → Playable Layers → FX）
   - ScanFxLayers で全レイヤーを走査し、AFK ステートを持つレイヤーを特定
   - 各レイヤーごとに AFK ステート + 遷移を削除（遷移再接続なし）

## 技術知見

### Action Layer の特性

- Action Layer はデフォルトでウェイト 0

- AFK ステートに入る時、VRC Playable Layer Control で ウェイトを 1 に上げ、終了時に 0 に戻す

- VRC Animator Tracking Control でトラッキングを無効化

- VRC Animator Layer Control で FX レイヤーのウェイトも制御する場合がある

### AFK パラメータ

- VRChat ビルトイン Bool。Expression Parameters に追加不要

- HMD を外す、End キー、システムメニューでトリガー

### 既存ツールとの差別化

- Avatar Motion Changer（tmyt 氏）: AnimationClip の差し替えのみ。ステート構造入れ替え非対応。汎用ツール

- このツール: AFK 特化。ステート構造ごと入れ替え可能。初心者向け UI・ドキュメント

### NDMF 2パス構成

- Generating + Transforming.AfterPlugin("MA") の構成で成立する
- Pass 1（Generating）: MA コンポーネント生成（Add 操作用の ModularAvatarMenuItem 等）
- MA が Transforming フェーズで処理（MenuInstall / ParameterAssigner 等）
- Pass 2（Transforming.AfterPlugin("MA")）: Action/FX の実操作を実行
- 現行 1.0.x の AfterPlugin("MA") in Generating は、MA が Generating にパスを持たないため実質無効
- NDMF の constraint はフェーズローカル（クロスフェーズ制約は禁止。PluginResolver が例外を投げる）

### AnyState transition conditions

- AddCondition() は AnyState transition に対して直接呼べる。Unity API の制限なし
- 推奨方式は削除→再作成（CopyTransitionSettings で設定引き継ぎ）。現行コードパターンと一致
- sm.anyStateTransitions は配列コピーを返すが、各要素は Unity Object への参照（変更は永続化される）

### MA ParameterAssigner

- ModularAvatarMenuItem を Generating フェーズで生成すれば、Expression Parameters への登録は MA の ParameterAssigner が自動で行う（手動登録不要）
- MA の MenuInstallPluginPass が Transforming フェーズで Menu Item を Expression Menu に反映

### GoGoLoco Action Controller 構造

- 最大 3 階層ネスト SubSM。Root SM → SubSM:AFK → 内部に複数 SubSM（AFK Init / Blend Out AFK / Other 等）
- Blend Out AFK だけで 10 ステート × 33 Entry 遷移
- 現行 Scanner/Replacer は 1 階層 SubSM + flat のみ対応。2.0.0 では検出 + 警告 + スキップ

### effectiveSlots[0] フォールバック排他制御

- 有効スロット数 >= 2 の時、Expression Parameter default=1（1-based スロット値スキーム） + effectiveSlots[0] の Transition 条件を `AFK=true AND AfkManagerSlot <= 1`（AnimatorConditionMode.Less, slotValue+1）にする
- VRChat メニュー全 OFF 時（param=0）も effectiveSlots[0] が発火する
- value=0 に対応するメニュー項目がない構造的問題の解決パターン
- effectiveSlots[0] が IsOriginal=true なら `AddSlotConditionToExistingEntries(ctx, 1, isFallbackSlot=true)` で既存 entries に付与、IsOriginal=false なら `Add(..., slotValue=1, isFallbackSlot=true)` で新規 entries に付与
- effectiveSlots[i>=1] は従来通り `Equals (i+1)`

### MissingScript 検出 UI パターン

- `GetComponentsInChildren<Transform>(true)` で全 child 走査 + 各 GameObject の `GetComponents<Component>()` で null 要素を検出する方式
- OnEnable で初回スキャン + 「再スキャン」ボタンで手動更新（毎フレーム実行しない）
- Inspector の OnInspectorGUI 冒頭で描画
- パス表示は AnimationUtility.CalculateTransformPath、Ping は EditorGUIUtility.PingObject + Selection.activeGameObject

### VirtualRow + ReorderableList 非 SerializedProperty バインド

- ReorderableList を SerializedProperty バインドではなく自前の `List<VirtualRow>` にバインドすると、元 AFK + actionSources の合成リストを単一リストとして扱える
- VirtualRow: `{ bool IsOriginal; int ActionSourceIndex }`（IsOriginal=true のとき ActionSourceIndex=-1）
- `RebuildVirtualRows()`: `_virtualRows.Clear()` 後、actionSources を先に Add、`originalAfkOrder >= 0` ならクランプ位置に Original を Insert。OnInspectorGUI 冒頭 + 各ミューテーション callback 後に呼ぶ（idempotent）
- Undo: `Undo.RecordObject(target, "...") + EditorUtility.SetDirty(target) + serializedObject.Update() + RebuildVirtualRows()` の idiom で全ミューテーション（Add/Remove/Reorder/Picker/Drop/Toggle）を統一
- PropertyField 系（メニュー名・InputType・ObjectField・slotName）は SerializedProperty 経由で Unity 自動 Undo

### ReorderableList 全体への D&D 統合

- `_slotList.DoLayoutList()` 直後に `GUILayoutUtility.GetLastRect()` で全体 Rect（ヘッダー + 要素領域 + フッター全て含む）取得 → `HandleDragDropInRect(listRect)` で枠内のどこにドロップしても D&D 受付
- ボタンクリック（MouseDown 型）と D&D（DragUpdated/DragPerform 型）はイベント種別が異なるので衝突しない。P3 ボタン領域内へドロップ時も D&D が優先される（`HandleDragDropInRect` が `DragPerform` で `evt.Use()` するため）

### D&D ホバー視覚フィードバック

- DragUpdated/DragPerform で `_isDragHovering = listRect.Contains(mouse) && HasValidDragObjects()`、DragExited/MouseUp でクリア
- Repaint イベント時に `EditorGUI.DrawRect(listRect, new Color(0.5f, 0.8f, 1f, 0.15f))` で半透明オーバーレイ
- Unity は DragUpdated 後に自動 Repaint トリガーするのでホバー状態変化は即座に画面反映

### ReorderableList 空要素領域の高さ確保

- `_slotList.elementHeight = 60f` で `drawNoneElementCallback` の描画領域が 60px 確保される
- `elementHeightCallback` 設定時は要素ある時の高さには影響しない（elementHeight は空時のみ効く）
- 空時に P3 ボタン + サブテキストのような拡張描画を入れる時に使える

## UI

- アバタールートに付ける MonoBehaviour コンポーネント（AfkManagerComponent）
- Action セクション（helpBox 枠）:
  - 「元の AFK を含める」Toggle（ReorderableList の上、常時表示。ON = `originalAfkOrder >= 0` / OFF = `-1`。OFF → ON 時は `originalAfkOrder = 0`）
  - 「AFK スロット」単一 ReorderableList（元 AFK 行 + 追加スロット行を統合。VirtualRow モデルで合成）
    - 元 AFK 行の 2 行レイアウト: Row 1 = ドラッグハンドル + ★バッジ（条件付き） + 固定ラベル「元の AFK」(bold) + スキャン結果 miniLabel（アバターの Action Controller から取得） / Row 2 = MA 必須時のみ メニュー名 PropertyField（`originalAfkMenuName`）
    - 追加スロット行の 2 行レイアウト: Row 1 = ドラッグハンドル + ★バッジ（条件付き） + InputType Popup（Avatar/Prefab or Controller） + ObjectField + ▼ ミニボタン（AvatarPrefab 時のみ、プレハブリストピッカー起動） + スキャン結果 miniLabel / Row 2 = MA 必須時のみ メニュー名 PropertyField（`slotName`）
  - ★ バッジ条件: 先頭行のみ表示、`effectiveSlotCount >= 2` の時（元 AFK 行が先頭でも同条件）
  - fallback hint miniLabel: `effectiveSlotCount >= 2` の時に「★ 先頭スロットがメニュー OFF 時のデフォルトになります」を ReorderableList 下に表示
  - P3 空時ピッカー: `_virtualRows.Count == 0` の時、`drawNoneElementCallback` で「アバター一覧から選ぶ ▼」ボタン + サブテキスト「または Avatar / Controller をここにドラッグ」を描画
  - Drop Area: ReorderableList 全体 Rect（ヘッダー + 要素領域 + フッター全て含む）を D&D 受付。ホバー時に薄青オーバーレイ（`Color(0.5f, 0.8f, 1f, 0.15f)`）
- FX セクション（helpBox 枠）:
  - 「現在の FX AFK」miniLabel + 「元の FX AFK を外す」チェックボックス
- 表示ルール:
  - originalAfkOrder == -1 + ソース 0 → Warning「棒立ち」
  - MA 必須 + MA 未検出 → Warning「MA が必要です」
  - MA 必須 + MA 検出 → Info「Expression Menu で切り替え」
  - MA 必須判定: 有効スロット数 >= 2（= actionSources.Count + (originalAfkOrder >= 0 ? 1 : 0) >= 2）

## Current Blocker

なし。

## Rules

- 非破壊を最優先にし、ビルド時のクローン上でのみ処理する

- まず短い plan を出してから作業する

- commit / push は明示的な指示があるまで行わない

- Runtime ファイルの namespace は `Sebanne.AfkManager`、Editor ファイルの namespace は `Sebanne.AfkManager.Editor` に統一する（Core / Debug サブ namespace あり）

### コード変更の原則
- 変更した全ての行が、依頼内容に直接たどれること（判定基準）
- 元からあった死にコードはこのプロジェクトでは報告だけして消さない
- 依頼を成立させるための連鎖変更（Component→Editor→Plugin 等）は「直接関係する」に含む
- 事前に plan で承認されたリファクタ・構造変更はこの原則の対象外

## 次フェーズ候補

Notion 次フェーズ候補 DB（リポ=AFK Changer）を参照。
