# TOOL_INFO

このファイルは、`AFK Manager` の repo 補助文書です。README の代わりではなく、公開準備や listing 反映時に確認したい情報を短くまとめています。

## 基本情報

- ツール名: `AFK Manager`
- package名: `com.sebanne.afk-changer`
- 表示名: `AFK Manager`
- Runtime asmdef: `Sebanne.AfkManager`
- Editor asmdef: `Sebanne.AfkManager.Editor`
- 現在 version: `2.0.0`

package ID (`com.sebanne.afk-changer`) は v1.x との互換性のため維持しています。ツール名・表示名・asmdef・リポ名は `AfkManager` 系へ変更しました。

## 公開メタ情報

- GitHub repo: `https://github.com/sebanne1225/afk-manager`
- changelogUrl: `https://github.com/sebanne1225/afk-manager/blob/main/CHANGELOG.md`
- listing repo: `https://github.com/sebanne1225/sebanne-listing`
- 参考 listing page (`VCC` 追加先ではない): `https://sebanne1225.github.io/sebanne-listing/`
- VCC に追加する URL: `https://sebanne1225.github.io/sebanne-listing/index.json`
- listing 側に追加する `githubRepos`: `sebanne1225/afk-manager`
- BOOTH 販売名: `AFK Manager`

## 公開スコープの要約

- Avatar / Prefab または AnimatorController を入力し、Action Layer の AFK ステートを複数スロットで Add / Replace / Delete できる
- flat パターンと SubStateMachine パターンの両方に対応
- TrackingControl / PlayableLayerControl を自動付与（ソースに含まれていない場合も補完）
- AFK BlendOut ステートを自動生成（ウェイト復帰 + トラッキング復帰）
- Modular Avatar Expression Menu を NDMF ビルド時に自動生成（複数スロット使用時）
- 元 AFK の位置制御（含める / 外す・リスト内で並び替え）
- FX レイヤーの AFK ステート削除（FX Clean、`removeFxAfk`）
- MissingScript 検出警告 UI（v1.x → v2.0.0 マイグレーション補助）
- GoGoLoco 等の多重ネスト SubStateMachine を検出して警告・スキップ
- デバッグ用 AnimatorController ダンプ機能（Tools / Assets / Hierarchy メニューから起動）

## 導入導線の前提

- 主導線は VCC / VPM
- Git URL / local package 導入は補助扱い
- Git URL 導入時は依存 package の解決を別途確認する
- Modular Avatar は複数スロット使用時に必須、単一スロット時は optional

## 既知の制限

- AnimationClip 単体での差し替えは未対応（次フェーズ候補）
- GoGoLoco 等の多重ネスト SubStateMachine は未対応（検出して警告・スキップ）
- Action Layer 未設定時の自動追加は未対応
- Dry Run / Inspector プレビューは未実装
- FX の「付ける」側（FX Replace / Object Toggle 構想）は `2.1+` で設計予定、`2.0.0` では FX Clean のみ対応
