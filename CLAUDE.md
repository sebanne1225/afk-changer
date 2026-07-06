## Goal

VRChat アバターの AFK アニメーションを非破壊で管理する NDMF プラグイン。

初心者でも簡単に AFK モーションの入れ替え・削除・追加ができることを目的とする。

## Current State

2.0.1 公開済み（NDMF 非破壊プラグイン・コア実装完了・GitHub Release / VPM listing / VCC / BOOTH 反映済み）。ツール名は AFK Changer → AFK Manager 済み（package.json name は com.sebanne.afk-changer 維持）。
付け外し型 UI（単一 ReorderableList + VirtualRow モデル）で AFK スロットの入れ替え・削除・追加に対応。Action（構造ごと入れ替え）+ FX Clean（AFK ステート削除）+ MA Menu 生成（有効スロット >= 2 時）。GoGoLoco 等の多重ネスト SubSM は検出 + 警告 + スキップのみ（対応は次フェーズ）。デバッグ用 ControllerDumper は別リポ sebanne-dumpers へ移行済（公開スコープ外）。ActionControllerResolver は本体機能で使用継続中。
実装史・バグ修正・追加機能・クローズアウト調整の開発工程ログ・commit 考古学は git log が正本。設計経緯・検証済みパターンは下記各節および plans / sessions を参照。

### 実装済み

主要クラスと責務は下記「ファイル構成」、走査・入れ替えアルゴリズムと設計判断は下記「設計判断」「ビルド時の処理フロー」、fallback 排他制御・VirtualRow・D&D 等の実装知見は下記「技術知見」を参照（Scanner の BFS 走査 / Engine の Delete・Replace・Add / Context の Action・FX 差異吸収 / MenuGenerator / 2パス Plugin / VirtualRow Editor / ActionControllerResolver 共通化）。詳細実装は各 `.cs` を直読。

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

- FX: removeFxAfk で FX レイヤーの AFK パラメータ関連ステートを削除

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
- 旧 1.0.x の AfterPlugin("MA") in Generating は、MA が Generating にパスを持たないため実質無効だった
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

アバタールートに付ける MonoBehaviour（AfkManagerComponent）の CustomEditor。付け外し型 UI:
- Action セクション: 「元の AFK を含める」Toggle + 元 AFK 行と追加スロット行を統合した単一 ReorderableList（VirtualRow モデル）。★ バッジ / P3 空時ピッカー / ReorderableList 全体への D&D 受付を持つ。
- FX セクション: 「元の FX AFK を外す」チェックボックス。
- 表示ルール: 有効スロット数（= actionSources.Count + (originalAfkOrder >= 0 ? 1 : 0)）で MA 必須判定し Warning/Info を出し分け。★ バッジと fallback は有効スロット数 >= 2 で表示。

具体レイアウト・色値・条件式の実態は `Editor/AfkManagerEditor.cs` を直読。設計知見は下記「技術知見」の VirtualRow / D&D 各節を参照。

## Current Blocker

なし。

## Rules

- 非破壊を最優先にし、ビルド時のクローン上でのみ処理する
- Runtime ファイルの namespace は `Sebanne.AfkManager`、Editor ファイルの namespace は `Sebanne.AfkManager.Editor` に統一する（Core / Debug サブ namespace あり）

## 次フェーズ候補

knowledge-base `next-phase/tool-dev.md`「AFK Manager」節（後回しの正本）を参照。旧 Notion 次フェーズ候補 DB は凍結。
