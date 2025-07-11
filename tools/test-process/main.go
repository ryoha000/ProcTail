package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"log"
	"os"
	"proctail-test-process/operations"
	"strings"
	"time"
)

type Config struct {
	Count    int           `json:"count"`
	Interval time.Duration `json:"interval"`
	Dir      string        `json:"dir"`
	Verbose  bool          `json:"verbose"`
	Command  string        `json:"command,omitempty"`
	Ops      []string      `json:"operations,omitempty"`
	Duration time.Duration `json:"duration,omitempty"`
}

type Report struct {
	Operation   string        `json:"operation"`
	Config      Config        `json:"config"`
	StartTime   time.Time     `json:"start_time"`
	EndTime     time.Time     `json:"end_time"`
	Duration    time.Duration `json:"duration"`
	TotalOps    int           `json:"total_operations"`
	SuccessOps  int           `json:"successful_operations"`
	FailedOps   int           `json:"failed_operations"`
	Errors      []string      `json:"errors,omitempty"`
	ProcessID   int           `json:"process_id"`
	ChildPIDs   []int         `json:"child_process_ids,omitempty"`
}

// Implement the required interfaces for operations
func (r *Report) GetConfig() operations.Config {
	return operations.Config{
		Count:    r.Config.Count,
		Interval: r.Config.Interval,
		Dir:      r.Config.Dir,
		Verbose:  r.Config.Verbose,
		Duration: r.Config.Duration,
	}
}

func (r *Report) GetProcessConfig() operations.ProcessConfig {
	return operations.ProcessConfig{
		Count:    r.Config.Count,
		Interval: r.Config.Interval,
		Dir:      r.Config.Dir,
		Verbose:  r.Config.Verbose,
		Command:  r.Config.Command,
		Duration: r.Config.Duration,
	}
}

func (r *Report) GetMixedConfig() operations.MixedConfig {
	return operations.MixedConfig{
		Count:    r.Config.Count,
		Interval: r.Config.Interval,
		Dir:      r.Config.Dir,
		Verbose:  r.Config.Verbose,
		Command:  r.Config.Command,
		Ops:      r.Config.Ops,
		Duration: r.Config.Duration,
	}
}

func (r *Report) IncrementSuccess() {
	r.SuccessOps++
}

func (r *Report) IncrementFailed() {
	r.FailedOps++
}

func (r *Report) AddError(err error) {
	r.Errors = append(r.Errors, err.Error())
}

func (r *Report) SetTotalOps(count int) {
	r.TotalOps = count
}

func (r *Report) AddChildPID(pid int) {
	r.ChildPIDs = append(r.ChildPIDs, pid)
}

// ProcessReportAdapter adapts Report to ProcessReport interface
type ProcessReportAdapter struct {
	report *Report
}

func (a *ProcessReportAdapter) GetConfig() operations.ProcessConfig {
	return a.report.GetProcessConfig()
}

func (a *ProcessReportAdapter) IncrementSuccess() {
	a.report.IncrementSuccess()
}

func (a *ProcessReportAdapter) IncrementFailed() {
	a.report.IncrementFailed()
}

func (a *ProcessReportAdapter) AddError(err error) {
	a.report.AddError(err)
}

func (a *ProcessReportAdapter) SetTotalOps(count int) {
	a.report.SetTotalOps(count)
}

func (a *ProcessReportAdapter) AddChildPID(pid int) {
	a.report.AddChildPID(pid)
}

// MixedReportAdapter adapts Report to MixedReport interface
type MixedReportAdapter struct {
	report *Report
}

func (a *MixedReportAdapter) GetConfig() operations.MixedConfig {
	return a.report.GetMixedConfig()
}

func (a *MixedReportAdapter) IncrementSuccess() {
	a.report.IncrementSuccess()
}

func (a *MixedReportAdapter) IncrementFailed() {
	a.report.IncrementFailed()
}

func (a *MixedReportAdapter) AddError(err error) {
	a.report.AddError(err)
}

func (a *MixedReportAdapter) SetTotalOps(count int) {
	a.report.SetTotalOps(count)
}

func (a *MixedReportAdapter) AddChildPID(pid int) {
	a.report.AddChildPID(pid)
}

func main() {
	var (
		count    = flag.Int("count", 3, "操作回数")
		interval = flag.Duration("interval", time.Second, "操作間隔")
		dir      = flag.String("dir", os.TempDir(), "対象ディレクトリ")
		verbose  = flag.Bool("verbose", false, "詳細ログ")
		command  = flag.String("command", "", "実行するコマンド (child-process用)")
		ops      = flag.String("operations", "write,read,delete", "実行する操作のリスト (mixed用)")
		jsonOut  = flag.Bool("json", false, "JSON形式で結果出力")
		waitKey  = flag.Bool("wait", false, "開始前にキー入力待機")
		duration = flag.Duration("duration", 0, "継続実行時間 (0=無効)")
	)
	flag.Parse()

	if len(flag.Args()) == 0 {
		fmt.Println("使用方法: test-process [operation] [options]")
		fmt.Println("")
		fmt.Println("操作:")
		fmt.Println("  file-write    - ファイル書き込み操作")
		fmt.Println("  file-read     - ファイル読み込み操作")
		fmt.Println("  file-delete   - ファイル削除操作")
		fmt.Println("  child-process - 子プロセス作成")
		fmt.Println("  mixed         - 複数操作の組み合わせ")
		fmt.Println("  continuous    - 継続実行モード (--duration必須)")
		fmt.Println("")
		fmt.Println("オプション:")
		flag.PrintDefaults()
		os.Exit(1)
	}

	operation := flag.Args()[0]
	
	config := Config{
		Count:    *count,
		Interval: *interval,
		Dir:      *dir,
		Verbose:  *verbose,
		Command:  *command,
		Ops:      strings.Split(*ops, ","),
		Duration: *duration,
	}

	if *verbose {
		log.Printf("テストプロセス開始: %s", operation)
		log.Printf("設定: %+v", config)
		log.Printf("プロセスID: %d", os.Getpid())
	}

	if *waitKey {
		fmt.Print("開始するにはEnterキーを押してください...")
		fmt.Scanln()
	}

	report := Report{
		Operation: operation,
		Config:    config,
		StartTime: time.Now(),
		ProcessID: os.Getpid(),
	}

	var err error
	switch operation {
	case "file-write":
		err = operations.ExecuteFileWrite(&report)
	case "file-read":
		err = operations.ExecuteFileRead(&report)
	case "file-delete":
		err = operations.ExecuteFileDelete(&report)
	case "child-process":
		processReport := &ProcessReportAdapter{report: &report}
		err = operations.ExecuteChildProcess(processReport)
	case "mixed":
		mixedReport := &MixedReportAdapter{report: &report}
		err = operations.ExecuteMixed(mixedReport)
	case "continuous":
		if config.Duration <= 0 {
			log.Fatalf("continuous操作には--durationオプションが必要です")
		}
		err = operations.ExecuteContinuous(&report)
	default:
		log.Fatalf("不明な操作: %s", operation)
	}

	report.EndTime = time.Now()
	report.Duration = report.EndTime.Sub(report.StartTime)

	if *jsonOut {
		jsonData, _ := json.MarshalIndent(report, "", "  ")
		fmt.Println(string(jsonData))
	} else if *verbose {
		log.Printf("実行完了: %s", operation)
		log.Printf("総操作数: %d, 成功: %d, 失敗: %d", 
			report.TotalOps, report.SuccessOps, report.FailedOps)
		log.Printf("実行時間: %v", report.Duration)
	}

	if err != nil {
		if *verbose {
			log.Printf("エラー: %v", err)
		}
		os.Exit(1)
	}
}