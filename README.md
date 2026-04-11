# AFK Changer

VRChat アバターの AFK アニメーションを非破壊で入れ替える NDMF プラグインです。

差し替え元の AnimatorController を設定してアップロードするだけで、ビルド時に Action Layer の AFK ステートを自動で入れ替えます。初心者でも簡単に AFK モーションを差し替えられることを目的としています。

## 何ができるか

- AFK モーションの AnimatorController をアバターに設定するだけで、ビルド時に Action Layer の AFK ステートを自動で入れ替えます
- flat パターン（ルート直下のステート）と SubStateMachine パターンの両方に対応しています
- TrackingControl / PlayableLayerControl を自動で付与します（ソースに含まれていない場合も補完）
- AFK BlendOut ステートを自動生成し、AFK 終了時のウェイト復帰とトラッキング復帰を処理します
- 元のアバターには直接変更を加えない非破壊設計です（NDMF）

## 対応環境

- Unity `2022.3`
- VRChat SDK（Avatars）
- NDMF
- VCC / VPM ベースの VRChat プロジェクトを推奨します

## VCC / VPM 導入方法

### 推奨: VCC / VPM から導入

1. VCC に追加する URL として `https://sebanne1225.github.io/sebanne-listing/index.json` を追加します。
2. package 一覧から `AFK Changer` (`com.sebanne.afk-changer`) を追加します。
3. Unity を開き、依存 package が解決されていることを確認します。

参考ページ (`VCC` 追加先ではありません): `https://sebanne1225.github.io/sebanne-listing/`

### 補助: Git URL / Release zip から導入

- repo: `https://github.com/sebanne1225/afk-changer`
- Git URL や local package での導入は、開発確認や手動検証向けの補助導線です
- GitHub Release の zip も補助導線として使えます。`com.sebanne.afk-changer-1.0.0.zip` を展開すると、直下に `package.json` が見える package 構成です
- これらの補助導線では、`VRChat Avatars` と `NDMF` の依存解決を自分で確認する必要があります

## 使い方

1. アバタールートの Inspector で `Add Component` から `AFK Changer` を追加します。
2. 差し替え元の AFK モーションが入った Avatar または Prefab を ObjectField にドラッグします（Action Controller が自動で取得されます）。または、AnimatorController を直接 `Source Controller` に指定することもできます。
3. アップロードします。NDMF がビルド時に自動で AFK ステートを入れ替えます。

Modular Avatar を導入している場合、MA の処理の後に AFK Changer が動作します（明示的な設定は不要です）。

## 制限事項

- 現在は AnimatorController 入力のみ対応しています。AnimationClip 単体での差し替えは未対応です
- GoGoLoco 等の多重ネスト SubStateMachine パターンは未対応です
- Action Layer が未設定のアバターへの自動追加は未対応です
- Dry Run（事前確認）は未実装です

## ライセンス

MIT License です。詳細は `LICENSE` を参照してください。
