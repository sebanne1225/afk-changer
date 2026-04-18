# Changelog

このファイルは `AFK Manager` の変更履歴を管理します。

## [2.0.0] - 2026-04-18

重要: ツール名変更（AFK Changer → AFK Manager）。package ID (`com.sebanne.afk-changer`) は v1.x との互換性のため維持しています。

### Added

- 複数スロット対応（Add / Replace / Delete を AfkOperationEngine に内部統合）
- Modular Avatar Expression Menu 自動生成（ビルド時、`AfkMenuGenerator`）
- FX Clean（FX レイヤーの AFK ステート削除、`removeFxAfk` フラグ）
- 元 AFK のスロット管理（「含める / 外す」「並び替え」「-1 = 削除」）
- fallback スロット（先頭スロット = メニュー OFF 時のデフォルト、1-based スロット値スキーム、`★` バッジ表示）
- MissingScript 検出警告 UI（v1.x → v2.0.0 マイグレーション補助、Inspector 冒頭に HelpBox + Ping ボタン + 再スキャン）
- GoGoLoco 等の多重ネスト SubStateMachine 検出 + 警告 + スキップ
- 単一 ReorderableList 型 Inspector UI（元 AFK 行 + 追加スロット行を同一リストに統合、Drop Area 全体化、空時ピッカー）
- メニューインストール先指定（`menuInstallTarget`、MA `AvMenuTreeViewWindow` リフレクションによるツリーブラウザボタン）
- プレハブリスト最適化（Action+FX コントローラペアでグルーピング、代表 + `"(N variants)"` 表示）

### Changed

- Inspector UI を単一 ReorderableList 型に刷新（旧: 付け外し型 UI）
- NDMF を 2 パス構成化（Pass 1 = `Generating` で MA 生成、Pass 2 = `Transforming.AfterPlugin("MA")` で実操作）
- namespace: `Sebanne.AfkChanger` → `Sebanne.AfkManager`
- Runtime asmdef: `Sebanne.AfkChanger` → `Sebanne.AfkManager`
- Editor asmdef: `Sebanne.AfkChanger.Editor` → `Sebanne.AfkManager.Editor`
- `AfkStateReplacer` + `AfkFxProcessor` を `AfkOperationEngine` に統合
- AFK スロット位置の表現: `removeActionAfk (bool)` → `originalAfkOrder (int)`（-1 = 削除、0+ = リスト内位置）
- 全ケース統一の 1-based スロット値スキームに変更（旧: `!removeActionAfk` 時 0-based / `removeActionAfk` 時 1-based の混在を解消）

### Fixed

- Slot 1 フォールバック排他制御（複数スロット + 元 AFK 削除時、VRChat メニュー全 OFF 状態で AFK が発火しない問題）
- `AfkMenuGenerator` の MenuInstaller + 親 SubMenu(Children) MenuItem 欠落による Expression Menu 生成失敗
- Add 操作の AnyState 再発火による無限ループ（per-state 入口遷移に統一）

### Notes

- package ID (`com.sebanne.afk-changer`) は互換性のため維持。release asset zip 名も `com.sebanne.afk-changer-<version>.zip` のまま
- v1.x で生成された `AfkChangerComponent` は MissingScript として残存する可能性あり。Inspector 冒頭の警告 UI から残存 GameObject を確認・削除可能
- 過去エントリ内の `AfkChangerComponent` 等の旧識別子は歴史事実として維持

## [1.0.2] - 2026-04-12

### Fixed

- AfkChangerComponent に IEditorOnly を実装し、VRChat SDK の validation 警告を解消

## [1.0.1] - 2026-04-12

### Fixed

- ビルド後に AfkChangerComponent がアバターに残り、VRChat SDK のバリデーションで警告される問題を修正

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
