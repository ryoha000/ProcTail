package operations

import (
	"fmt"
	"log"
	"math/rand"
	"os"
	"time"
)

// MixedReport interface for mixed operations
type MixedReport interface {
	GetConfig() MixedConfig
	IncrementSuccess()
	IncrementFailed()
	AddError(error)
	SetTotalOps(int)
	AddChildPID(int)
}

type MixedConfig struct {
	Count    int
	Interval time.Duration
	Dir      string
	Verbose  bool
	Command  string
	Ops      []string
}

// ExecuteMixed performs a combination of different operations
func ExecuteMixed(report MixedReport) error {
	config := report.GetConfig()
	
	// Parse operations list
	operations := config.Ops
	if len(operations) == 0 {
		operations = []string{"write", "read", "delete"}
	}

	totalOps := config.Count * len(operations)
	report.SetTotalOps(totalOps)
	
	if config.Verbose {
		log.Printf("複合操作開始: %d回 x %d種類 = %d操作、間隔 %v", 
			config.Count, len(operations), totalOps, config.Interval)
		log.Printf("操作種類: %v", operations)
	}

	// Create a mixed report adapter that implements the required interfaces
	adapter := &MixedReportAdapter{report: report}

	for i := 0; i < config.Count; i++ {
		if config.Verbose {
			log.Printf("=== 複合操作セット %d/%d ===", i+1, config.Count)
		}

		// Execute each operation type
		for j, opType := range operations {
			if config.Verbose {
				log.Printf("操作 %d.%d: %s", i+1, j+1, opType)
			}

			var err error
			switch opType {
			case "write", "file-write":
				// Single file write
				err = executeSingleFileWrite(adapter, i, j)
			case "read", "file-read":
				// Single file read
				err = executeSingleFileRead(adapter, i, j)
			case "delete", "file-delete":
				// Single file delete
				err = executeSingleFileDelete(adapter, i, j)
			case "rename", "file-rename":
				// Single file rename
				err = executeSingleFileRename(adapter, i, j)
			case "process", "child-process":
				// Single child process
				err = executeSingleChildProcess(adapter, i, j)
			case "dir", "directory":
				// Directory operations
				err = executeSingleDirectoryOp(adapter, i, j)
			default:
				// Random operation
				err = executeRandomOperation(adapter, i, j)
			}

			if err != nil {
				report.AddError(fmt.Errorf("複合操作エラー %d.%d (%s): %w", i+1, j+1, opType, err))
				report.IncrementFailed()
			} else {
				report.IncrementSuccess()
			}

			// Wait between operations within the same set
			if j < len(operations)-1 {
				time.Sleep(config.Interval / time.Duration(len(operations)))
			}
		}

		// Wait between operation sets
		if i < config.Count-1 {
			time.Sleep(config.Interval)
		}
	}

	return nil
}

// MixedReportAdapter adapts MixedReport to other report interfaces
type MixedReportAdapter struct {
	report MixedReport
}

func (a *MixedReportAdapter) GetConfig() Config {
	config := a.report.GetConfig()
	return Config{
		Count:    config.Count,
		Interval: config.Interval,
		Dir:      config.Dir,
		Verbose:  config.Verbose,
	}
}

func (a *MixedReportAdapter) GetProcessConfig() ProcessConfig {
	config := a.report.GetConfig()
	return ProcessConfig{
		Count:    config.Count,
		Interval: config.Interval,
		Dir:      config.Dir,
		Verbose:  config.Verbose,
		Command:  config.Command,
	}
}

func (a *MixedReportAdapter) IncrementSuccess() {
	// Don't increment here, let the main report handle it
}

func (a *MixedReportAdapter) IncrementFailed() {
	// Don't increment here, let the main report handle it
}

func (a *MixedReportAdapter) AddError(err error) {
	// Don't add here, let the main report handle it
}

func (a *MixedReportAdapter) SetTotalOps(count int) {
	// Don't set here, let the main report handle it
}

func (a *MixedReportAdapter) AddChildPID(pid int) {
	a.report.AddChildPID(pid)
}

// Individual operation executors
func executeSingleFileWrite(adapter *MixedReportAdapter, setNum, opNum int) error {
	config := adapter.GetConfig()
	fileName := fmt.Sprintf("mixed_write_%d_%d_%d.txt", os.Getpid(), setNum, opNum)
	filePath := fmt.Sprintf("%s/%s", config.Dir, fileName)
	
	content := fmt.Sprintf("Mixed write operation %d.%d\nTimestamp: %s\nPID: %d\n", 
		setNum+1, opNum+1, time.Now().Format(time.RFC3339), os.Getpid())
	
	if config.Verbose {
		log.Printf("  ファイル書き込み: %s", filePath)
	}

	err := os.WriteFile(filePath, []byte(content), 0644)
	if err == nil && config.Verbose {
		log.Printf("  ファイル書き込み完了: %s", filePath)
	}
	return err
}

func executeSingleFileRead(adapter *MixedReportAdapter, setNum, opNum int) error {
	config := adapter.GetConfig()
	
	// Create a temporary file to read
	fileName := fmt.Sprintf("mixed_read_%d_%d_%d.txt", os.Getpid(), setNum, opNum)
	filePath := fmt.Sprintf("%s/%s", config.Dir, fileName)
	
	content := fmt.Sprintf("Mixed read test %d.%d\nCreated: %s\n", 
		setNum+1, opNum+1, time.Now().Format(time.RFC3339))
	
	// Write file first
	err := os.WriteFile(filePath, []byte(content), 0644)
	if err != nil {
		return err
	}
	
	if config.Verbose {
		log.Printf("  ファイル読み込み: %s", filePath)
	}

	// Read the file
	data, err := os.ReadFile(filePath)
	if err == nil {
		if config.Verbose {
			log.Printf("  ファイル読み込み完了: %s (%d bytes)", filePath, len(data))
		}
		// Clean up
		os.Remove(filePath)
	}
	return err
}

func executeSingleFileDelete(adapter *MixedReportAdapter, setNum, opNum int) error {
	config := adapter.GetConfig()
	
	// Create a temporary file to delete
	fileName := fmt.Sprintf("mixed_delete_%d_%d_%d.txt", os.Getpid(), setNum, opNum)
	filePath := fmt.Sprintf("%s/%s", config.Dir, fileName)
	
	content := fmt.Sprintf("Mixed delete test %d.%d\nCreated: %s\n", 
		setNum+1, opNum+1, time.Now().Format(time.RFC3339))
	
	// Write file first
	err := os.WriteFile(filePath, []byte(content), 0644)
	if err != nil {
		return err
	}
	
	if config.Verbose {
		log.Printf("  ファイル削除: %s", filePath)
	}

	// Delete the file
	err = os.Remove(filePath)
	if err == nil && config.Verbose {
		log.Printf("  ファイル削除完了: %s", filePath)
	}
	return err
}

func executeSingleFileRename(adapter *MixedReportAdapter, setNum, opNum int) error {
	config := adapter.GetConfig()
	
	// Create a temporary file to rename
	oldFileName := fmt.Sprintf("mixed_rename_old_%d_%d_%d.txt", os.Getpid(), setNum, opNum)
	newFileName := fmt.Sprintf("mixed_rename_new_%d_%d_%d.txt", os.Getpid(), setNum, opNum)
	oldPath := fmt.Sprintf("%s/%s", config.Dir, oldFileName)
	newPath := fmt.Sprintf("%s/%s", config.Dir, newFileName)
	
	content := fmt.Sprintf("Mixed rename test %d.%d\nCreated: %s\n", 
		setNum+1, opNum+1, time.Now().Format(time.RFC3339))
	
	// Write file first
	err := os.WriteFile(oldPath, []byte(content), 0644)
	if err != nil {
		return err
	}
	
	if config.Verbose {
		log.Printf("  ファイルリネーム: %s -> %s", oldPath, newPath)
	}

	// Rename the file
	err = os.Rename(oldPath, newPath)
	if err == nil {
		if config.Verbose {
			log.Printf("  ファイルリネーム完了: %s -> %s", oldPath, newPath)
		}
		// Clean up
		os.Remove(newPath)
	}
	return err
}

func executeSingleChildProcess(adapter *MixedReportAdapter, setNum, opNum int) error {
	// This is a simplified version - we'll create a single child process
	config := adapter.GetProcessConfig()
	
	if config.Verbose {
		log.Printf("  子プロセス作成 %d.%d", setNum+1, opNum+1)
	}

	// Create a simple mock report for the child process operation
	mockReport := &SimpleMockReport{
		config: config,
		adapter: adapter,
	}
	
	// Set count to 1 for single operation
	mockReport.config.Count = 1
	
	return ExecuteChildProcess(mockReport)
}

func executeSingleDirectoryOp(adapter *MixedReportAdapter, setNum, opNum int) error {
	config := adapter.GetConfig()
	
	dirName := fmt.Sprintf("mixed_dir_%d_%d_%d", os.Getpid(), setNum, opNum)
	dirPath := fmt.Sprintf("%s/%s", config.Dir, dirName)
	
	if config.Verbose {
		log.Printf("  ディレクトリ作成/削除: %s", dirPath)
	}

	// Create directory
	err := os.Mkdir(dirPath, 0755)
	if err != nil {
		return err
	}
	
	// Wait a bit
	time.Sleep(100 * time.Millisecond)
	
	// Delete directory
	err = os.Remove(dirPath)
	if err == nil && config.Verbose {
		log.Printf("  ディレクトリ作成/削除完了: %s", dirPath)
	}
	return err
}

func executeRandomOperation(adapter *MixedReportAdapter, setNum, opNum int) error {
	// Choose a random operation
	operations := []string{"write", "read", "delete", "rename", "dir"}
	rand.Seed(time.Now().UnixNano())
	opType := operations[rand.Intn(len(operations))]
	
	config := adapter.GetConfig()
	if config.Verbose {
		log.Printf("  ランダム操作: %s", opType)
	}

	switch opType {
	case "write":
		return executeSingleFileWrite(adapter, setNum, opNum)
	case "read":
		return executeSingleFileRead(adapter, setNum, opNum)
	case "delete":
		return executeSingleFileDelete(adapter, setNum, opNum)
	case "rename":
		return executeSingleFileRename(adapter, setNum, opNum)
	case "dir":
		return executeSingleDirectoryOp(adapter, setNum, opNum)
	default:
		return executeSingleFileWrite(adapter, setNum, opNum)
	}
}

// SimpleMockReport implements ProcessReport for single child process operations
type SimpleMockReport struct {
	config ProcessConfig
	adapter *MixedReportAdapter
}

func (m *SimpleMockReport) GetConfig() ProcessConfig {
	return m.config
}

func (m *SimpleMockReport) IncrementSuccess() {
	// The actual counting is handled by the main report
}

func (m *SimpleMockReport) IncrementFailed() {
	// The actual counting is handled by the main report
}

func (m *SimpleMockReport) AddError(err error) {
	// Errors are handled by the main report
}

func (m *SimpleMockReport) SetTotalOps(count int) {
	// Total ops are managed by the main report
}

func (m *SimpleMockReport) AddChildPID(pid int) {
	m.adapter.AddChildPID(pid)
}