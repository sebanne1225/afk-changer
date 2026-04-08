# Sebanne Tool Template

この repository は完成済みツールではなく、VRChat 向け Unity Editor ツールを作り始めるためのテンプレ repo です。複製後に命名や実装を置き換え、各ツール専用の package として育てる前提で用意しています。

## 概要

`Sebanne Tool Template` は、VRChat 向け Unity Editor ツールを UPM 形式で整理して開発するためのテンプレートです。Runtime と Editor の責務を分離し、配布・再利用しやすい最小構成を用意しています。

## 何ができるか

- Unity Package としてツールを整理できます
- `Editor` と `Runtime` を分離して保守しやすくできます
- ドキュメント、サンプル、開発メモを同じ repo で管理できます
- Dry Run や診断を先に考慮したツール設計の土台にできます

## 導入方法

1. このテンプレートを複製して新しい repo を作成します。
2. `package.json` の package 名、表示名、説明をツール名に合わせて変更します。
3. asmdef 名、名前空間、README、ドキュメントをツール内容に合わせて置き換えます。
4. Unity プロジェクトの Package Manager からローカルパッケージまたは Git URL として読み込みます。

## 関連ファイル

- パッケージ定義: [`package.json`](./package.json)
- ライセンス: [`LICENSE`](./LICENSE)
- Runtime asmdef: [`Runtime/Sebanne.ToolTemplate.asmdef`](./Runtime/Sebanne.ToolTemplate.asmdef)
- Editor asmdef: [`Editor/Sebanne.ToolTemplate.Editor.asmdef`](./Editor/Sebanne.ToolTemplate.Editor.asmdef)
- テンプレ確認ウィンドウ: [`Editor/ToolTemplateCheckWindow.cs`](./Editor/ToolTemplateCheckWindow.cs)

## 使い方

1. `Runtime/` に実行時に必要な型や共通ロジックを配置します。
2. `Editor/` にメニュー、ウィンドウ、検証、変換処理などの Editor 拡張を配置します。
3. `Documentation~/` に公開向けドキュメントや設計メモを追加します。
4. `Samples~/Example/` に最小の使用例を置きます。

## 動作確認

1. Unity プロジェクトの Package Manager から、この repo をローカルパッケージとして読み込みます。
2. Unity 上部メニューの `Tools/Sebanne Tool Template/Template Check Window` を開きます。
3. ウィンドウ内の `確認ログを出す` ボタンを押し、Console に 1 行ログが出ることを確認します。

## 新しいツールを作るときの置換ポイント

- `ToolTemplate`
- `com.sebanne.tool-template`
- `Sebanne Tool Template`

## Dry Run / 診断

- 破壊的な処理を実装する前に Dry Run モードを用意し、対象件数や変更予定内容を先に確認できるようにします。
- 実処理と診断処理のログ形式をそろえ、利用者が差分を追いやすいようにします。
- 問題発生時は、対象、理由、回避策がログや UI から分かるようにします。

## 制限事項

- このテンプレート自体には具体的な機能実装は含まれていません。
- VRChat SDK 依存コード、外部パッケージ依存、各種メニュー実装は未追加です。
- 実運用前に Unity バージョン、依存関係、命名規則、ログ方針をプロジェクトに合わせて調整してください。

## ライセンス

このテンプレートは MIT License で提供します。詳細は `LICENSE` を参照してください。
