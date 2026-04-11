## Goal

VRChat アバターの AFK アニメーションを非破壊で入れ替える NDMF プラグイン。

初心者でも簡単に AFK モーションを差し替えられることを目的とする。

## Current State

version 1.0.0 リリース準備フェーズ。MVP 実装完了・全パターン検証済み。

### 実装済み
- AfkStateScanner: AFK ステートを BFS 走査 + content/skeleton 分類
  - BFS 停止条件: entrySourceStates（逆流防止）と isExit のみ
  - HasAfkFalseCondition は停止条件に使わない（出口チェーンを切らないため）
- AfkStateReplacer: SubSM パターンと flat パターンの二系統
  - 入口: AnyState は使わない。ターゲットの元の入口遷移を付け替え
  - 出口: コンテンツ境界ベース（content 外への遷移を AFK BlendOut に付け替え）
  - TrackingControl / PlayableLayerControl をツール側で自動付与（入口: Animation+weight=1、出口: Tracking+weight=0）
  - AFK BlendOut ステートを生成し、default state への遷移を作成
- AfkChangerPlugin: NDMF Generating フェーズ、AfterPlugin("nadena.dev.modular-avatar")
- ControllerDumper: Tools メニュー / Assets 右クリック / Hierarchy 右クリックの3箇所起動。毎回新規ファイル生成（dump_{name}_{timestamp}.txt）
- AfkChangerEditor: Custom Editor。Avatar/Prefab ObjectField、スキャン結果表示、警告 HelpBox
- ActionControllerResolver: Descriptor → Action Layer → AnimatorController 取得ロジック共通化

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

## 入力

- モード2（MVP）: AnimatorController を入力。Controller 内の AFK パラメータ使用ステートを走査し、ビルド時にアバターの Action Controller 内の AFK ステートをまるごと入れ替える

- モード1（次フェーズ）: AnimationClip を入力。既存 AFK ステートの Motion を差し替え（構造は維持）

## アーキテクチャ

- MonoBehaviour コンポーネント（アバタールートに設置）

- NDMF プラグイン（Generating フェーズ、AfterPlugin("nadena.dev.modular-avatar") で MA の後に動く）

- 非破壊: ビルド時にクローン上で処理。元の Animator は変更しない

## ファイル構成

- `Runtime/AfkChangerComponent.cs` — MonoBehaviour。Source Controller フィールド
- `Editor/AfkChangerPlugin.cs` — NDMF Plugin。Generating フェーズで AFK 入れ替え実行
- `Editor/AfkChangerEditor.cs` — CustomEditor。Avatar/Prefab ObjectField、スキャン結果表示、警告 HelpBox
- `Editor/Core/AfkStateScanner.cs` — BFS 走査 + content/skeleton 分類
- `Editor/Core/AfkStateReplacer.cs` — SubSM / flat パターンの入れ替え処理
- `Editor/Core/AfkScanResult.cs` — スキャン結果データクラス
- `Editor/Core/ActionControllerResolver.cs` — Descriptor → Action Layer → AnimatorController 取得ロジック共通化
- `Editor/Core/AfkLog.cs` — ログユーティリティ（[AFK Changer] プレフィックス）
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

## UI

- アバタールートに付ける MonoBehaviour コンポーネント
- Avatar / Prefab の ObjectField（ドラッグで Action Controller を自動取得）
- Source Controller の ObjectField（直接指定も可能）
- スキャン結果を miniLabel で表示（パターン名 + ステート数）
- 警告は HelpBox（AFK 未検出、Avatar Descriptor なし等）

## Current Blocker

なし。

## Rules

- 非破壊を最優先にし、ビルド時のクローン上でのみ処理する

- まず短い plan を出してから作業する

- commit / push は明示的な指示があるまで行わない

- Runtime ファイルの namespace は `Sebanne.AfkChanger`、Editor ファイルの namespace は `Sebanne.AfkChanger.Editor` に統一する（Core / Debug サブ namespace あり）

## 次フェーズ候補

- モード1: AnimationClip 差し替え（既存 AFK ステートの Motion のみ入れ替え）

- GoGoLoco 等の多重ネスト SubSM パターン対応

- Action Layer 未設定時の挙動

- Dry Run / Inspector プレビュー

- MA Menu 連携

- ソースプリセット/スロット機能（保存してプルダウンから呼び出し）

- 混合パターン（target=SubSM / source=flat、またはその逆）のハンドリング改善

- ステート名マッチ依存の再接続改善（名前が異なるアバター構成への対応）
