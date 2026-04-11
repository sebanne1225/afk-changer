# Changelog

このファイルは `AFK Changer` の変更履歴を管理します。

## [1.0.0] - 2026-04-11

### Added

- AnimatorController 入力による AFK ステート入れ替え（Action Layer の AFK ステートを構造ごと入れ替え）
- flat パターン（ルート直下のステート）と SubStateMachine パターンの両方に対応
- TrackingControl / PlayableLayerControl 自動付与（入口: Animation + weight=1、出口: Tracking + weight=0）
- AFK BlendOut ステート自動生成（ウェイト復帰 + トラッキング復帰）
- Custom Editor（Avatar / Prefab ドラッグで Action Controller を自動取得、スキャン結果表示、警告 HelpBox）
- ActionControllerResolver（Descriptor → Action Controller 取得ロジック共通化）
- ControllerDumper（Tools / Assets 右クリック / Hierarchy 右クリックの 3 箇所起動、タイムスタンプ付き出力）
- NDMF Generating フェーズ、AfterPlugin("nadena.dev.modular-avatar") 対応

### Changed

- BFS 走査に entrySourceStates 境界を追加（過剰展開の防止）
- 入口再接続をターゲットの元の入口遷移付け替え方式に変更（AnyState 無限ループの防止）
- 出口再接続をコンテンツ境界ベースに変更（出口チェーンの content 漏れ防止）
- README / TOOL_INFO / package.json を公開向けに整備

### Notes

- Modular Avatar は optional（未導入でも動作）
- AnimationClip 差し替え・Dry Run は次フェーズ

## [0.1.0] - 2026-04-09

### Added

- 初期セットアップ。repo 構成・package.json・asmdef・NDMF Plugin スケルトン・コンポーネント雛形を追加
- AFK ステート走査ロジック実装（BFS、SubStateMachine 対応）
- AFK ステート入れ替えロジック実装（content/skeleton 分離、SubSM パターン/flat パターン対応）
- NDMF Generating フェーズでのビルドパス
