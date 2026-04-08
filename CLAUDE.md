## Goal

VRChat アバターの AFK アニメーションを非破壊で入れ替える NDMF プラグイン。

初心者でも簡単に AFK モーションを差し替えられることを目的とする。

## Current State

設計フェーズ。repo 初期セットアップ済み。

## 入力

- モード2（MVP）: AnimatorController を入力。Controller 内の AFK パラメータ使用ステートを走査し、ビルド時にアバターの Action Controller 内の AFK ステートをまるごと入れ替える

- モード1（次フェーズ）: AnimationClip を入力。既存 AFK ステートの Motion を差し替え（構造は維持）

## アーキテクチャ

- MonoBehaviour コンポーネント（アバタールートに設置）

- NDMF プラグイン（Generating フェーズ後半、MA MergeAnimator の後に動く）

- 非破壊: ビルド時にクローン上で処理。元の Animator は変更しない

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

2. Action Controller 内で `AFK` Bool を Transition 条件に使ってるステート群を走査・特定

3. AFK ステート群を削除（Transition 含む）

4. ユーザー入力 Controller 内の AFK ステート群を走査・特定

5. Action Controller に移植（State Behaviour 含む）

6. AFK への入口・出口 Transition を繋ぎ直す

## NDMF フェーズ

- Generating / AfterPlugin を基本候補

- MA MergeAnimator の後に動く → GoGoLoco 等サードパーティの結果を最終的に上書き

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

- フィールド: AnimatorController の ObjectField

- Inspector に走査結果プレビュー（後回し可）

## Current Blocker

なし。

## Rules

- 非破壊を最優先にし、ビルド時のクローン上でのみ処理する

- まず短い plan を出してから作業する

- commit / push は明示的な指示があるまで行わない

- Runtime ファイルの namespace は `Sebanne.AfkChanger`、Editor ファイルの namespace は `Sebanne.AfkChanger.Editor` に統一する

## 次フェーズ候補

- モード1: AnimationClip 差し替え（既存 AFK ステートの Motion のみ入れ替え）

- Action Layer 未設定時の挙動

- Dry Run / Inspector プレビュー

- MA Menu 連携
