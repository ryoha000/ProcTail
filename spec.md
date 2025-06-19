# ProcTail 仕様書

## 1. はじめに

本仕様書は、C# (.NET 8 LTS) で開発されるワーカーサービス `ProcTail` の機能および非機能要件を定義するものです。
`ProcTail` は、特定のプロセスとその子孫プロセスによるファイル操作とプロセス開始/終了イベントを監視・記録し、他のプロセスからの要求に応じて情報を提供し、制御されることを目的とします。

## 2. 名称

ProcTail (プロックテイル)

## 3. 概要k

- 独立したワーカーサービスとして動作します（.NET 8 LTS ランタイムが必要）。
- 常に管理者権限での実行を試みます。権限が得られない場合は起動に失敗します。
- Windows の ETW (Event Tracing for Windows) 機能を利用し、特定のファイル操作イベントとプロセス開始/終了イベントを高効率に監視します。
- **Named Pipes** を利用したプロセス間通信 (IPC) を介して、外部プロセスから以下の操作を受け付けます。
    - 監視対象プロセスID (PID) とタグ名の登録 (`AddWatchTarget`)
    - タグ名に基づいた記録済みイベント (`BaseEventData` のリスト) の取得 (`GetRecordedEvents`)
    - タグ名に基づいた記録済みイベントのクリア (`ClearEvents`)
    - ヘルスチェック (`HealthCheck`)
    - サービスのシャットダウン要求 (`Shutdown`)
- 監視対象プロセスが行った指定のファイル操作および開始/終了したプロセス（子プロセス含む）の情報を、イベントの種類に応じた**型安全なレコード (`BaseEventData` 派生型)** として、タグごとにオンメモリのFIFOキューで記録します。キューには設定可能な上限件数を設けます。
- 監視対象プロセスが子プロセスを開始した場合、その子プロセスも自動的に同じタグ名で監視対象に追加し、追跡を継続します。
- エラーや動作状況のログは、**Serilog** などのモダンなロギングライブラリを使用して出力し、設定によりファイルやWindowsイベントログ等への出力先を選択可能とします。

## 4. 機能要件

### 4.1. 起動と終了

- **起動:**
    - 独立したプロセスとして起動されること。
- **管理者権限:**
    - 起動時に管理者権限への昇格を試みること。UACプロンプトが表示されます。
    - **管理者権限が得られなかった場合（例: UACで拒否された場合）は、エラーメッセージをログに出力し、アプリケーションは即座に終了すること。**
- **終了:**
    - 他のプロセスから Named Pipes 経由で `Shutdown` 要求を受け付けること。
    - `Shutdown` 要求を受信した場合、新しいIPC要求の受付を停止し、進行中の処理を安全に完了させ、リソース（ETWセッション、IPCリスナー等）を適切に解放し、正常に終了処理を行うこと。

### 4.2. イベント監視 (ETW)

- **監視対象イベント:**
    - 以下のETWイベントを購読し、監視すること。
        - **ファイル操作:**
            - `Microsoft-Windows-Kernel-FileIO`: `FileIo/Create` (ファイル作成)
            - `Microsoft-Windows-Kernel-FileIO`: `FileIo/Write` (ファイル書き込み)
            - `Microsoft-Windows-Kernel-FileIO`: `FileIo/Delete` (ファイル削除)
            - `Microsoft-Windows-Kernel-FileIO`: `FileIo/Rename` (ファイル名変更)
            - `Microsoft-Windows-Kernel-FileIO`: `FileIo/SetInfo` (ファイル情報設定)
            - **※ `FileIo/Read` は監視対象外とする。**
        - **プロセス操作:**
            - `Microsoft-Windows-Kernel-Process`: `Process/Start` (プロセス開始)
            - `Microsoft-Windows-Kernel-Process`: `Process/End` (プロセス終了)
- **監視開始:**
    - `ProcTail` サービスの起動と同時にETWセッションを開始し、上記イベントのリアルタイム監視を開始すること。
    - ただし、イベントの記録は、監視対象プロセスが指定されてから行うこと。

### 4.3. 監視対象の管理

- **監視対象の追加 (`AddWatchTarget`):**
    - 他のプロセスから Named Pipes 経由で、監視対象の追加要求を受け付けること。
    - 要求には、「監視対象とするプロセスのID (PID)」と、記録の識別に使用する「タグ名 (文字列)」が含まれること。
    - **バリデーション:** PID > 0、タグ名が空でないこと、指定PIDのプロセスが存在すること、指定PIDがまだ監視中でないことを確認する。
    - **エラー応答:** バリデーションに失敗した場合、`AddWatchTargetResponse` に `Success = false` と具体的なエラーメッセージを設定して返す（例: "Invalid PID", "Tag name cannot be empty", "Process not found", "PID already watched"）。
    - バリデーションが成功した場合、PIDとタグ名を内部の監視対象リストに登録する。
- **子プロセスの自動追加:**
    - `Process/Start` イベントを検知した際、イベントを発生させたプロセス (親プロセス) のPIDが監視対象リストに含まれているか確認すること。
    - 含まれている場合、新たに開始された子プロセスのPIDを取得し、親プロセスと同じ「タグ名」で自動的に監視対象リストに追加すること。

### 4.4. イベント記録

- **記録条件:**
    - ETWから受信したイベント (4.2で定義) について、そのイベントを発生させたプロセスのPIDが、内部の監視対象リストに登録されているPID（子プロセス含む）と一致する場合のみ、イベント情報を記録すること。
- **記録内容:**
    - イベントの種類に応じて、**`BaseEventData` の派生レコード（`FileEventData`, `ProcessStartEventData`, `ProcessEndEventData`, `GenericEventData` など）** として記録すること。これらは以下の情報を含む。
        - **共通情報 (全イベント):** `Timestamp`, `TagName`, `ProcessId`, `ThreadId`, `ProviderName`, `EventName`, `ActivityId`, `RelatedActivityId`, `Payload` (全ペイロード情報の辞書)
        - **ファイルイベント (`FileEventData`):** 上記に加え、必須の `FilePath` (string)
        - **プロセス開始イベント (`ProcessStartEventData`):** 上記に加え、必須の `ChildProcessId` (int), `ChildProcessName` (string)
        - **プロセス終了イベント (`ProcessEndEventData`):** 上記に加え、必須の `ExitCode` (int)
        - **その他イベント (`GenericEventData`):** 共通情報のみ。詳細は `Payload` を参照。
- **記録方式:**
    - オンメモリで、タグ名ごとに**FIFO (First-In, First-Out) キュー (`ConcurrentQueue<BaseEventData>`)** を用いて記録を管理すること。
    - 各タグのキューには、設定ファイル等で指定可能な上限件数を設けること。キューが上限に達した状態で新しいイベントを追加する場合、最も古いイベントデータをキューから破棄すること。
    - データ構造例: `ConcurrentDictionary<string, ConcurrentQueue<BaseEventData>>` および `ConcurrentDictionary<string, int> MaxEventsPerTag`

### 4.5. 情報提供

- **記録取得要求 (`GetRecordedEvents`):**
    - 他のプロセスから Named Pipes 経由で、記録された情報の取得要求を受け付けること。
    - 要求には、「タグ名」が含まれること。
- **情報提供:**
    - 指定された「タグ名」に関連付けられた全てのイベント記録 (**`List<BaseEventData>`**) を応答として返すこと。
    - **注意:** 非常に大量のイベントが記録されている場合、全件取得はパフォーマンスやメモリに影響を与える可能性がある。
    - 情報取得後も、オンメモリの記録は保持し続けること。
- **記録クリア要求 (`ClearEvents`):**
    - 他のプロセスから Named Pipes 経由で、特定のタグに関連する記録のクリア要求を受け付けること。
    - 要求には、「タグ名」が含まれること。
    - 指定されたタグ名に対応するオンメモリのイベントキューを空にすること。

### 4.6. ヘルスチェック

- **ヘルスチェック要求 (`HealthCheck`):**
    - 他のプロセスから Named Pipes 経由でヘルスチェック要求を受け付けること。
- **応答:**
    - `ProcTail` の基本的な動作状態を示す文字列 **`"Healthy"` または `"Unhealthy"`** のいずれかを応答として返すこと (`HealthCheckResponse.Status` プロパティ)。
    - "Healthy" は、主要な機能（ETWセッションがアクティブ、IPCリスナーが動作中など）が正常に動作している状態を示す。それ以外の場合は "Unhealthy" とする。

## 5. 非機能要件

- **パフォーマンス:**
    - ETWによるイベント監視は、システムへのパフォーマンス影響を最小限に抑えること。
    - オンメモリでの記録と取得処理は効率的に行うこと。Named Pipes 通信も低レイテンシであること。
- **セキュリティ:**
    - 管理者権限で動作するため、Named Pipes インターフェースは適切なアクセス制御リスト (ACL) を設定し、意図しないプロセスからのアクセスを防ぐこと。**ローカルユーザーからのアクセスのみを許可することを推奨。**
- **信頼性:**
    - `Shutdown` 要求に対して、リソースリークなく正常にシャットダウンできること。
    - ETWセッションの開始・停止、イベント処理中の例外ハンドリングを適切に行い、ログに出力すること。
- **プラットフォーム:**
    - **Windows オペレーティングシステム (x64)**
    - **.NET 8 LTS ランタイム**

## 6. インターフェース

### 6.1. 起動インターフェース

- **方法:** 標準的なプロセス起動方法。
- **引数:** （オプション） `-launched-by <AppName>` などの引数。

### 6.2. プロセス間通信 (IPC) インターフェース (Named Pipes)

- **技術:** **Named Pipes** を使用。パイプ名は固定（`\\\\.\\pipe\\ProcTailIPC`）とする。
- **通信形式:** 要求と応答は、**JSON** (`System.Text.Json` を使用) でシリアライズされたデータ構造で行う。
    - **注意:** JSONシリアライズ/デシリアライズ時には、`BaseEventData` の派生型を正しく扱うための設定（例: `System.Text.Json` のポリモーフィック型シリアライズ）が必要です。
- **提供メソッド (Request/Response クラス):**
    
    ```csharp
    // --- 共通応答基底クラス ---
    public abstract record BaseResponse { public bool Success { get; init; } = true; public string ErrorMessage { get; init; } = string.Empty; }
    
    // --- AddWatchTarget ---
    public record AddWatchTargetRequest(int ProcessId, string TagName);
    public record AddWatchTargetResponse : BaseResponse;
    
    // --- GetRecordedEvents ---
    public record GetRecordedEventsRequest(string TagName);
    public record GetRecordedEventsResponse(List<BaseEventData> Events) : BaseResponse; // Eventsは成功時のみ有効
    
    // --- ClearEvents ---
    public record ClearEventsRequest(string TagName);
    public record ClearEventsResponse : BaseResponse;
    
    // --- Shutdown ---
    public record ShutdownRequest; // データなし
    public record ShutdownResponse : BaseResponse;
    
    // --- HealthCheck ---
    public record HealthCheckRequest; // データなし
    public record HealthCheckResponse(string Status) : BaseResponse; // Status: "Healthy" or "Unhealthy"
    
    ```
    

## 7. 実装上の考慮事項

- **.NET ランタイム:** **.NET 8 LTS** を使用する。
- **ETWライブラリ:** `Microsoft.Diagnostics.Tracing.TraceEvent` NuGet パッケージの利用を推奨。
- **ETWイベント処理:** ETWイベントハンドラでは、`TraceEvent` オブジェクトの `ProviderName` と `EventName` (またはID) を基に、適切な `BaseEventData` 派生レコード（`FileEventData`, `ProcessStartEventData` など）をインスタンス化し、必要な情報をペイロードから抽出して設定します。対応する派生型がない場合は `GenericEventData` を使用します。
- **管理者権限昇格:** アプリケーションマニフェストファイル (`app.manifest`) で `requestedExecutionLevel` を `requireAdministrator` に設定する。
- **IPC実装 (Named Pipes):** `System.IO.Pipes` 名前空間のクラス (`NamedPipeServerStream` など) を使用して Named Pipes サーバーを実装する。非同期処理 (`async/await`) を活用する。
- **IPCセキュリティ (ACL):** `NamedPipeServerStream` 作成時に `PipeSecurity` を設定し、アクセス権を制御する（例: ローカルの認証済みユーザーに読み書き許可、Administratorsにフルコントロール許可）。
- **JSONシリアライズ (ポリモーフィズム):** `System.Text.Json` で継承関係のある `BaseEventData` を送受信する場合、ポリモーフィック型シリアライズの設定が必要です。`.NET 7+` であれば `[JsonPolymorphic]` および `[JsonDerivedType]` 属性を `BaseEventData` の定義に追加するのが簡単です。それ以前のバージョンではカスタム `JsonConverter` の実装が必要になる場合があります。
- **ロギング:** **Serilog** などのロギングライブラリを導入し、設定ファイル (`appsettings.json`) でログレベルや出力先（ファイル、イベントログ、コンソール等）を構成可能にする。
- **設定管理:** `Microsoft.Extensions.Configuration` と `Microsoft.Extensions.Options` を使用して `appsettings.json` から設定（ログ設定、キュー上限、パイプ名など）を読み込む。設定ファイルの構造を定義する（例: `PipeSettings`, `EventSettings` クラス）。
- **スレッドセーフ:** 監視対象リスト (`ConcurrentDictionary`) やイベント記録キュー (`ConcurrentQueue<BaseEventData>`) へのアクセスはスレッドセーフなコレクションを使用する。
- **メモリ管理:** タグごとのイベントキューの上限件数を `appsettings.json` で設定可能にし、超過分はFIFOで破棄するロジックを実装する。
- **エラーハンドリング:** ETWセッション、イベント処理、IPC通信、設定読み込み等のエラーを網羅的に捕捉し、ログに出力する。必要に応じて `HealthCheck` ステータスを `Unhealthy` にする。IPC応答では `BaseResponse` の `Success` と `ErrorMessage` を適切に設定する。
- **シャットダウン処理:** `Shutdown` 要求受信時や、プロセス終了シグナル (Ctrl+C など) をハンドルし、ETWセッションの停止 (`TraceEventSession.Dispose()`)、IPCリスナーの停止、ログのフラッシュなどのクリーンアップ処理を確実に行う。
- **クライアント側での利用:** IPCで `List<BaseEventData>` を受け取ったクライアントは、C#のパターンマッチング (`switch` 式や `if (ev is ...)`）を使用して、各イベントの具体的な型を判別し、型安全に固有プロパティ（`FilePath`, `ChildProcessId` など）にアクセスします。
