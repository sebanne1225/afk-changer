# AFK Manager

VRChat アバターの AFK アニメーションを非破壊で管理する NDMF プラグインです。

複数の AFK モーションを Expression Menu から切り替えたり、元の AFK との入れ替え・追加・削除を Inspector から行えます。Modular Avatar と連携し、ビルド時に Expression Menu とパラメータを自動生成します。

## 何ができるか

- AFK モーションを Avatar / Prefab または AnimatorController から入力して、Action Layer に追加・入れ替え・削除できます
- 複数の AFK スロットを Expression Menu から切り替えられます（Modular Avatar 連携、ビルド時に自動生成）
- 元の AFK を「含める / 外す」「リスト内で並び替え」で制御できます
- flat パターン（ルート直下のステート）と SubStateMachine パターンの両方に対応しています
- TrackingControl / PlayableLayerControl を自動で付与します（ソースに含まれていない場合も補完）
- AFK BlendOut ステートを自動生成し、AFK 終了時のウェイト復帰とトラッキング復帰を処理します
- FX レイヤーの AFK ステート削除（FX Clean）に対応しています
- 元のアバターには直接変更を加えない非破壊設計です（NDMF）

## 対応環境

- Unity `2022.3`
- VRChat Avatars package `>= 3.5.0`
- NDMF `>= 1.4.0`
- Modular Avatar（複数スロットで Expression Menu 切り替えを使う場合に必須、単一スロット時は optional）
- VCC / VPM ベースの VRChat プロジェクトを推奨します

## VCC / VPM 導入方法

### 推奨: VCC / VPM から導入

1. VCC に追加する URL として `https://sebanne1225.github.io/sebanne-listing/index.json` を追加します。
2. package 一覧から `AFK Manager` (`com.sebanne.afk-changer`) を追加します。
3. Unity を開き、依存 package が解決されていることを確認します。

参考ページ (`VCC` 追加先ではありません): `https://sebanne1225.github.io/sebanne-listing/`

### 補助: Git URL / Release zip から導入

- repo: `https://github.com/sebanne1225/afk-manager`
- Git URL や local package での導入は、開発確認や手動検証向けの補助導線です
- GitHub Release の zip も補助導線として使えます。`com.sebanne.afk-changer-2.0.0.zip` を展開すると、直下に `package.json` が見える package 構成です
- これらの補助導線では、`VRChat Avatars` と `NDMF` の依存解決を自分で確認する必要があります

なお、package ID (`com.sebanne.afk-changer`) は v1.x との互換性のため維持しています。ツール名は `AFK Manager` ですが、package ID と release asset 名には旧名が残ります。

## 使い方

1. アバタールートの Inspector で `Add Component` から `AFK Manager` を追加します。
2. 「AFK スロット」リストに AFK モーションを追加します。
   - Avatar / Prefab をリストまたは ReorderableList 枠内にドラッグします（Action Controller が自動で取得されます）
   - または、`Controller` モードに切り替えて AnimatorController を直接指定します
   - 追加スロット行の `▼` ボタンからプロジェクト内のアバタープレハブ一覧を選択することもできます
3. 「元の AFK を含める」チェックボックスで、アバター標準の AFK をリストに含めるか切り替えます。含める場合はリスト内でドラッグして位置を調整できます。
4. アップロードします。NDMF がビルド時に自動で AFK ステートを処理します。

単一スロットだけを設定した場合、従来の入れ替え（または削除のみ）として動作し、Modular Avatar は不要です。

## 複数スロットと Expression Menu

AFK スロットが 2 個以上（元の AFK を含める設定の場合、元の AFK もカウントされます）になると、Modular Avatar 経由で Expression Menu が自動生成されます。

- 先頭のスロットがメニュー OFF 時のデフォルトになります（`★` バッジで表示）
- Expression Menu の各項目はスロットに対応し、切り替えで AFK モーションが変わります
- メニューのインストール先は Inspector の `メニュー設定` から選択できます（指定しない場合はアバターのトップメニューに設置されます）
- MA が未導入の場合は Inspector に Warning が表示されます

## FX の扱い

- 「元の FX AFK を外す」チェックで、FX レイヤーの AFK 関連ステートを削除できます（FX Clean）
- FX の「付ける」側（FX Replace や Object Toggle 連携）は `2.1+` で設計予定です。`2.0.0` では FX Clean のみ対応します

## Release Asset

GitHub Release には、VPM 配布確認や手動保管に使える package zip を添付します。

- 例: `com.sebanne.afk-changer-<version>.zip`

package ID 互換性維持のため、zip 名は旧名 (`afk-changer`) のままです。

## 制限事項

- AnimationClip 単体での差し替えは未対応です（次フェーズ候補）
- GoGoLoco 等の多重ネスト SubStateMachine パターンは未対応です（検出時は警告を出してスキップします）
- Action Layer が未設定のアバターへの自動追加は未対応です
- Dry Run（事前確認）は未実装です
- FX の「付ける」側（FX Replace / Object Toggle 連携）は未対応です（`2.1+` で設計予定）

## ライセンス

MIT License です。詳細は `LICENSE` を参照してください。
