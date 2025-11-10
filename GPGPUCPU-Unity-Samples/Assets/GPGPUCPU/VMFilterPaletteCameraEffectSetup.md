# VMFilterPaletteRendererFeature の適用手順

このドキュメントは、Universal Render Pipeline (URP) のレンダラーに `VMFilterPaletteRendererFeature`
を追加し、カメラのレンダリング結果に VMFilterPalette コンピュートシェーダーを適用するための手順をまとめたものです。

## 必要なアセットの配置確認

以下のアセットがすべて `Assets/GPGPUCPU` フォルダ内に存在することを確認してください。

- `FilterPalette.compute`
- `SampleIndex.png`（または同等のインデックステクスチャ）
- `out/bytecode.hex.txt`
- `out/programs.json`

これらは既存のサンプルをインポートすると自動的に配置されています。`SampleIndex.png` などが無い場合は、メニュー **GPGPUCPU > Create Sample Textures** を実行して再生成してください。

## Universal Renderer Asset への追加

1. 対象の URP Renderer Asset（例: `ForwardRenderer.asset`）を Project ウィンドウで選択します。
2. Inspector の **Renderer Features** セクションで **Add Renderer Feature** をクリックし、一覧から `VMFilterPaletteRendererFeature` を追加します。
3. 追加した Renderer Feature の Inspector で次のフィールドを設定します。
   - **Shader** : `Assets/GPGPUCPU/FilterPalette.compute`
   - **Index Texture** : `Assets/GPGPUCPU/SampleIndex.png` など、ピクセル毎に 0〜255 のインデックスを持つテクスチャ
   - **Bytecode Hex** : `Assets/GPGPUCPU/out/bytecode.hex.txt`
   - **Programs Json** : `Assets/GPGPUCPU/out/programs.json`
4. 任意で **Disable Jumps** や **Max Steps** を調整して、VM の挙動を制御します。

> **メモ:** Renderer Feature はそれを参照するすべてのカメラに適用されます。特定のカメラでのみ使用したい場合は、URP Renderer を複製して適用対象のカメラに割り当ててください。

## カメラ側の設定

- URP の Renderer Asset が適用されているカメラであれば追加のスクリプトは不要です。
- 後処理を無効化していてもフィルターは適用されますが、ほかのポストエフェクトと併用する場合は Renderer Features の実行順序で描画結果が変化します。

## 解像度の注意点

- インデックステクスチャの解像度がカメラの出力解像度と異なる場合、Renderer Feature が一時的な RenderTexture にリサイズして使用します。
  意図したパレット結果を得るためには、できるだけ同じ解像度で作成することを推奨します。
- カメラの解像度が変更された場合も、自動的に内部の RenderTexture を再確保します。

## トラブルシュート

- Index Texture が未設定の場合はレンダリングされず、ログに警告が表示されます。正しいテクスチャが指定されているか確認してください。
- Bytecode Hex / Programs Json の内容が破損している場合は、Inspector の参照先が正しいか、再生成を行ってください。
- `FilterPalette.compute` 内のカーネル名が `Run` でないと Renderer Feature が初期化に失敗します。必要に応じてカーネル名を確認してください。
