# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

ProcTailは、C# (.NET 8 LTS) で開発されるWindowsワーカーサービスです。ETW (Event Tracing for Windows) を使用して特定のプロセスとその子プロセスのファイル操作・プロセス開始終了イベントを監視し、Named Pipesを介して他のプロセスからの制御・情報取得要求に応答します。

## 主要なアーキテクチャ

- **ETW監視エンジン**: `Microsoft.Diagnostics.Tracing.TraceEvent` を使用してファイル操作とプロセスイベントをリアルタイム監視
- **Named Pipes IPC**: `System.IO.Pipes.NamedPipeServerStream` でプロセス間通信を実装（パイプ名: `\\\\.\\pipe\\ProcTailIPC`）
- **イベント記録システム**: タグごとに `ConcurrentQueue<BaseEventData>` でFIFOキューを管理
- **型安全なイベントデータ**: `BaseEventData` 基底クラスから派生した `FileEventData`, `ProcessStartEventData`, `ProcessEndEventData` レコード
- **管理者権限必須**: app.manifestで `requireAdministrator` を設定し、UACプロンプト経由で管理者権限を取得

## 監視対象イベント

- **ファイル操作**: FileIo/Create, Write, Delete, Rename, SetInfo (※ReadはOFF)
- **プロセス操作**: Process/Start, Process/End
- 子プロセスは親と同じタグで自動的に監視対象に追加される

## IPCインターフェース（JSON）

- `AddWatchTarget`: PIDとタグ名でプロセス監視開始
- `GetRecordedEvents`: タグ名でイベント履歴取得  
- `ClearEvents`: タグ名でイベント履歴クリア
- `HealthCheck`: サービス稼働状況確認
- `Shutdown`: サービス終了要求

## 技術スタック

- **.NET 8 LTS**: ターゲットフレームワーク
- **Microsoft.Diagnostics.Tracing.TraceEvent**: ETW操作用NuGetパッケージ
- **System.Text.Json**: IPC通信でのJSONシリアライズ（ポリモーフィック対応必要）
- **Serilog**: 構造化ロギング
- **Microsoft.Extensions.Configuration/Options**: appsettings.json設定管理
- **スレッドセーフコレクション**: ConcurrentDictionary, ConcurrentQueue使用

## セキュリティ要件

- Named PipesにACL設定でローカルユーザーのみアクセス許可
- 管理者権限での動作が前提（権限取得失敗時は即座に終了）

## 重要な実装ポイント

- ETWイベントハンドラで `TraceEvent.ProviderName` と `EventName` を基に適切な派生レコードを生成
- 各タグのキューに設定可能な上限件数、超過時は古いイベントをFIFO削除
- Shutdownシグナル受信時のリソースクリーンアップ（ETWセッション、IPCリスナー停止）
- ポリモーフィックJSONシリアライズには `[JsonPolymorphic]`, `[JsonDerivedType]` 属性を使用

## メモリ

- スタブ化はしないでください
- 日本語で回答するというメモリを追加
- ETWの実装は ./references/profile-explorer/src/ProfileExplorerUI/Profile/ETW を参考にしてください
- コードを変更したときはdotnet testを実行する
- ビルド時のwarningを無視しない
- テストが失敗した場合は即座に本質的な解決を試みる
- 書いたテストは実行する
- cmdではなくPowerShellをつかう