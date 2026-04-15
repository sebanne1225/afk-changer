## Goal

VRChat アバターの AFK アニメーションを非破壊で管理する NDMF プラグイン。

初心者でも簡単に AFK モーションの入れ替え・削除・追加ができることを目的とする。

## Current State

2.0.0 開発中。ツール名変更: AFK Changer → AFK Manager。

Step 1（土台）完了: namespace 変更（Sebanne.AfkChanger → Sebanne.AfkManager）、ファイルリネーム、Component フィールド刷新（付け外し型 UI のデータモデル）、asmdef に MA Version Defines 追加。Plugin は最小適応（actionSources[0] 読み出し + removeFxAfk）。
Step 2（Inspector）完了: 付け外し型 UI 実装。Action セクション（ReorderableList でスロット一覧、スキャン結果表示、MA 必須判定 + Warning/Info）、FX セクション（スキャン結果 + 削除チェックボックス）。
Step 3（Engine）完了: AfkStateReplacer + AfkFxProcessor → AfkOperationEngine に統合。AfkOperationContext で Action/FX の差異を吸収（NeedsBlendOut / NeedsBehaviours）。Delete（standalone）+ Replace（content 入れ替え）の 2 操作。Plugin を ProcessAction / ProcessFx に分離。
Step 4（MenuGenerator + 2パス + Add）完了: AfkMenuGenerator 新規（MA Menu Item + Parameters 生成、#if HAS_MODULAR_AVATAR）。Engine に Add + AddSlotConditionToExistingEntries + EnsureSlotParameter 追加。Plugin を Generating + Transforming.AfterPlugin("MA") の 2 パス構成に移行。複数スロット対応（Delete→Add×N / AddSlotCondition+Add×N）。
Step 5（GoGoLoco）完了: Inspector に GoGoLoco 検出 + Warning（MA Merge Animator の Controller 名に "GoLoco" を含むか）。ビルド時に多重ネスト SubSM 検出 → エラーログ + Action 処理スキップ。Scanner に HasNestedSubStateMachines 追加。

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
- AfkManagerEditor: Custom Editor。付け外し型 UI（Action セクション + FX セクション）。ReorderableList でスロット一覧、スロットごとスキャンキャッシュ、MA 必須判定
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

- Action: AfkSlot のリストで指定。各スロットは Avatar/Prefab または Controller 入力。removeActionAfk で元 AFK の削除制御

- FX: removeFxAfk で FX レイヤーの AFK パラメータ関連ステートを削除。「付ける」側は 2.1+ で Object Toggle 構想と合わせて設計

- モード1（次フェーズ）: AnimationClip を入力。既存 AFK ステートの Motion を差し替え（構造は維持）

## アーキテクチャ

- MonoBehaviour コンポーネント（AfkManagerComponent。アバタールートに設置）

- NDMF プラグイン（2パス構成。Pass 1 = Generating で MA コンポーネント生成、Pass 2 = Transforming.AfterPlugin("MA") で実操作）

- 非破壊: ビルド時にクローン上で処理。元の Animator は変更しない

## ファイル構成

- `Runtime/AfkManagerComponent.cs` — MonoBehaviour + AfkSlot + AfkSourceInputType。removeActionAfk / actionSources / removeFxAfk
- `Editor/AfkManagerPlugin.cs` — NDMF Plugin。Generating フェーズで AFK 処理実行
- `Editor/AfkManagerEditor.cs` — CustomEditor。付け外し型 UI（Action / FX セクション、ReorderableList、スキャンキャッシュ）
- `Editor/Core/AfkStateScanner.cs` — BFS 走査 + content/skeleton 分類
- `Editor/Core/AfkOperationEngine.cs` — Delete / Replace / Add 操作 + SlotParameter 管理。旧 Replacer + FxProcessor 統合
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

## UI

- アバタールートに付ける MonoBehaviour コンポーネント（AfkManagerComponent）
- Action セクション（helpBox 枠）:
  - 「現在の AFK」miniLabel（アバターの Action Controller をスキャンして表示）
  - 「元の AFK を外す」チェックボックス（removeActionAfk）
  - 「付ける AFK」ReorderableList（ドラッグ並べ替え対応）
  - 各スロット: InputType Popup（Avatar/Prefab or Controller）+ ObjectField + スキャン結果 miniLabel
  - MA 必須構成の時のみスロット名フィールド表示
- FX セクション（helpBox 枠）:
  - 「現在の FX AFK」miniLabel + 「元の FX AFK を外す」チェックボックス
- 表示ルール:
  - removeActionAfk ON + ソース 0 → Warning「棒立ち」
  - MA 必須 + MA 未検出 → Warning「MA が必要です」
  - MA 必須 + MA 検出 → Info「Expression Menu で切り替え」
  - MA 必須判定: sourceCount >= 2 || (!removeAction && sourceCount >= 1)

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

### 2.0.0 コア

- AFK Manager 構想（追加・削除・入れ替えの3操作 × Action/FX 2レイヤー）← 設計済み
- GoGoLoco 検出 + 警告 + スキップ（2.0.0 では非対応。プレハブ置き型で MA マージ依存のため安全に止める）
- 混合パターン（target=SubSM / source=flat、またはその逆）のハンドリング改善
- ステート名マッチ依存の再接続改善（v1.0.x で大部分済み、残存箇所を GoGoLoco と合わせて対応）

### 2.1+

- ソースプリセット/スロット機能（保存してプルダウンから呼び出し）
- AFK 以外の MA 型エモートギミック → AFK 変換
- FX レイヤーの AFK ステート削除（選択制）
- FX Replace（Animator が分かる人向けの上級機能）
- AFK Object Toggle 構想（FX の AFK 操作を「AFK 中にオブジェクト ON/OFF」で直接指定する UI。FX の本命候補）
- アバタープレハブ自動リスト化（VRC Avatar Descriptor 走査 → プルダウン）
- AFK サムネ機能（ソース入力時にプレビュー表示）
- Action スロットごとの FX 紐づけ（各 AFK モーションに対応する FX 効果をセットで管理）
- Document 整備

### その他

- モード1: AnimationClip 差し替え（既存 AFK ステートの Motion のみ入れ替え）
- Action Layer 未設定時の挙動
- Dry Run / Inspector プレビュー
- MA Menu 連携
