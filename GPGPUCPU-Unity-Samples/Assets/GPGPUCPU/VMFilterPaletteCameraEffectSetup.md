# VMFilterPaletteCameraEffect の適用手順

このドキュメントは、`VMFilterPaletteCameraEffect` スクリプトを使ってカメラのレンダリング結果に VMFilterPalette コンピュートシェーダーを適用する手順をまとめたものです。

## 必要なアセットの配置確認

以下のアセットがすべて `Assets/GPGPUCPU` フォルダ内に存在することを確認してください。

- `FilterPalette.compute`
- `SampleIndex.png`（または同等のインデックステクスチャ）
- `out/bytecode.hex.txt`
- `out/programs.json`

これらは既存のサンプルをインポートすると自動的に配置されています。`SampleIndex.png` などが無い場合は、メニュー **GPGPUCPU > Create Sample Textures** を実行して再生成してください。

## スクリプトの追加手順

1. Unity で対象のシーンを開きます。
2. カメラ（例: `Main Camera`）を選択し、Inspector で **Add Component** をクリックして `VMFilterPaletteCameraEffect` を追加します。
3. Inspector に表示される以下のフィールドへ、それぞれのアセットを割り当てます。
   - **Shader** : `Assets/GPGPUCPU/FilterPalette.compute`
   - **Index Texture** : `Assets/GPGPUCPU/SampleIndex.png` など、ピクセル毎に 0〜255 のインデックスを持つテクスチャ
   - **Bytecode Hex** : `Assets/GPGPUCPU/out/bytecode.hex.txt`
   - **Programs Json** : `Assets/GPGPUCPU/out/programs.json`
4. シーンを再生すると、カメラの描画結果に対してコンピュートシェーダーによるパレット変換が適用されます。

## 解像度の注意点

- インデックステクスチャの解像度がカメラの出力解像度と異なる場合でも、スクリプトが自動で一時的な RenderTexture にリサイズします。ただし、意図したパレット結果を得るためには、できるだけ同じ解像度で作成することを推奨します。
- カメラの解像度が変更された場合、スクリプトが内部の RenderTexture を自動的に作り直します。

## VM のパラメーター

- **Disable Jumps** : VM のジャンプ命令を無効化するかどうか。
- **Max Steps** : ピクセルあたりの最大命令ステップ数。パフォーマンスと品質のバランスを見ながら調整してください。

## トラブルシュート

- Index Texture が未設定の状態ではエフェクトは実行されず、ログに警告が表示されます。
- Bytecode Hex / Programs Json がパースできない場合は、Inspector で正しいアセットが設定されているか確認してください。
- `OnRenderImage` が動作するため、カメラの **Allow HDR/Allow MSAA** 設定や後処理スタックと併用する場合は、Unity の標準ポストプロセスと同様に順序を調整してください。

