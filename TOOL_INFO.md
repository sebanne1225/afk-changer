# TOOL_INFO

このファイルは、`AFK Changer` の repo 補助文書です。README の代わりではなく、公開準備や listing 反映時に確認したい情報を短くまとめています。

## 基本情報

- ツール名: `AFK Changer`
- package名: `com.sebanne.afk-changer`
- 表示名: `AFK Changer`
- Runtime asmdef: `Sebanne.AfkChanger`
- Editor asmdef: `Sebanne.AfkChanger.Editor`
- 現在 version: `1.0.0`

## 公開メタ情報

- GitHub repo: `https://github.com/sebanne1225/afk-changer`
- changelogUrl: `https://github.com/sebanne1225/afk-changer/blob/main/CHANGELOG.md`
- listing repo: `https://github.com/sebanne1225/sebanne-listing`
- 参考 listing page (`VCC` 追加先ではない): `https://sebanne1225.github.io/sebanne-listing/`
- VCC に追加する URL: `https://sebanne1225.github.io/sebanne-listing/index.json`
- listing 側に追加する `githubRepos`: `sebanne1225/afk-changer`
- BOOTH 販売名: TBD

## 公開スコープの要約

- AnimatorController を入力し、Action Layer の AFK ステートを構造ごと入れ替える
- flat パターンと SubStateMachine パターンの両方に対応
- TrackingControl / PlayableLayerControl を自動付与（ソースに含まれていない場合も補完）
- AFK BlendOut ステートを自動生成（ウェイト復帰 + トラッキング復帰）
- デバッグ用 AnimatorController ダンプ機能（Tools / Assets / Hierarchy メニューから起動）

## 導入導線の前提

- 主導線は VCC / VPM
- Git URL / local package 導入は補助扱い
- Git URL 導入時は依存 package の解決を別途確認する
- Modular Avatar は optional（AfterPlugin 順序制御のみ、MA 未導入でも動作する）

## 既知の制限

- AnimationClip 単体での差し替えは未対応（次フェーズ候補）
- GoGoLoco 等の多重ネスト SubStateMachine は未対応
- Action Layer 未設定時の自動追加は未対応
- Dry Run / Inspector プレビューは未実装
